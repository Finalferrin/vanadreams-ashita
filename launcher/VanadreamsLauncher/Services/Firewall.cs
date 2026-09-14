using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>Windows Firewall allow rules for the loader and the game, checked and added.</summary>
    public static class Firewall
    {
        /// <summary>The programs that appear in inbound allow rules, lower-cased full paths.</summary>
        public static HashSet<string> AllowedPrograms(string netshShowRuleOutput)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string program = null; string dir = null; string action = null; string enabled = null;
            Action flush = () =>
            {
                if (program != null && string.Equals(dir, "In", StringComparison.OrdinalIgnoreCase) && string.Equals(action, "Allow", StringComparison.OrdinalIgnoreCase) && !string.Equals(enabled, "No", StringComparison.OrdinalIgnoreCase))
                    set.Add(program);
                program = dir = action = enabled = null;
            };
            foreach (var raw in (netshShowRuleOutput ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Rule Name:", StringComparison.OrdinalIgnoreCase)) { flush(); continue; }
                var idx = line.IndexOf(':');
                if (idx < 0) continue;
                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim();
                if (key.Equals("Program", StringComparison.OrdinalIgnoreCase)) program = Environment.ExpandEnvironmentVariables(val);
                else if (key.Equals("Direction", StringComparison.OrdinalIgnoreCase)) dir = val;
                else if (key.Equals("Action", StringComparison.OrdinalIgnoreCase)) action = val;
                else if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) enabled = val;
            }
            flush();
            return set;
        }

        public static string ReadRules()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=all verbose")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.Default
                };
                using (var p = Process.Start(psi))
                {
                    var text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    return text;
                }
            }
            catch (Exception ex) { Log.Warn("netsh show rule failed: " + ex.Message); return ""; }
        }

        public static Dictionary<string, bool> Check(IEnumerable<string> exePaths)
        {
            var allowed = AllowedPrograms(ReadRules());
            return exePaths.ToDictionary(p => p, p => allowed.Contains(Path.GetFullPath(p)), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Add inbound allow rules through one elevated prompt. Returns false if the prompt was refused.</summary>
        public static bool AddAllowRules(IEnumerable<string> exePaths)
        {
            var cmds = exePaths.Select(p => $"netsh advfirewall firewall add rule name=\"Vanadreams - {Path.GetFileName(p)}\" dir=in action=allow program=\"{Path.GetFullPath(p)}\" enable=yes profile=private,public");
            var script = string.Join(" & ", cmds);
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + script) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi)) { p.WaitForExit(30000); return p.ExitCode == 0; }
            }
            catch (Exception ex) { Log.Warn("firewall add refused or failed: " + ex.Message); return false; }
        }
    }
}
