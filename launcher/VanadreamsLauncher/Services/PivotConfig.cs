using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>
    /// XIPivot's config\pivot\pivot.ini: a [settings] block with the DATs root, then an [overlays] block
    /// listing overlay folder names in priority order. This adds and removes names without disturbing
    /// anything else in the file.
    /// </summary>
    public static class PivotConfig
    {
        public static string IniPath(string ashitaRoot) => Path.Combine(ashitaRoot, "config", "pivot", "pivot.ini");
        public static string OverlaysRoot(string ashitaRoot) => Path.Combine(ashitaRoot, "polplugins", "DATs");

        public static List<string> ReadOverlays(string ashitaRoot)
        {
            var ini = IniPath(ashitaRoot);
            if (!File.Exists(ini)) return new List<string>();
            var list = new List<string>();
            var inOverlays = false;
            foreach (var raw in File.ReadAllLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith("[")) { inOverlays = line.Equals("[overlays]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inOverlays || line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var name = line.Substring(eq + 1).Trim();
                if (name.Length > 0) list.Add(name);
            }
            return list;
        }

        /// <summary>Drop a stray root_path an older launcher wrote; the plugin's own default is the right one.</summary>
        public static void RemoveRootPath(string ashitaRoot)
        {
            var ini = IniPath(ashitaRoot);
            if (!File.Exists(ini)) return;
            var lines = File.ReadAllLines(ini);
            var kept = lines.Where(l => !l.TrimStart().StartsWith("root_path", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (kept.Length != lines.Length) File.WriteAllLines(ini, kept, new UTF8Encoding(false));
        }

        /// <summary>Make sure the overlay is listed. New overlays go last, so existing texture packs keep priority.</summary>
        public static void AddOverlay(string ashitaRoot, string name)
        {
            var list = ReadOverlays(ashitaRoot);
            if (list.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            list.Add(name);
            Write(ashitaRoot, list);
        }

        public static void RemoveOverlay(string ashitaRoot, string name)
        {
            var list = ReadOverlays(ashitaRoot);
            if (list.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0) Write(ashitaRoot, list);
        }

        private static void Write(string ashitaRoot, List<string> overlays)
        {
            var ini = IniPath(ashitaRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(ini));
            var kept = new List<string>();     // every line outside [overlays], as it was
            var seenSettings = false;
            if (File.Exists(ini))
            {
                var inOverlays = false;
                foreach (var raw in File.ReadAllLines(ini))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[")) { inOverlays = line.Equals("[overlays]", StringComparison.OrdinalIgnoreCase); if (line.Equals("[settings]", StringComparison.OrdinalIgnoreCase)) seenSettings = true; if (inOverlays) continue; }
                    if (!inOverlays) kept.Add(raw);
                }
            }
            var sb = new StringBuilder();
            // No root_path: XIPivot defaults to the DATs folder beside pivot.dll, and a relative value
            // here resolves against the game's working directory instead, which breaks every overlay.
            if (!seenSettings)
            {
                sb.AppendLine("[settings]");
                sb.AppendLine("debug_log = false");
            }
            foreach (var l in kept) sb.AppendLine(l);
            if (kept.Count > 0 && kept[kept.Count - 1].Trim().Length > 0) sb.AppendLine();
            sb.AppendLine("[overlays]");
            for (var i = 0; i < overlays.Count; i++) sb.AppendLine(i + " = " + overlays[i]);
            File.WriteAllText(ini, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
