using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Vanadreams.Services
{
    /// <summary>
    /// Windows Firewall allow rules for the loader and the game, checked and added.
    /// The check reads the rules from the registry, where they are stored as language-independent
    /// key=value strings, so it works the same on every Windows language; netsh's text output is
    /// kept only as the fallback when the registry cannot be read.
    /// </summary>
    public static class Firewall
    {
        private const string RulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

        /// <summary>Programs with an enabled inbound allow rule, from the registry: full paths, case-insensitive.</summary>
        public static HashSet<string> AllowedProgramsFromRegistry()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var key = Registry.LocalMachine.OpenSubKey(RulesKey))
            {
                if (key == null) throw new InvalidOperationException("firewall rules key not readable");
                foreach (var name in key.GetValueNames())
                {
                    var rule = key.GetValue(name) as string;
                    if (rule == null) continue;
                    var program = ParseRegistryRule(rule);
                    if (program != null) set.Add(program);
                }
            }
            return set;
        }

        /// <summary>One registry rule string such as "v2.31|Action=Allow|Active=TRUE|Dir=In|App=C:\x.exe|Name=..|" to its program, or null.</summary>
        public static string ParseRegistryRule(string rule)
        {
            string app = null, action = null, active = null, dir = null;
            foreach (var part in (rule ?? "").Split('|'))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var k = part.Substring(0, eq); var v = part.Substring(eq + 1);
                if (k.Equals("App", StringComparison.OrdinalIgnoreCase)) app = Environment.ExpandEnvironmentVariables(v);
                else if (k.Equals("Action", StringComparison.OrdinalIgnoreCase)) action = v;
                else if (k.Equals("Active", StringComparison.OrdinalIgnoreCase)) active = v;
                else if (k.Equals("Dir", StringComparison.OrdinalIgnoreCase)) dir = v;
            }
            var ok = app != null && "Allow".Equals(action, StringComparison.OrdinalIgnoreCase) && "In".Equals(dir, StringComparison.OrdinalIgnoreCase) && !"FALSE".Equals(active, StringComparison.OrdinalIgnoreCase);
            return ok ? app : null;
        }

        /// <summary>The programs that appear in inbound allow rules in netsh's English output; the fallback parser.</summary>
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
                // netsh writes the console's OEM code page, not the ANSI one
                var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                var psi = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=all verbose")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = oem
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
            HashSet<string> allowed;
            try { allowed = AllowedProgramsFromRegistry(); }
            catch (Exception ex) { Log.Warn("firewall registry read failed, using netsh: " + ex.Message); allowed = AllowedPrograms(ReadRules()); }
            return exePaths.ToDictionary(p => p, p => allowed.Contains(Path.GetFullPath(p)), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Add inbound allow rules through one elevated prompt, skipping any that already exist. Returns false if the prompt was refused.</summary>
        public static bool AddAllowRules(IEnumerable<string> exePaths)
        {
            var present = Check(exePaths);
            var wanted = exePaths.Where(p => !present[p]).ToList();
            if (wanted.Count == 0) return true;
            var cmds = wanted.Select(p => $"netsh advfirewall firewall add rule name=\"Vanadreams - {Path.GetFileName(p)}\" dir=in action=allow program=\"{Path.GetFullPath(p)}\" enable=yes profile=private,public");
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
