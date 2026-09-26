using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vanadreams.Services
{
    public enum WindowMode { Registry = -1, Fullscreen = 0, Windowed = 1, Borderless = 2, FullscreenWindowed = 3 }

    /// <summary>One Ashita v4 boot profile: a config\boot\*.ini file.</summary>
    public sealed class Profile
    {
        public string Path { get; set; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public string Id => System.IO.Path.GetFileNameWithoutExtension(Path);
        public bool IsExample => Id.StartsWith("example", StringComparison.OrdinalIgnoreCase);
        /// <summary>A retail profile: PlayOnline's own quick-play command, launched through PlayOnline Viewer.</summary>
        public bool IsRetail => (Command.Extra ?? "").IndexOf("/game", StringComparison.OrdinalIgnoreCase) >= 0
                                || BootFile.EndsWith(@"PlayOnlineViewer\pol.exe", StringComparison.OrdinalIgnoreCase);

        public string Name { get; set; } = "";
        public bool AutoClose { get; set; } = true;
        public string BootFile { get; set; } = "";
        public string Script { get; set; } = "";
        public LoaderCommand Command { get; set; } = new LoaderCommand();
        public int Width { get; set; } = -1;
        public int Height { get; set; } = -1;
        public int MenuWidth { get; set; } = -1;
        public int MenuHeight { get; set; } = -1;
        /// <summary>Background resolution (registry 0003/0004): the size the game draws the world at. -1 is the game's default.</summary>
        public int BackgroundWidth { get; set; } = -1;
        public int BackgroundHeight { get; set; } = -1;
        public WindowMode Mode { get; set; } = WindowMode.Registry;
        public Dictionary<string, bool> PolPlugins { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public IniFile Ini { get; private set; }

        public static Profile Load(string path)
        {
            var ini = IniFile.Load(path);
            var p = new Profile { Path = path, Ini = ini };
            p.Name = ini.Get("ashita.launcher", "name", "");
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = p.Id;
            p.AutoClose = ini.Get("ashita.launcher", "autoclose", "1") != "0";
            p.BootFile = UnescapePath(ini.Get("ashita.boot", "file", ""));
            p.Script = ini.Get("ashita.boot", "script", "");
            p.Command = LoaderCommand.Parse(ini.Get("ashita.boot", "command", ""));
            p.Width = ReadInt(ini, "0001"); p.Height = ReadInt(ini, "0002");
            p.MenuWidth = ReadInt(ini, "0037"); p.MenuHeight = ReadInt(ini, "0038");
            p.BackgroundWidth = ReadInt(ini, "0003"); p.BackgroundHeight = ReadInt(ini, "0004");
            var mode = ReadInt(ini, "0034");
            p.Mode = Enum.IsDefined(typeof(WindowMode), mode) ? (WindowMode)mode : WindowMode.Registry;
            foreach (var kv in ini.Section("ashita.polplugins")) p.PolPlugins[kv.Key] = kv.Value.Trim() == "1";
            return p;
        }

        /// <summary>Write the profile back into its ini, touching only the keys the launcher owns.</summary>
        public void Save(string path = null)
        {
            if (Ini == null) Ini = IniFile.FromText("");
            Ini.Set("ashita.launcher", "name", Name);
            Ini.Set("ashita.launcher", "autoclose", AutoClose ? "1" : "0");
            Ini.Set("ashita.boot", "file", EscapePath(BootFile));
            Ini.Set("ashita.boot", "command", Command.ToIniCommand());
            Ini.Set("ashita.boot", "script", Script ?? "");
            if (!Ini.Has("ashita.boot", "gamemodule")) Ini.Set("ashita.boot", "gamemodule", "ffximain.dll");
            Ini.Set("ffxi.registry", "0001", Width.ToString());
            Ini.Set("ffxi.registry", "0002", Height.ToString());
            Ini.Set("ffxi.registry", "0037", MenuWidth.ToString());
            Ini.Set("ffxi.registry", "0038", MenuHeight.ToString());
            Ini.Set("ffxi.registry", "0003", BackgroundWidth.ToString());
            Ini.Set("ffxi.registry", "0004", BackgroundHeight.ToString());
            Ini.Set("ffxi.registry", "0034", ((int)Mode).ToString());
            foreach (var kv in PolPlugins) Ini.Set("ashita.polplugins", kv.Key, kv.Value ? "1" : "0");
            Ini.Save(path ?? Path);
            if (path != null) Path = path;
        }

        /// <summary>A copy of this profile written to a new file, ready to be edited on its own.</summary>
        public Profile DuplicateTo(string newPath, string newName)
        {
            File.Copy(Path, newPath, false);
            var copy = Load(newPath);
            copy.Name = newName;
            copy.Save();
            return copy;
        }

        public static string EscapePath(string p) => string.IsNullOrWhiteSpace(p) ? "" : p.Replace("\\", "\\\\");
        public static string UnescapePath(string p) => string.IsNullOrWhiteSpace(p) ? "" : p.Replace("\\\\", "\\");

        private static int ReadInt(IniFile ini, string key)
        {
            int v; return int.TryParse(ini.Get("ffxi.registry", key, "-1"), out v) ? v : -1;
        }
    }

    /// <summary>All the profiles in an Ashita folder.</summary>
    public sealed class ProfileStore
    {
        public string AshitaRoot { get; }
        public string BootDir => System.IO.Path.Combine(AshitaRoot, "config", "boot");

        public ProfileStore(string ashitaRoot) { AshitaRoot = ashitaRoot; }

        public IList<Profile> LoadAll()
        {
            if (!Directory.Exists(BootDir)) return new List<Profile>();
            return Directory.GetFiles(BootDir, "*.ini")
                .Where(f => !System.IO.Path.GetFileName(f).StartsWith(".launch-", StringComparison.OrdinalIgnoreCase))   // GameLauncher's temp copies carry a login
                .Select(Profile.Load)
                .OrderBy(p => p.IsExample ? 1 : 0)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string PathFor(string id) => System.IO.Path.Combine(BootDir, id + ".ini");

        public bool Exists(string id) => File.Exists(PathFor(id));

        /// <summary>The file the launcher copies when a player asks for a new profile.</summary>
        public string TemplatePath()
        {
            var preferred = PathFor("example-privateserver");
            if (File.Exists(preferred)) return preferred;
            var any = Directory.Exists(BootDir) ? Directory.GetFiles(BootDir, "*.ini").FirstOrDefault() : null;
            return any;
        }

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            return id.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
        }
    }
}
