using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Vanadreams.Services
{
    /// <summary>
    /// An ini file kept as its original lines, so comments, blank lines and keys the
    /// launcher does not know about survive a round trip. Ashita's boot profiles are
    /// read and written through this.
    /// </summary>
    public sealed class IniFile
    {
        private static readonly Regex SectionLine = new Regex(@"^\s*\[(?<name>[^\]]+)\]\s*$");
        private static readonly Regex KeyLine = new Regex(@"^\s*(?<key>[^;=\s][^=]*?)\s*=\s*(?<value>.*?)\s*$");

        public List<string> Lines { get; } = new List<string>();
        public string Path { get; private set; }

        public IniFile() { }

        public static IniFile Load(string path)
        {
            var ini = new IniFile { Path = path };
            ini.Lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
            return ini;
        }

        public static IniFile FromText(string text)
        {
            var ini = new IniFile();
            ini.Lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
            return ini;
        }

        public void Save(string path = null)
        {
            var target = path ?? Path;
            if (string.IsNullOrEmpty(target)) throw new InvalidOperationException("No path to save to.");
            File.WriteAllText(target, string.Join("\r\n", Lines) + "\r\n", new UTF8Encoding(false));
            Path = target;
        }

        public string Get(string section, string key, string fallback = null)
        {
            var inSection = false;
            foreach (var line in Lines)
            {
                var s = SectionLine.Match(line);
                if (s.Success) { inSection = SectionEquals(s.Groups["name"].Value, section); continue; }
                if (!inSection || IsComment(line)) continue;
                var k = KeyLine.Match(line);
                if (k.Success && KeyEquals(k.Groups["key"].Value, key)) return k.Groups["value"].Value;
            }
            return fallback;
        }

        public bool Has(string section, string key) => Get(section, key) != null;

        public IEnumerable<KeyValuePair<string, string>> Section(string section)
        {
            var inSection = false;
            foreach (var line in Lines)
            {
                var s = SectionLine.Match(line);
                if (s.Success) { inSection = SectionEquals(s.Groups["name"].Value, section); continue; }
                if (!inSection || IsComment(line)) continue;
                var k = KeyLine.Match(line);
                if (k.Success) yield return new KeyValuePair<string, string>(k.Groups["key"].Value, k.Groups["value"].Value);
            }
        }

        /// <summary>Set a key in place if it exists, insert it under the section header if not, append the section if that is missing too.</summary>
        public void Set(string section, string key, string value)
        {
            int start = -1, end = Lines.Count;
            for (var i = 0; i < Lines.Count; i++)
            {
                var s = SectionLine.Match(Lines[i]);
                if (!s.Success) continue;
                if (start >= 0) { end = i; break; }
                if (SectionEquals(s.Groups["name"].Value, section)) start = i;
            }
            if (start < 0)
            {
                if (Lines.Count > 0 && Lines[Lines.Count - 1].Trim().Length > 0) Lines.Add("");
                Lines.Add("[" + section + "]");
                Lines.Add(key + " = " + value);
                return;
            }
            for (var i = start + 1; i < end; i++)
            {
                if (IsComment(Lines[i])) continue;
                var k = KeyLine.Match(Lines[i]);
                if (k.Success && KeyEquals(k.Groups["key"].Value, key)) { Lines[i] = key + " = " + value; return; }
            }
            // insert after the last non-blank line of the section so trailing blank lines stay at the end
            var insertAt = end;
            while (insertAt - 1 > start && Lines[insertAt - 1].Trim().Length == 0) insertAt--;
            Lines.Insert(insertAt, key + " = " + value);
        }

        public void Remove(string section, string key)
        {
            var inSection = false;
            for (var i = 0; i < Lines.Count; i++)
            {
                var s = SectionLine.Match(Lines[i]);
                if (s.Success) { inSection = SectionEquals(s.Groups["name"].Value, section); continue; }
                if (!inSection || IsComment(Lines[i])) continue;
                var k = KeyLine.Match(Lines[i]);
                if (k.Success && KeyEquals(k.Groups["key"].Value, key)) { Lines.RemoveAt(i); return; }
            }
        }

        private static bool IsComment(string line) { var t = line.TrimStart(); return t.StartsWith(";") || t.StartsWith("#"); }
        private static bool SectionEquals(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        private static bool KeyEquals(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
