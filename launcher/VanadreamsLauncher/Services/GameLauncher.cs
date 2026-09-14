using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>
    /// Starts Ashita for a profile. When a login is remembered it travels in a temporary copy of the
    /// profile, named per launch, which is removed as soon as Ashita has exited or, failing that, on a
    /// ceiling timer, on launcher exit, and by the sweep at the next start. Nothing with a password is
    /// ever left behind on purpose.
    /// </summary>
    public static class GameLauncher
    {
        private const string TempPrefix = ".launch-";

        public static Process Launch(string ashitaRoot, Profile profile, Credential credential)
        {
            var cli = Path.Combine(ashitaRoot, "Ashita-cli.exe");
            if (!File.Exists(cli)) throw new FileNotFoundException("Ashita-cli.exe is missing from " + ashitaRoot + "; run Repair.", cli);

            var iniName = profile.FileName;
            string temp = null;
            if (credential != null && (!string.IsNullOrEmpty(credential.User) || !string.IsNullOrEmpty(credential.Password)))
            {
                var launchCmd = new LoaderCommand
                {
                    Server = profile.Command.Server, Hairpin = profile.Command.Hairpin, Extra = profile.Command.Extra,
                    User = credential.User, Password = credential.Password
                };
                temp = Path.Combine(Path.GetDirectoryName(profile.Path), TempPrefix + profile.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ini");
                var ini = IniFile.Load(profile.Path);
                ini.Set("ashita.boot", "command", launchCmd.ToLaunchCommand());
                ini.Save(temp);
                iniName = Path.GetFileName(temp);
            }

            // Ashita-cli.exe asks for administrator in its manifest. Starting it through the shell lets
            // Windows raise the UAC prompt; a direct start fails with "requires elevation" instead.
            var psi = new ProcessStartInfo(cli, "\"" + iniName + "\"") { WorkingDirectory = ashitaRoot, UseShellExecute = true };
            Process process;
            try { process = Process.Start(psi); }
            catch (Exception ex)
            {
                Remove(temp);
                var w32 = ex as System.ComponentModel.Win32Exception;
                if (w32 != null && w32.NativeErrorCode == 1223)
                    throw new InvalidOperationException("Windows asked to run Ashita as administrator and the prompt was cancelled. Press Play again and choose Yes.");
                throw;
            }
            Log.Info("Launched " + profile.Name + " via " + iniName);

            if (temp != null)
            {
                var toDelete = temp;
                // Ashita reads the ini in its first moments; gone the instant it exits, or after a minute regardless
                if (process != null)
                {
                    try { process.EnableRaisingEvents = true; process.Exited += (s, e) => Remove(toDelete); }
                    catch (Exception) { /* no handle to the elevated process: the ceiling below covers it */ }
                }
                Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ => Remove(toDelete));
            }
            return process;
        }

        private static void Remove(string path)
        {
            if (path == null) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Warn("temp profile not removed: " + ex.Message); }
        }

        /// <summary>Any launch copies still on disk are removed: at startup, and again when the launcher closes.</summary>
        public static void Sweep(string ashitaRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(ashitaRoot)) return;
                var boot = Path.Combine(ashitaRoot, "config", "boot");
                if (!Directory.Exists(boot)) return;
                foreach (var f in Directory.GetFiles(boot, TempPrefix + "*.ini")) Remove(f);
            }
            catch (Exception) { }
        }
    }
}
