using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Vanadreams.Services
{
    /// <summary>
    /// The launcher is its own installer. Run from a download, it offers to copy itself to a
    /// folder of its own and make Start menu and desktop shortcuts, then hands over to that copy.
    /// </summary>
    public static class SelfInstall
    {
        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vanadreams", "Launcher");
        public static string InstalledExe => Path.Combine(InstallDir, "VanadreamsLauncher.exe");
        public static string CurrentExe => Assembly.GetExecutingAssembly().Location;

        public static bool IsInstalledCopy => string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this exe is running from somewhere a download lands, not from a place it was put on purpose.</summary>
        public static bool LooksLikeADownload()
        {
            if (IsInstalledCopy) return false;
            var dir = Path.GetDirectoryName(CurrentExe) ?? "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var name in new[] { "Downloads", "Desktop" })
                if (dir.StartsWith(Path.Combine(home, name), StringComparison.OrdinalIgnoreCase)) return true;
            if (dir.StartsWith(Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Copy the exe into its folder, make the shortcuts, return the installed path.</summary>
        public static string Install()
        {
            Directory.CreateDirectory(InstallDir);
            File.Copy(CurrentExe, InstalledExe, true);
            var config = CurrentExe + ".config";
            if (File.Exists(config)) File.Copy(config, InstalledExe + ".config", true);
            MakeShortcuts();
            Log.Info("installed to " + InstalledExe);
            return InstalledExe;
        }

        public static void MakeShortcuts()
        {
            var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Vanadreams");
            Directory.CreateDirectory(programs);
            Shortcut(Path.Combine(programs, "Vanadreams Launcher.lnk"));
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Vanadreams Launcher.lnk"));
        }

        private static void Shortcut(string lnkPath)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic lnk = shell.CreateShortcut(lnkPath);
                lnk.TargetPath = InstalledExe;
                lnk.WorkingDirectory = InstallDir;
                lnk.Description = "Vanadreams Launcher";
                lnk.IconLocation = InstalledExe + ",0";
                lnk.Save();
            }
            catch (Exception ex) { Log.Warn("shortcut " + lnkPath + ": " + ex.Message); }
        }

        /// <summary>Start the installed copy and let this one exit.</summary>
        public static void HandOver()
        {
            Process.Start(new ProcessStartInfo(InstalledExe) { WorkingDirectory = InstallDir, UseShellExecute = true });
        }

        /// <summary>Remove the shortcuts and the installed copy; settings and profiles are left alone.</summary>
        public static void Uninstall()
        {
            foreach (var lnk in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Vanadreams", "Vanadreams Launcher.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Vanadreams Launcher.lnk"),
            })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch (Exception) { }
        }
    }
}
