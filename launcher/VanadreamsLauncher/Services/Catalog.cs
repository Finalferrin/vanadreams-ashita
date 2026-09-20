using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vanadreams.Services
{
    public enum SourceType { Bundled, GithubRelease, RepoFolder, None }
    // UnzipToAddons: the archive holds the addon folder itself (Balloon/Balloon.lua), so it unpacks into addons\.
    public enum InstallAction { NothingToInstall, UnzipToRoot, CopyToAddons, PivotOverlay, UnzipToAddons }

    /// <summary>One line of catalog.json: a plugin, addon or POL plugin the picker can offer.</summary>
    public sealed class CatalogItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }            // plugin | addon | polplugin
        public SourceType Source { get; set; }
        public string Repo { get; set; }            // owner/name
        public string Asset { get; set; }           // glob for the release asset
        public string Path { get; set; }            // folder in the repo for repo-folder
        public string Branch { get; set; } = "main";
        public string Version { get; set; }
        public InstallAction Install { get; set; }
        public string Load { get; set; }            // "/load x" | "/addon load x" | "polplugins:x"
        public string Config { get; set; }
        public string Docs { get; set; }
        public string Description { get; set; }
        public string Replacement { get; set; }
        public string Maintainer { get; set; }
        public List<string> ConfigLines { get; set; } = new List<string>();
        public List<string> Conflicts { get; set; } = new List<string>();   // ids this item gives way to while they are enabled
        public string HeldBack { get; set; }        // why this item is never written to the startup script, however it is ticked
        public bool OnByDefault { get; set; }       // installed and ticked for the player once, without being asked; theirs to untick after
        public string V3Name { get; set; }
        public string V3Carry { get; set; }
        public string V3Note { get; set; }

        public bool HasV4 => Source != SourceType.None;

        public LoadKind LoadKind
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Load)) return LoadKind.None;
                if (Load.StartsWith("polplugins:", StringComparison.OrdinalIgnoreCase)) return LoadKind.PolPlugin;
                if (Load.StartsWith("/addon load ", StringComparison.OrdinalIgnoreCase)) return LoadKind.Addon;
                if (Load.StartsWith("/load ", StringComparison.OrdinalIgnoreCase)) return LoadKind.Plugin;
                return LoadKind.None;
            }
        }

        public string LoadName
        {
            get
            {
                switch (LoadKind)
                {
                    case LoadKind.PolPlugin: return Load.Substring("polplugins:".Length).Trim();
                    case LoadKind.Addon: return Load.Substring("/addon load ".Length).Trim();
                    case LoadKind.Plugin: return Load.Substring("/load ".Length).Trim();
                    default: return null;
                }
            }
        }

        /// <summary>The source tag the picker shows on the right of the row.</summary>
        public string SourceTag
        {
            get
            {
                switch (Source)
                {
                    case SourceType.Bundled: return "bundled";
                    case SourceType.None: return "no v4";
                    default: return string.IsNullOrWhiteSpace(Maintainer) ? (Repo ?? "").Split('/').FirstOrDefault() ?? "" : Maintainer;
                }
            }
        }

        public ScriptEntry ToScriptEntry() => new ScriptEntry { Id = Id, Kind = LoadKind, LoadName = LoadName, ConfigLines = ConfigLines };

        /// <summary>Where the item's files land, so "is it installed" can be answered by looking.</summary>
        public string InstalledMarker(string ashitaRoot)
        {
            switch (Kind)
            {
                case "addon": return System.IO.Path.Combine(ashitaRoot, "addons", LoadName ?? Id, (LoadName ?? Id) + ".lua");
                case "polplugin": return System.IO.Path.Combine(ashitaRoot, "polplugins", (LoadName ?? Id) + ".dll");
                case "overlay": return System.IO.Path.Combine(ashitaRoot, "polplugins", "DATs", Id);
                default: return System.IO.Path.Combine(ashitaRoot, "plugins", (LoadName ?? Id) + ".dll");
            }
        }

        /// <summary>
        /// The folder the Config button opens. A catalogue path is under the Ashita folder, unless it starts with
        /// {desktop}, which is the player's real Desktop wherever Windows keeps it.
        /// </summary>
        public static string ResolveConfigPath(string ashitaRoot, string config, string desktop)
        {
            if (string.IsNullOrWhiteSpace(config)) return null;
            const string token = "{desktop}";
            if (config.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                return System.IO.Path.Combine(desktop ?? "", config.Substring(token.Length).TrimStart('\\', '/'));
            return System.IO.Path.Combine(ashitaRoot ?? "", config);
        }

        public bool IsInstalled(string ashitaRoot)
        {
            if (Source == SourceType.None || string.IsNullOrEmpty(ashitaRoot)) return false;
            var marker = InstalledMarker(ashitaRoot);
            if (File.Exists(marker)) return true;
            if (Kind == "overlay") return Directory.Exists(marker) && Directory.EnumerateFileSystemEntries(marker).Any();
            // plugin DLL names are not always lower case on disk
            var dir = System.IO.Path.GetDirectoryName(marker);
            var name = System.IO.Path.GetFileName(marker);
            return Directory.Exists(dir) && Directory.GetFiles(dir).Any(f => string.Equals(System.IO.Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class Catalog
    {
        public int Schema { get; set; }
        public string Interface { get; set; }
        public List<CatalogItem> Items { get; } = new List<CatalogItem>();

        public CatalogItem Find(string id) => Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The enabled item that keeps this one out of the startup script, or null. Two addons that patch the
        /// same part of the game cannot both load in either order; the one that names the conflict gives way.
        /// </summary>
        public CatalogItem BlockedBy(CatalogItem item, IEnumerable<string> enabledIds)
        {
            var enabled = (enabledIds ?? Enumerable.Empty<string>()).ToList();
            return item.Conflicts.Where(c => enabled.Contains(c, StringComparer.OrdinalIgnoreCase))
                                 .Select(Find).FirstOrDefault(other => other != null && other.HasV4);
        }

        /// <summary>
        /// What the enabled set contributes to the startup script, in the order given, without the items held back:
        /// the ones giving way to a conflict, and the ones the catalogue says must never load here (HeldBack).
        /// Every profile the launcher makes runs this one script, so an item that is wrong for Vanadreams is kept
        /// out for good and the catalogue names what to tick instead.
        /// </summary>
        public List<ScriptEntry> ScriptEntries(IEnumerable<string> enabledIds)
        {
            var enabled = (enabledIds ?? Enumerable.Empty<string>()).ToList();
            return enabled.Select(Find)
                          .Where(i => i != null && i.HasV4 && string.IsNullOrEmpty(i.HeldBack) && BlockedBy(i, enabled) == null)
                          .Select(i => i.ToScriptEntry()).ToList();
        }

        public static Catalog Parse(string json)
        {
            var root = Json.ParseObject(json);
            if (root == null) throw new FormatException("The catalogue is not a JSON object.");
            var cat = new Catalog { Schema = Json.Int(root, "schema", 1), Interface = Json.Str(root, "interface", "") };
            foreach (var o in Json.List(root.ContainsKey("items") ? root["items"] : null))
            {
                var d = Json.Obj(o);
                if (d == null) continue;
                var item = new CatalogItem
                {
                    Id = Json.Str(d, "id"),
                    Name = Json.Str(d, "name") ?? Json.Str(d, "id"),
                    Kind = (Json.Str(d, "kind") ?? "addon").ToLowerInvariant(),
                    Version = Json.Str(d, "version"),
                    Load = Json.Str(d, "load"),
                    Config = Json.Str(d, "config"),
                    Docs = Json.Str(d, "docs"),
                    Description = Json.Str(d, "description"),
                    Replacement = Json.Str(d, "replacement"),
                    Maintainer = Json.Str(d, "maintainer"),
                    ConfigLines = Json.Strings(d, "configLines"),
                    Conflicts = Json.Strings(d, "conflicts"),
                    HeldBack = Json.Str(d, "heldBack"),
                    OnByDefault = Json.Bool(d, "onByDefault"),
                };
                var src = Json.Obj(d.ContainsKey("source") ? d["source"] : null);
                var type = (Json.Str(src, "type") ?? "none").ToLowerInvariant();
                switch (type)
                {
                    case "bundled": item.Source = SourceType.Bundled; break;
                    case "github-release": item.Source = SourceType.GithubRelease; break;
                    case "repo-folder": item.Source = SourceType.RepoFolder; break;
                    default: item.Source = SourceType.None; break;
                }
                item.Repo = Json.Str(src, "repo");
                item.Asset = Json.Str(src, "asset");
                item.Path = Json.Str(src, "path");
                item.Branch = Json.Str(src, "branch") ?? "main";
                var install = (Json.Str(d, "install") ?? "").ToLowerInvariant();
                item.Install = install == "unzip-to-root" ? InstallAction.UnzipToRoot
                             : install == "copy-to-addons" ? InstallAction.CopyToAddons
                             : install == "pivot-overlay" ? InstallAction.PivotOverlay
                             : install == "unzip-to-addons" ? InstallAction.UnzipToAddons
                             : InstallAction.NothingToInstall;
                var v3 = Json.Obj(d.ContainsKey("v3") ? d["v3"] : null);
                item.V3Name = Json.Str(v3, "name");
                item.V3Carry = Json.Str(v3, "carry");
                item.V3Note = Json.Str(v3, "note");
                if (!string.IsNullOrWhiteSpace(item.Id)) cat.Items.Add(item);
            }
            return cat;
        }
    }
}
