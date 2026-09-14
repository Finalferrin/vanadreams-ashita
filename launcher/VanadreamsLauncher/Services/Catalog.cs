using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vanadreams.Services
{
    public enum SourceType { Bundled, GithubRelease, RepoFolder, None }
    public enum InstallAction { NothingToInstall, UnzipToRoot, CopyToAddons, PivotOverlay }

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

        public bool IsInstalled(string ashitaRoot)
        {
            if (Source == SourceType.None || string.IsNullOrEmpty(ashitaRoot)) return false;
            var marker = InstalledMarker(ashitaRoot);
            if (File.Exists(marker)) return true;
            if (Kind == "overlay") return Directory.Exists(marker) && Directory.EnumerateFiles(marker, "*", SearchOption.AllDirectories).Any();
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
