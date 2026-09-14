using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Vanadreams.Services
{
    /// <summary>The xiloader command line, taken apart and put back together.</summary>
    public sealed class LoaderCommand
    {
        private static readonly Regex Token = new Regex("\"[^\"]*\"|\\S+");

        public string Server { get; set; } = "";
        public bool Hairpin { get; set; }
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public string Extra { get; set; } = "";

        public static LoaderCommand Parse(string command)
        {
            var c = new LoaderCommand();
            if (string.IsNullOrWhiteSpace(command)) return c;
            var tokens = new List<string>();
            foreach (Match m in Token.Matches(command)) tokens.Add(m.Value.Trim('"'));
            var extra = new List<string>();
            for (var i = 0; i < tokens.Count; i++)
            {
                switch (tokens[i].ToLowerInvariant())
                {
                    case "--server": if (++i < tokens.Count) c.Server = tokens[i]; break;
                    case "--user": case "--username": if (++i < tokens.Count) c.User = tokens[i]; break;
                    case "--pass": case "--password": if (++i < tokens.Count) c.Password = tokens[i]; break;
                    case "--hairpin": c.Hairpin = true; break;
                    default: extra.Add(tokens[i]); break;
                }
            }
            c.Extra = string.Join(" ", extra);
            return c;
        }

        /// <summary>The command line as it goes into a boot ini: never carries credentials.</summary>
        public string ToIniCommand()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Server)) { parts.Add("--server"); parts.Add(Quote(Server)); }
            if (Hairpin) parts.Add("--hairpin");
            if (!string.IsNullOrWhiteSpace(Extra)) parts.Add(Extra.Trim());
            return string.Join(" ", parts);
        }

        /// <summary>The command line handed to xiloader at launch, credentials included.</summary>
        public string ToLaunchCommand()
        {
            var parts = new List<string> { ToIniCommand() };
            if (!string.IsNullOrWhiteSpace(User)) { parts.Add("--user"); parts.Add(Quote(User)); }
            if (!string.IsNullOrWhiteSpace(Password)) { parts.Add("--pass"); parts.Add(Quote(Password)); }
            return string.Join(" ", parts).Trim();
        }

        private static string Quote(string s) => s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s;
    }
}
