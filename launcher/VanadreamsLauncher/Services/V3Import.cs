using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Vanadreams.Services
{
    /// <summary>What an import did, one line per thing, shown to the player afterwards.</summary>
    public sealed class ImportReport
    {
        public List<string> Lines { get; } = new List<string>();
        public List<string> ProfilesWritten { get; } = new List<string>();
        public void Add(string line) => Lines.Add(line);
    }

    /// <summary>Finds an Ashita v3 install and brings its profiles and the carry-over configs into v4.</summary>
    public static class V3Import
    {
        public static bool LooksLikeV3(string folder) =>
            !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "Ashita.exe")) && Directory.Exists(Path.Combine(folder, "config", "boot"));

        public static List<string> FindInstalls()
        {
            var candidates = new List<string>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var root in new[]
            {
                Path.Combine(home, "Desktop"), Path.Combine(home, "Downloads"), Path.Combine(home, "Documents"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"C:\Games", @"D:\Games", @"C:\", @"D:\"
            })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                        if (Path.GetFileName(dir).IndexOf("ashita", StringComparison.OrdinalIgnoreCase) >= 0 && LooksLikeV3(dir) && !candidates.Contains(dir, StringComparer.OrdinalIgnoreCase))
                            candidates.Add(dir);
                }
                catch (Exception) { }
            }
            return candidates;
        }

        /// <summary>A v3 boot XML read into a v4 profile shape. The caller decides the file it is saved as.</summary>
        public static Profile ConvertBootXml(string xmlPath, string v4Root)
        {
            var doc = XDocument.Load(xmlPath);
            var settings = doc.Root.Elements("setting").ToDictionary(e => (string)e.Attribute("name"), e => e.Value, StringComparer.OrdinalIgnoreCase);
            Func<string, string> get = k => settings.ContainsKey(k) ? settings[k] : null;
            var p = new Profile();
            p.Name = get("config_name") ?? Path.GetFileNameWithoutExtension(xmlPath);
            p.AutoClose = string.Equals(get("auto_close"), "True", StringComparison.OrdinalIgnoreCase);
            p.Script = "vanadreams.txt";
            var bootFile = (get("boot_file") ?? "").Replace("\\\\", "\\");
            // v3 kept the loader under ffxi-bootmod; v4 keeps it under bootloader
            p.BootFile = bootFile.IndexOf("xiloader", StringComparison.OrdinalIgnoreCase) >= 0 || bootFile.IndexOf("bootmod", StringComparison.OrdinalIgnoreCase) >= 0
                ? Path.Combine(v4Root, "bootloader", "xiloader.exe")
                : bootFile;
            p.Command = LoaderCommand.Parse(get("boot_command") ?? "");
            // a retail profile whose viewer path is empty or gone gets the one the installer registered
            if (p.IsRetail && (string.IsNullOrWhiteSpace(p.BootFile) || !File.Exists(p.BootFile)))
                p.BootFile = ClientVersion.FindPlayOnlineViewer() ?? p.BootFile;
            int w, h;
            if (int.TryParse(get("window_x"), out w)) { p.Width = w; p.MenuWidth = w; }
            if (int.TryParse(get("window_y"), out h)) { p.Height = h; p.MenuHeight = h; }
            var windowed = string.Equals(get("windowed"), "True", StringComparison.OrdinalIgnoreCase);
            var border = !string.Equals(get("show_border"), "False", StringComparison.OrdinalIgnoreCase);
            p.Mode = !windowed ? WindowMode.Fullscreen : border ? WindowMode.Windowed : WindowMode.Borderless;
            return p;
        }

        /// <summary>Import every v3 boot profile as a v4 ini, copied from the v4 example so every other key is sane.</summary>
        public static ImportReport ImportProfiles(string v3Root, string v4Root, out List<Credential> foundLogins, out List<string> profileIds)
        {
            var report = new ImportReport();
            foundLogins = new List<Credential>();
            profileIds = new List<string>();
            var store = new ProfileStore(v4Root);
            var template = store.TemplatePath();
            if (template == null) { report.Add("No example profile in the v4 folder to copy from; nothing imported."); return report; }
            foreach (var xml in Directory.GetFiles(Path.Combine(v3Root, "config", "boot"), "*.xml"))
            {
                try
                {
                    var converted = ConvertBootXml(xml, v4Root);
                    var id = SafeId(converted.Name);
                    var path = store.PathFor(id);
                    if (File.Exists(path)) { report.Add($"Skipped {converted.Name}: a v4 profile named {id} already exists."); continue; }
                    File.Copy(template, path);
                    var p = Profile.Load(path);
                    p.Name = converted.Name; p.AutoClose = converted.AutoClose; p.BootFile = converted.BootFile; p.Script = converted.Script;
                    p.Width = converted.Width; p.Height = converted.Height; p.MenuWidth = converted.MenuWidth; p.MenuHeight = converted.MenuHeight; p.Mode = converted.Mode;
                    var cmd = converted.Command;
                    Credential login = null;
                    if (!string.IsNullOrEmpty(cmd.User) || !string.IsNullOrEmpty(cmd.Password))
                    {
                        login = new Credential { User = cmd.User, Password = cmd.Password };
                        report.Add($"{converted.Name}: login moved out of the profile into the protected store.");
                    }
                    p.Command = new LoaderCommand { Server = cmd.Server, Hairpin = cmd.Hairpin, Extra = cmd.Extra };
                    p.Save();
                    report.ProfilesWritten.Add(path);
                    // the two lists stay in step: a profile without a login gets a null in its place
                    profileIds.Add(id);
                    foundLogins.Add(login);
                    report.Add($"Profile {converted.Name} written as {id}.ini.");
                }
                catch (Exception ex)
                {
                    report.Add($"Could not convert {Path.GetFileName(xml)}: {ex.Message}");
                }
            }
            return report;
        }

        /// <summary>Copy the configs the map says carry over. Each block is independent; a missing source is simply reported.</summary>
        public static ImportReport ImportConfigs(string v3Root, string v4Root)
        {
            var report = new ImportReport();
            CopyFile(Path.Combine(v3Root, "config", "MultiSend.xml"), Path.Combine(v4Root, "config", "MultiSend.xml"), "MultiSend groups", report);
            CopyTree(Path.Combine(v3Root, "config", "Lootwhore"), Path.Combine(v4Root, "config", "lootwhore", "profiles"), "Lootwhore profiles", report);
            CopyTree(Path.Combine(v3Root, "config", "Ashitacast"), Path.Combine(v4Root, "config", "LegacyAC"), "Ashitacast XMLs into LegacyAC", report);
            var dats = Path.Combine(v3Root, "plugins", "DATs");
            if (Directory.Exists(dats))
            {
                CopyTree(dats, PivotConfig.OverlaysRoot(v4Root), "XIPivot overlays", report);
                var overlays = ReadPivotOverlays(Path.Combine(v3Root, "config", "XIPivot.xml"));
                if (overlays.Count > 0)
                {
                    // added to whatever pivot.ini already holds, never written from scratch
                    foreach (var name in overlays) PivotConfig.AddOverlay(v4Root, name);
                    report.Add($"XIPivot config carries {overlays.Count} overlay(s) from v3: {string.Join(", ", overlays)}.");
                }
            }
            return report;
        }

        public static List<string> ReadPivotOverlays(string xipivotXml)
        {
            var list = new List<string>();
            if (!File.Exists(xipivotXml)) return list;
            try
            {
                var doc = XDocument.Load(xipivotXml);
                var node = doc.Root.Elements("setting").FirstOrDefault(e => string.Equals((string)e.Attribute("name"), "overlays", StringComparison.OrdinalIgnoreCase));
                if (node != null) list.AddRange(node.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            }
            catch (Exception) { }
            return list;
        }

        public static string SafeId(string name)
        {
            var cleaned = Regex.Replace(name ?? "profile", @"[^A-Za-z0-9 _\-\.]", "").Trim();
            return string.IsNullOrEmpty(cleaned) ? "profile" : cleaned;
        }

        private static void CopyFile(string src, string dst, string what, ImportReport report)
        {
            if (!File.Exists(src)) { report.Add($"{what}: nothing to copy."); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            report.Add($"{what}: copied.");
        }

        private static void CopyTree(string src, string dst, string what, ImportReport report)
        {
            if (!Directory.Exists(src)) { report.Add($"{what}: nothing to copy."); return; }
            var count = 0;
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(src.Length).TrimStart('\\', '/');
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                count++;
            }
            report.Add($"{what}: {count} file(s) copied.");
        }
    }
}
