using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>What the launcher remembers between runs. Lives in %LOCALAPPDATA%\Vanadreams\settings.json.</summary>
    public sealed class LauncherSettings
    {
        public static string DataFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vanadreams");
        public static string DefaultPath => System.IO.Path.Combine(DataFolder, "settings.json");

        public string Path { get; private set; }
        public string AshitaRoot { get; set; } = "";
        public string FfxiFolderOverride { get; set; } = "";
        public string LastProfile { get; set; } = "";
        public string CatalogUrl { get; set; } = "https://raw.githubusercontent.com/Finalferrin/vanadreams-ashita/main/catalog.json";
        public string StatusUrl { get; set; } = ServerStatusClient.DefaultUrl;
        public string CaptureUrl { get; set; } = "https://fairywitch.ca/api/public/vanadreams/capture";
        public string ExpectedClientVer { get; set; } = "30260805_0";
        public int VerLock { get; set; } = 2;
        public bool SetupDone { get; set; }
        public List<string> EnabledAddons { get; set; } = new List<string>();
        public Dictionary<string, string> InstalledVersions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LastPlayed { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> GuideDone { get; set; } = new List<string>();

        public static LauncherSettings Load(string path = null)
        {
            var s = new LauncherSettings { Path = path ?? DefaultPath };
            if (!File.Exists(s.Path)) return s;
            try
            {
                var d = Json.ParseObject(File.ReadAllText(s.Path, Encoding.UTF8));
                if (d == null) return s;
                s.AshitaRoot = Json.Str(d, "ashitaRoot", "");
                s.FfxiFolderOverride = Json.Str(d, "ffxiFolderOverride", "");
                s.LastProfile = Json.Str(d, "lastProfile", "");
                s.CatalogUrl = Json.Str(d, "catalogUrl", s.CatalogUrl);
                s.StatusUrl = Json.Str(d, "statusUrl", s.StatusUrl);
                s.CaptureUrl = Json.Str(d, "captureUrl", s.CaptureUrl);
                s.ExpectedClientVer = Json.Str(d, "expectedClientVer", s.ExpectedClientVer);
                s.VerLock = Json.Int(d, "verLock", 2);
                s.SetupDone = Json.Bool(d, "setupDone");
                s.EnabledAddons = Json.Strings(d, "enabledAddons");
                s.GuideDone = Json.Strings(d, "guideDone");
                var inst = Json.Obj(d.ContainsKey("installedVersions") ? d["installedVersions"] : null);
                if (inst != null) foreach (var kv in inst) s.InstalledVersions[kv.Key] = Convert.ToString(kv.Value);
                var last = Json.Obj(d.ContainsKey("lastPlayed") ? d["lastPlayed"] : null);
                if (last != null) foreach (var kv in last) s.LastPlayed[kv.Key] = Convert.ToString(kv.Value);
            }
            catch (Exception) { /* unreadable settings start fresh */ }
            return s;
        }

        public void Save()
        {
            var d = new Dictionary<string, object>
            {
                { "ashitaRoot", AshitaRoot ?? "" },
                { "ffxiFolderOverride", FfxiFolderOverride ?? "" },
                { "lastProfile", LastProfile ?? "" },
                { "catalogUrl", CatalogUrl },
                { "statusUrl", StatusUrl },
                { "captureUrl", CaptureUrl },
                { "expectedClientVer", ExpectedClientVer },
                { "verLock", VerLock },
                { "setupDone", SetupDone },
                { "enabledAddons", EnabledAddons.ToList() },
                { "guideDone", GuideDone.ToList() },
                { "installedVersions", InstalledVersions.ToDictionary(k => k.Key, k => (object)k.Value) },
                { "lastPlayed", LastPlayed.ToDictionary(k => k.Key, k => (object)k.Value) },
            };
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, Json.Stringify(d), new UTF8Encoding(false));
        }

        public bool HasAshita => !string.IsNullOrWhiteSpace(AshitaRoot) && File.Exists(System.IO.Path.Combine(AshitaRoot, "Ashita-cli.exe"));
        public string CredentialsPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "credentials.dat");
        public string CatalogCachePath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "catalog.cache.json");
        public string StatusCachePath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "status.cache.json");
        public string DownloadsFolder => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "downloads");
        public string LogPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "launcher.log");
    }
}
