using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>
    /// Keeps the installed launcher current. Two paths:
    ///  * a downloaded exe that is started from Downloads (or the desktop, or a temp folder) while an
    ///    installed copy exists refreshes that copy if it is older and hands over to it, so clicking a
    ///    download always lands in the one true launcher;
    ///  * the running launcher checks the GitHub release once per start, downloads a newer exe, checks
    ///    its signature, swaps it into place and offers a restart.
    /// A running exe cannot be overwritten on Windows, but it can be renamed; the old one becomes .old
    /// and is swept on the next start.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "VanaDreams/vanadreams-ashita";
        public const string AssetName = "VanadreamsLauncher.exe";
        public const string Signer = "CN=Lee Hattery";

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>"v0.2.8" or "0.2.8" to a Version, or null.</summary>
        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var t = tag.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
            var plus = t.IndexOf('+'); if (plus >= 0) t = t.Substring(0, plus);
            Version v;
            return Version.TryParse(t, out v) ? Normalise(v) : null;
        }

        /// <summary>Versions compare on major.minor.build; the assembly carries a fourth part the tag never does.</summary>
        public static Version Normalise(Version v) => v == null ? null : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        public static bool IsNewer(Version candidate, Version current) => candidate != null && current != null && candidate > Normalise(current);

        public static Version FileVersion(string exe)
        {
            try { return ParseTag(FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? FileVersionInfo.GetVersionInfo(exe).FileVersion); }
            catch (Exception) { return null; }
        }

        /// <summary>The exe carries a signature whose subject is ours and whose chain Windows trusts.</summary>
        public static bool IsSignedByUs(string exe)
        {
            try
            {
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(exe)))
                {
                    if (cert.Subject.IndexOf(Signer, StringComparison.OrdinalIgnoreCase) < 0) return false;
                    using (var chain = new X509Chain())
                    {
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        return chain.Build(cert);
                    }
                }
            }
            catch (Exception ex) { Log.Warn("signature check " + Path.GetFileName(exe) + ": " + ex.Message); return false; }
        }

        /// <summary>A newer release than this build, or null.</summary>
        public static async Task<ReleaseAsset> CheckAsync(Downloader downloader)
        {
            try
            {
                var asset = await downloader.LatestReleaseAssetAsync(Repo, AssetName);
                if (asset == null) return null;
                var v = ParseTag(asset.Tag);
                return IsNewer(v, Current) ? asset : null;
            }
            catch (Exception ex) { Log.Warn("update check: " + ex.Message); return null; }
        }

        /// <summary>Fetch the release exe into the downloads folder and make sure it is ours. Returns the path, or null.</summary>
        public static async Task<string> FetchAsync(Downloader downloader, ReleaseAsset asset, string downloadsFolder)
        {
            var path = Path.Combine(downloadsFolder, "VanadreamsLauncher-" + asset.Tag + ".exe");
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length != asset.Size)
                    await downloader.DownloadFileAsync(asset.Url, path, null, "Launcher " + asset.Tag, asset.Size);
                if (!IsSignedByUs(path)) { Log.Warn("update " + asset.Tag + " is not signed by us; not applied"); try { File.Delete(path); } catch (Exception) { } return null; }
                return path;
            }
            catch (Exception ex) { Log.Warn("update download: " + ex.Message); return null; }
        }

        /// <summary>Put the new exe where this one lives. The running file is renamed aside first. Returns the path that now holds the new version.</summary>
        public static string Apply(string newExe)
        {
            var target = SelfInstall.CurrentExe;
            var aside = target + ".old";
            try { if (File.Exists(aside)) File.Delete(aside); } catch (Exception) { }
            File.Move(target, aside);
            try { File.Copy(newExe, target, true); }
            catch (Exception) { File.Move(aside, target); throw; }
            Log.Info("updated " + target + " from " + Path.GetFileName(newExe));
            return target;
        }

        /// <summary>Start the given exe and let this process go.</summary>
        public static void Restart(string exe)
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>Remove the .old left by a previous update.</summary>
        public static void SweepOld()
        {
            try { var aside = SelfInstall.CurrentExe + ".old"; if (File.Exists(aside)) File.Delete(aside); } catch (Exception) { }
        }

        /// <summary>
        /// Started from a download while an installed copy exists: refresh that copy if this one is newer,
        /// then hand over to it. Returns true when this process should exit.
        /// </summary>
        public static bool HandOverToInstalled()
        {
            if (SelfInstall.IsInstalledCopy || !SelfInstall.LooksLikeADownload() || !File.Exists(SelfInstall.InstalledExe)) return false;
            var installed = FileVersion(SelfInstall.InstalledExe);
            var mine = Normalise(Current);
            try
            {
                if (installed == null || mine > installed)
                {
                    File.Copy(SelfInstall.CurrentExe, SelfInstall.InstalledExe, true);   // the installed copy is not running, or this throws
                    Log.Info("installed copy refreshed to " + mine + " from " + SelfInstall.CurrentExe);
                }
            }
            catch (Exception ex) { Log.Warn("could not refresh the installed copy (" + ex.Message + "); handing over to it as it is"); }
            SelfInstall.HandOver();
            return true;
        }
    }
}
