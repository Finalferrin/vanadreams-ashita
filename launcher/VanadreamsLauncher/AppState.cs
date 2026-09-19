using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanadreams.Services;

namespace Vanadreams
{
    /// <summary>Everything the pages share: settings, stores, the last status and version check.</summary>
    public sealed class AppState
    {
        public LauncherSettings Settings { get; }
        public CredentialStore Credentials { get; }
        public ServerStatusClient StatusClient { get; }
        public Downloader Downloader { get; } = new Downloader();
        public Catalog Catalog { get; private set; } = new Catalog();
        public bool CatalogFromCache { get; private set; }
        public StatusInfo Status { get; private set; } = new StatusInfo();
        public VersionCheck Version { get; private set; } = new VersionCheck { Verdict = VersionVerdict.Unknown };

        public event Action Changed;

        public AppState()
        {
            Settings = LauncherSettings.Load();
            Log.Path = Settings.LogPath;
            Credentials = new CredentialStore(Settings.CredentialsPath);
            StatusClient = new ServerStatusClient(Settings.StatusCachePath) { Url = Settings.StatusUrl };
            Status = StatusClient.ReadCache();
            Status.FromCache = true;
            LoadCatalogFromCache();
            if (Catalog.Items.Count == 0) LoadBundledCatalog();
        }

        public ProfileStore Profiles => new ProfileStore(Settings.AshitaRoot);
        public string AshitaRoot => Settings.AshitaRoot;
        public bool HasAshita => Settings.HasAshita;
        public string FfxiFolder => !string.IsNullOrWhiteSpace(Settings.FfxiFolderOverride) ? Settings.FfxiFolderOverride : ClientVersion.FindFfxiFolder();

        public void Notify() => Changed?.Invoke();

        public VersionCheck CheckVersion()
        {
            var installed = ClientVersion.ReadInstalled(FfxiFolder);
            var published = !string.IsNullOrEmpty(Status.ClientVer);
            var expected = published ? Status.ClientVer : Settings.ExpectedClientVer;
            var lockMode = Status.Lock ?? (Enum.IsDefined(typeof(VersionLock), Settings.VerLock) ? (VersionLock)Settings.VerLock : VersionLock.MatchingOrNewer);
            Version = ClientVersion.Compare(installed, expected, lockMode);
            Version.ExpectedIsPublished = published;
            return Version;
        }

        public async Task RefreshStatusAsync()
        {
            StatusClient.Url = Settings.StatusUrl;
            Status = await StatusClient.FetchAsync();
            if (!string.IsNullOrEmpty(Status.ClientVer)) { Settings.ExpectedClientVer = Status.ClientVer; if (Status.Lock.HasValue) Settings.VerLock = (int)Status.Lock.Value; Settings.Save(); }
            CheckVersion();
            Notify();
        }

        private void LoadCatalogFromCache()
        {
            try
            {
                if (File.Exists(Settings.CatalogCachePath)) { Catalog = Catalog.Parse(File.ReadAllText(Settings.CatalogCachePath, Encoding.UTF8)); CatalogFromCache = true; }
            }
            catch (Exception ex) { Log.Warn("catalogue cache unreadable: " + ex.Message); }
        }

        public async Task RefreshCatalogAsync()
        {
            try
            {
                var text = await Downloader.GetStringAsync(Settings.CatalogUrl);
                var cat = Catalog.Parse(text);
                Catalog = cat;
                CatalogFromCache = false;
                Directory.CreateDirectory(Path.GetDirectoryName(Settings.CatalogCachePath));
                File.WriteAllText(Settings.CatalogCachePath, text, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Warn("catalogue fetch failed, using cache: " + ex.Message);
                CatalogFromCache = true;
                if (Catalog.Items.Count == 0) LoadBundledCatalog();
            }
            Notify();
        }

        /// <summary>The catalogue shipped inside the exe, used before the first successful fetch.</summary>
        private void LoadBundledCatalog()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/assets/catalog.json");
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info == null) return;
                using (var reader = new StreamReader(info.Stream, Encoding.UTF8)) Catalog = Catalog.Parse(reader.ReadToEnd());
            }
            catch (Exception ex) { Log.Warn("bundled catalogue unreadable: " + ex.Message); }
        }

        public int EnabledCount => Settings.EnabledAddons.Count(id => Catalog.Find(id) != null);
        public int UpdateCount => Catalog.Items.Count(i => i.Source == SourceType.GithubRelease && Settings.InstalledVersions.ContainsKey(i.Id) && !string.IsNullOrEmpty(i.Version) && !string.Equals(Settings.InstalledVersions[i.Id], i.Version, StringComparison.OrdinalIgnoreCase));

        public string LoaderVersion()
        {
            try
            {
                var p = Path.Combine(AshitaRoot, "bootloader", "xiloader.exe");
                if (!File.Exists(p)) return "missing";
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                return string.IsNullOrWhiteSpace(v.FileVersion) || v.FileVersion == "0.0" ? "present" : v.FileVersion;
            }
            catch (Exception) { return "present"; }
        }

        public string AshitaUpdated()
        {
            try { var dll = Path.Combine(AshitaRoot, "Ashita.dll"); return File.Exists(dll) ? File.GetLastWriteTime(dll).ToString("yyyy-MM-dd") : "not installed"; }
            catch (Exception) { return ""; }
        }

        /// <summary>Write the startup script and the POL plugin flags from the enabled set.</summary>
        public void ApplyEnabledAddons(Profile profile = null)
        {
            if (!HasAshita) { Log.Warn("apply addons: no Ashita folder set, nothing written"); return; }
            PivotConfig.RemoveRootPath(AshitaRoot);   // a value an earlier launcher wrote that stops overlays loading
            var entries = Catalog.ScriptEntries(Settings.EnabledAddons);
            var scriptPath = Path.Combine(AshitaRoot, "scripts", "vanadreams.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            ScriptWriter.Write(scriptPath, entries);
            // XIPivot overlays: listed in pivot.ini while enabled, dropped from it when not
            foreach (var overlay in Catalog.Items.Where(i => i.Install == InstallAction.PivotOverlay))
            {
                try
                {
                    if (Settings.EnabledAddons.Contains(overlay.Id, StringComparer.OrdinalIgnoreCase) && overlay.IsInstalled(AshitaRoot)) PivotConfig.AddOverlay(AshitaRoot, overlay.Id);
                    else PivotConfig.RemoveOverlay(AshitaRoot, overlay.Id);
                }
                catch (Exception ex) { Log.Warn("pivot.ini " + overlay.Id + ": " + ex.Message); }
            }
            var pol = entries.Where(e => e.Kind == LoadKind.PolPlugin).Select(e => e.LoadName).ToList();
            foreach (var p in profile != null ? new[] { profile } : Profiles.LoadAll().Where(x => !x.IsExample).ToArray())
            {
                var changed = false;
                foreach (var name in pol) if (!p.PolPlugins.ContainsKey(name) || !p.PolPlugins[name]) { p.PolPlugins[name] = true; changed = true; }
                foreach (var key in p.PolPlugins.Keys.ToList())
                    if (p.PolPlugins[key] && !pol.Contains(key, StringComparer.OrdinalIgnoreCase) && Catalog.Items.Any(i => i.LoadKind == LoadKind.PolPlugin && string.Equals(i.LoadName, key, StringComparison.OrdinalIgnoreCase)))
                    { p.PolPlugins[key] = false; changed = true; }
                if (!string.Equals(p.Script, "vanadreams.txt", StringComparison.OrdinalIgnoreCase)) { p.Script = "vanadreams.txt"; changed = true; }
                if (changed) p.Save();
            }
        }
    }
}
