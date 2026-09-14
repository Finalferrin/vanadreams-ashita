using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    /// <summary>Starts Ashita for a profile. Credentials travel in a temporary copy of the profile that is removed once Ashita is up.</summary>
    public static class GameLauncher
    {
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
                temp = Path.Combine(Path.GetDirectoryName(profile.Path), ".launch-" + profile.Id + ".ini");
                var ini = IniFile.Load(profile.Path);
                ini.Set("ashita.boot", "command", launchCmd.ToLaunchCommand());
                ini.Save(temp);
                iniName = Path.GetFileName(temp);
            }

            var psi = new ProcessStartInfo(cli, "\"" + iniName + "\"") { WorkingDirectory = ashitaRoot, UseShellExecute = false };
            var process = Process.Start(psi);
            Log.Info("Launched " + profile.Name + " via " + iniName);

            if (temp != null)
            {
                var toDelete = temp;
                Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ =>
                {
                    try { if (File.Exists(toDelete)) File.Delete(toDelete); } catch (Exception ex) { Log.Warn("temp profile not removed: " + ex.Message); }
                });
            }
            return process;
        }

        /// <summary>Any launch copies left behind by a crash are removed at startup.</summary>
        public static void Sweep(string ashitaRoot)
        {
            try
            {
                var boot = Path.Combine(ashitaRoot, "config", "boot");
                if (!Directory.Exists(boot)) return;
                foreach (var f in Directory.GetFiles(boot, ".launch-*.ini")) File.Delete(f);
            }
            catch (Exception) { }
        }
    }
}
