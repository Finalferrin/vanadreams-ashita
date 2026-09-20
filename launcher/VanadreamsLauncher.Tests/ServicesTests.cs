using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class ServicesTests
    {
        private string _dir;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vdl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup() { try { Directory.Delete(_dir, true); } catch { } }

        private const string CatalogJson = @"{
  ""schema"": 1, ""interface"": ""4.30"",
  ""items"": [
    { ""id"": ""lootwhore"", ""name"": ""Lootwhore"", ""kind"": ""plugin"",
      ""source"": { ""type"": ""github-release"", ""repo"": ""ThornyFFXI/Lootwhore"", ""asset"": ""Lootwhore.*Interface.4.30.zip"" },
      ""version"": ""1.18c"", ""install"": ""unzip-to-root"", ""load"": ""/load lootwhore"", ""configLines"": [""/lw load default""],
      ""v3"": { ""name"": ""Lootwhore"", ""carry"": ""profiles"", ""note"": ""Check drop and store lists."" } },
    { ""id"": ""distance"", ""kind"": ""addon"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load distance"" },
    { ""id"": ""find"", ""kind"": ""addon"", ""source"": { ""type"": ""repo-folder"", ""repo"": ""Sippius/Ashita-v4-addons"", ""path"": ""find"" }, ""install"": ""copy-to-addons"", ""load"": ""/addon load find"" },
    { ""id"": ""pivot"", ""name"": ""XIPivot"", ""kind"": ""polplugin"", ""source"": { ""type"": ""github-release"", ""repo"": ""HealsCodes/XIPivot"", ""asset"": ""XIPivot_Ashita_v4_*.zip"" }, ""install"": ""unzip-to-root"", ""load"": ""polplugins:pivot"" },
    { ""id"": ""crafty"", ""name"": ""Crafty"", ""kind"": ""plugin"", ""source"": { ""type"": ""none"" }, ""replacement"": null }
  ]}";

        [TestMethod]
        public void Catalog_parses_every_source_type_and_resolves_load_kinds()
        {
            var cat = Catalog.Parse(CatalogJson);
            Assert.AreEqual("4.30", cat.Interface);
            Assert.AreEqual(5, cat.Items.Count);
            var lw = cat.Find("lootwhore");
            Assert.AreEqual(SourceType.GithubRelease, lw.Source);
            Assert.AreEqual(InstallAction.UnzipToRoot, lw.Install);
            Assert.AreEqual(LoadKind.Plugin, lw.LoadKind);
            Assert.AreEqual("lootwhore", lw.LoadName);
            Assert.AreEqual("ThornyFFXI", lw.SourceTag);
            Assert.AreEqual("profiles", lw.V3Carry);
            Assert.AreEqual(1, lw.ToScriptEntry().ConfigLines.Count);
            Assert.AreEqual(LoadKind.Addon, cat.Find("distance").LoadKind);
            Assert.AreEqual("bundled", cat.Find("distance").SourceTag);
            Assert.AreEqual(InstallAction.CopyToAddons, cat.Find("find").Install);
            Assert.AreEqual(LoadKind.PolPlugin, cat.Find("pivot").LoadKind);
            Assert.AreEqual("pivot", cat.Find("pivot").LoadName);
            Assert.IsFalse(cat.Find("crafty").HasV4);
            Assert.AreEqual("no v4", cat.Find("crafty").SourceTag);
        }

        [TestMethod]
        public void Catalog_installed_is_answered_by_looking_at_the_folder()
        {
            var cat = Catalog.Parse(CatalogJson);
            var root = Path.Combine(_dir, "ashita");
            Directory.CreateDirectory(Path.Combine(root, "addons", "distance"));
            File.WriteAllText(Path.Combine(root, "addons", "distance", "distance.lua"), "-- addon");
            Directory.CreateDirectory(Path.Combine(root, "plugins"));
            File.WriteAllBytes(Path.Combine(root, "plugins", "Lootwhore.dll"), new byte[] { 1 });
            Assert.IsTrue(cat.Find("distance").IsInstalled(root));
            Assert.IsTrue(cat.Find("lootwhore").IsInstalled(root), "case-insensitive DLL name");
            Assert.IsFalse(cat.Find("find").IsInstalled(root));
            Assert.IsFalse(cat.Find("crafty").IsInstalled(root));
        }

        private const string ConflictJson = @"{ ""schema"": 1, ""items"": [
    { ""id"": ""macrofix"", ""kind"": ""addon"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load macrofix"", ""conflicts"": [""xiui"", ""gone""] },
    { ""id"": ""xiui"", ""name"": ""XIUI"", ""kind"": ""addon"", ""source"": { ""type"": ""github-release"", ""repo"": ""tirem/XIUI"", ""asset"": ""XIUI-*.zip"" }, ""load"": ""/addon load XIUI"" },
    { ""id"": ""gone"", ""kind"": ""addon"", ""source"": { ""type"": ""none"" } }
  ]}";

        [TestMethod]
        public void Catalog_holds_an_item_back_while_one_it_conflicts_with_is_enabled()
        {
            var cat = Catalog.Parse(ConflictJson);
            var macrofix = cat.Find("macrofix");
            Assert.AreEqual("xiui", cat.BlockedBy(macrofix, new[] { "macrofix", "XIUI" })?.Id, "held back by the enabled one, whatever the case of the id");
            Assert.IsNull(cat.BlockedBy(macrofix, new[] { "macrofix" }), "alone, it loads");
            Assert.IsNull(cat.BlockedBy(macrofix, new[] { "macrofix", "gone" }), "an item with no v4 version never loads, so it holds nothing back");
            Assert.IsNull(cat.BlockedBy(cat.Find("xiui"), new[] { "macrofix", "xiui" }), "only the item that names the conflict gives way");
        }

        [TestMethod]
        public void Script_entries_leave_out_the_item_that_is_held_back()
        {
            var cat = Catalog.Parse(ConflictJson);
            var both = ScriptWriter.Build(cat.ScriptEntries(new[] { "macrofix", "xiui" }), "");
            StringAssert.Contains(both, "/addon load XIUI");
            Assert.IsFalse(both.Contains("/addon load macrofix"), "macrofix gives way to XIUI");
            var alone = ScriptWriter.Build(cat.ScriptEntries(new[] { "macrofix" }), "");
            StringAssert.Contains(alone, "/addon load macrofix");
        }

        private const string HeldBackJson = @"{ ""schema"": 1, ""items"": [
    { ""id"": ""chatfix"", ""kind"": ""addon"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load chatfix"", ""heldBack"": ""Breaks chat, tells and menus on Vanadreams."", ""replacement"": ""vanachatfix"" },
    { ""id"": ""vanachatfix"", ""kind"": ""addon"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load vanachatfix"" },
    { ""id"": ""distance"", ""kind"": ""addon"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load distance"" }
  ]}";

        [TestMethod]
        public void A_held_back_item_never_reaches_the_startup_script_however_it_is_ticked()
        {
            var cat = Catalog.Parse(HeldBackJson);
            Assert.AreEqual("Breaks chat, tells and menus on Vanadreams.", cat.Find("chatfix").HeldBack);
            Assert.IsTrue(string.IsNullOrEmpty(cat.Find("distance").HeldBack), "an item that says nothing is not held back");

            var alone = ScriptWriter.Build(cat.ScriptEntries(new[] { "chatfix" }), "");
            Assert.IsFalse(alone.Contains("/addon load chatfix"), "held back even when it is the only thing ticked");

            var all = ScriptWriter.Build(cat.ScriptEntries(new[] { "chatfix", "vanachatfix", "distance" }), "");
            Assert.IsFalse(all.Contains("/addon load chatfix\r") || all.Contains("/addon load chatfix\n") || all.EndsWith("/addon load chatfix"), "the held-back item is left out");
            StringAssert.Contains(all, "/addon load vanachatfix");
            StringAssert.Contains(all, "/addon load distance");
        }

        [TestMethod]
        public void Credentials_round_trip_and_never_store_plain_text()
        {
            var path = Path.Combine(_dir, "credentials.dat");
            var store = new CredentialStore(path);
            store.Set("vanadreams", "Ferrin", "hunter2");
            var raw = File.ReadAllText(path);
            Assert.IsFalse(raw.Contains("hunter2"));
            StringAssert.Contains(raw, "Ferrin");
            var again = new CredentialStore(path);
            Assert.AreEqual("hunter2", again.Get("vanadreams").Password);
            again.Remove("vanadreams");
            Assert.IsNull(new CredentialStore(path).Get("vanadreams"));
        }

        [TestMethod]
        public void Status_parses_the_route_and_reads_the_optional_version_fields()
        {
            var s = StatusInfo.Parse("{\"state\":\"online\",\"checked_at\":\"2026-09-13T21:12:00Z\",\"note\":\"Doors open.\",\"client_ver\":\"30260805_0\",\"ver_lock\":\"2\"}");
            Assert.AreEqual(ServerState.Online, s.State);
            Assert.AreEqual("Doors open.", s.Note);
            Assert.AreEqual("30260805_0", s.ClientVer);
            Assert.AreEqual(VersionLock.MatchingOrNewer, s.Lock);
            Assert.IsTrue(s.CheckedAt.HasValue);
            var bare = StatusInfo.Parse("{\"state\":\"setting-up\"}");
            Assert.AreEqual(ServerState.SettingUp, bare.State);
            Assert.IsNull(bare.ClientVer);
            Assert.AreEqual(ServerState.Unknown, StatusInfo.Parse("garbage").State);
        }

        [TestMethod]
        public void Settings_round_trip()
        {
            var path = Path.Combine(_dir, "settings.json");
            var s = LauncherSettings.Load(path);
            s.AshitaRoot = @"C:\Games\Vanadreams";
            s.EnabledAddons.Add("distance");
            s.InstalledVersions["lootwhore"] = "1.18c";
            s.LastPlayed["vanadreams"] = "2026-09-13T13:55:00";
            s.SetupDone = true;
            s.Save();
            var again = LauncherSettings.Load(path);
            Assert.AreEqual(@"C:\Games\Vanadreams", again.AshitaRoot);
            CollectionAssert.Contains(again.EnabledAddons, "distance");
            Assert.AreEqual("1.18c", again.InstalledVersions["lootwhore"]);
            Assert.IsTrue(again.SetupDone);
            Assert.AreEqual(2, again.VerLock);
        }

        [TestMethod]
        public void Downloader_glob_and_zip_overwrite()
        {
            Assert.IsTrue(Downloader.GlobToRegex("Bellhop.*Interface.4.30.zip").IsMatch("Bellhop.1.22.-.Interface.4.30.zip"));
            Assert.IsFalse(Downloader.GlobToRegex("Bellhop.*Interface.4.30.zip").IsMatch("Bellhop.1.22.-.Interface.4.16.zip"));
            var src = Path.Combine(_dir, "src"); Directory.CreateDirectory(Path.Combine(src, "plugins"));
            File.WriteAllText(Path.Combine(src, "plugins", "Bellhop.dll"), "new");
            File.WriteAllText(Path.Combine(src, "readme.txt"), "r");
            var zip = Path.Combine(_dir, "b.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(src, zip);
            var dst = Path.Combine(_dir, "ashita"); Directory.CreateDirectory(Path.Combine(dst, "plugins"));
            File.WriteAllText(Path.Combine(dst, "plugins", "Bellhop.dll"), "old");
            var written = Downloader.ExtractZipOverwrite(zip, dst);
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(dst, "plugins", "Bellhop.dll")));
            Assert.AreEqual(2, written.Count);
        }

        [TestMethod]
        public void Firewall_parser_reads_netsh_verbose_output()
        {
            var text = "Rule Name: Vanadreams - xiloader.exe\r\n----------------------------------------------------------------------\r\nEnabled: Yes\r\nDirection: In\r\nProfiles: Private,Public\r\nAction: Allow\r\nProgram: C:\\Games\\Vanadreams\\bootloader\\xiloader.exe\r\n\r\nRule Name: Blocked thing\r\nEnabled: Yes\r\nDirection: In\r\nAction: Block\r\nProgram: C:\\bad.exe\r\n\r\nRule Name: Out only\r\nEnabled: Yes\r\nDirection: Out\r\nAction: Allow\r\nProgram: C:\\out.exe\r\n";
            var allowed = Firewall.AllowedPrograms(text);
            Assert.IsTrue(allowed.Contains(@"C:\Games\Vanadreams\bootloader\xiloader.exe"));
            Assert.IsFalse(allowed.Contains(@"C:\bad.exe"));
            Assert.IsFalse(allowed.Contains(@"C:\out.exe"));
        }

        [TestMethod]
        public void V3Import_converts_a_real_boot_xml_and_moves_the_login_out()
        {
            var v3 = Path.Combine(_dir, "v3"); Directory.CreateDirectory(Path.Combine(v3, "config", "boot"));
            File.WriteAllText(Path.Combine(v3, "Ashita.exe"), "");
            File.WriteAllText(Path.Combine(v3, "config", "boot", "New Configuration 5.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><settings>" +
                "<setting name=\"config_name\">VanaDreams</setting><setting name=\"auto_close\">False</setting><setting name=\"windowed\">True</setting><setting name=\"show_border\">False</setting>" +
                "<setting name=\"window_x\">1920</setting><setting name=\"window_y\">1080</setting>" +
                "<setting name=\"boot_file\">C:\\Users\\final\\Desktop\\Ashita\\ffxi-bootmod\\xiloader.exe</setting>" +
                "<setting name=\"boot_command\">--server 192.168.0.12 --hairpin --user Ferrin --pass secret</setting><setting name=\"startup_script\">Default.txt</setting></settings>");
            Directory.CreateDirectory(Path.Combine(v3, "config", "Ashitacast")); File.WriteAllText(Path.Combine(v3, "config", "Ashitacast", "Ferrin_WAR.xml"), "<ashitacast/>");
            File.WriteAllText(Path.Combine(v3, "config", "MultiSend.xml"), "<groups/>");
            Directory.CreateDirectory(Path.Combine(v3, "plugins", "DATs", "HD-Remake", "ROM")); File.WriteAllText(Path.Combine(v3, "plugins", "DATs", "HD-Remake", "ROM", "0.dat"), "x");
            File.WriteAllText(Path.Combine(v3, "config", "XIPivot.xml"), "<settings><setting name=\"overlays\">HD-Remake,Other</setting></settings>");

            var v4 = Path.Combine(_dir, "v4"); Directory.CreateDirectory(Path.Combine(v4, "config", "boot"));
            File.WriteAllText(Path.Combine(v4, "config", "boot", "example-privateserver.ini"), "[ashita.launcher]\nname = Example\n[ashita.boot]\nfile = .\\\\bootloader\\\\pol.exe\ncommand = --server x\nscript = default.txt\n[ffxi.registry]\n0001 = 1920\n0002 = 1080\n0034 = 1\n");

            Assert.IsTrue(V3Import.LooksLikeV3(v3));
            System.Collections.Generic.List<Credential> logins; System.Collections.Generic.List<string> ids;
            var report = V3Import.ImportProfiles(v3, v4, out logins, out ids);
            Assert.AreEqual(1, ids.Count);
            var p = Profile.Load(Path.Combine(v4, "config", "boot", ids[0] + ".ini"));
            Assert.AreEqual("VanaDreams", p.Name);
            Assert.AreEqual(Path.Combine(v4, "bootloader", "xiloader.exe"), p.BootFile);
            Assert.AreEqual("192.168.0.12", p.Command.Server);
            Assert.IsTrue(p.Command.Hairpin);
            Assert.AreEqual(WindowMode.Borderless, p.Mode);
            Assert.AreEqual(1920, p.Width);
            Assert.AreEqual("vanadreams.txt", p.Script);
            Assert.AreEqual(1, logins.Count(l => l != null));
            Assert.AreEqual("secret", logins[0].Password);
            Assert.IsFalse(File.ReadAllText(p.Path).Contains("secret"));

            var cfg = V3Import.ImportConfigs(v3, v4);
            Assert.IsTrue(File.Exists(Path.Combine(v4, "config", "LegacyAC", "Ferrin_WAR.xml")));
            Assert.IsTrue(File.Exists(Path.Combine(v4, "config", "MultiSend.xml")));
            Assert.IsTrue(File.Exists(Path.Combine(v4, "polplugins", "DATs", "HD-Remake", "ROM", "0.dat")));
            var pivot = File.ReadAllText(Path.Combine(v4, "config", "pivot", "pivot.ini"));
            StringAssert.Contains(pivot, "0 = HD-Remake");
            StringAssert.Contains(pivot, "1 = Other");
            Assert.IsFalse(pivot.Contains("root_path"), "XIPivot's own default is the right DATs root; a relative override breaks overlays");
            Assert.IsTrue(cfg.Lines.Any(l => l.StartsWith("Lootwhore profiles: nothing")));
        }

        [TestMethod]
        public void V3_import_keeps_logins_in_step_with_profiles()
        {
            var v3 = Path.Combine(_dir, "v3"); Directory.CreateDirectory(Path.Combine(v3, "config", "boot"));
            File.WriteAllText(Path.Combine(v3, "config", "boot", "A-Retail.xml"), "<settings><setting name=\"name\">Retail</setting><setting name=\"boot_command\">/game eAZcFcB</setting></settings>");
            File.WriteAllText(Path.Combine(v3, "config", "boot", "B-Vana.xml"), "<settings><setting name=\"name\">Vana</setting><setting name=\"boot_command\">--server 10.0.0.2 --user Ferrin --pass secret</setting></settings>");
            var v4 = Path.Combine(_dir, "v4"); Directory.CreateDirectory(Path.Combine(v4, "config", "boot"));
            File.WriteAllText(Path.Combine(v4, "config", "boot", "example-privateserver.ini"), "[ashita.launcher]\nname = Example\n[ashita.boot]\nfile = x\ncommand = --server x\nscript = default.txt\n");
            System.Collections.Generic.List<Credential> logins; System.Collections.Generic.List<string> ids;
            V3Import.ImportProfiles(v3, v4, out logins, out ids);
            Assert.AreEqual(ids.Count, logins.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i].IndexOf("vana", StringComparison.OrdinalIgnoreCase) >= 0) { Assert.IsNotNull(logins[i]); Assert.AreEqual("secret", logins[i].Password); }
                else Assert.IsNull(logins[i], ids[i] + " had no login and must not receive one");
            }
        }

        [TestMethod]
        public void Profile_store_never_lists_a_launch_copy()
        {
            var root = Path.Combine(_dir, "ashita"); var boot = Path.Combine(root, "config", "boot"); Directory.CreateDirectory(boot);
            File.WriteAllText(Path.Combine(boot, "vanadreams.ini"), "[ashita.launcher]\nname = Vanadreams\n[ashita.boot]\ncommand = --server x\n");
            File.WriteAllText(Path.Combine(boot, ".launch-vanadreams-1a2b3c4d.ini"), "[ashita.launcher]\nname = Vanadreams\n[ashita.boot]\ncommand = --server x --user u --pass p\n");
            var all = new ProfileStore(root).LoadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("vanadreams", all[0].Id);
            GameLauncher.Sweep(root);
            Assert.AreEqual(1, Directory.GetFiles(boot).Length, "the sweep removes launch copies and nothing else");
        }

        [TestMethod]
        public void Pivot_config_adds_and_removes_overlays_without_a_root_path()
        {
            var root = Path.Combine(_dir, "ashita");
            PivotConfig.AddOverlay(root, "HD-Remake");
            PivotConfig.AddOverlay(root, "vanadreams-music");
            PivotConfig.AddOverlay(root, "vanadreams-music");
            CollectionAssert.AreEqual(new[] { "HD-Remake", "vanadreams-music" }, PivotConfig.ReadOverlays(root).ToArray());
            var text = File.ReadAllText(PivotConfig.IniPath(root));
            Assert.IsFalse(text.Contains("root_path"));
            File.WriteAllText(PivotConfig.IniPath(root), "[settings]\nroot_path=polplugins\\DATs\\\ndebug_log=false\n[overlays]\n0=vanadreams-music\n");
            PivotConfig.RemoveRootPath(root);
            Assert.IsFalse(File.ReadAllText(PivotConfig.IniPath(root)).Contains("root_path"));
            PivotConfig.RemoveOverlay(root, "vanadreams-music");
            Assert.AreEqual(0, PivotConfig.ReadOverlays(root).Count);
        }

        [TestMethod]
        public void Updater_compares_release_tags_with_the_build_version()
        {
            Assert.AreEqual(new Version(0, 2, 8), Updater.ParseTag("v0.2.8"));
            Assert.AreEqual(new Version(0, 2, 8), Updater.ParseTag("0.2.8+89509c5"));
            Assert.IsNull(Updater.ParseTag("latest"));
            Assert.IsTrue(Updater.IsNewer(Updater.ParseTag("v0.2.8"), new Version(0, 2, 7, 0)));
            Assert.IsFalse(Updater.IsNewer(Updater.ParseTag("v0.2.7"), new Version(0, 2, 7, 0)), "the same release is not an update");
            Assert.IsFalse(Updater.IsNewer(Updater.ParseTag("v0.2.6"), new Version(0, 2, 7, 0)), "an older release never replaces a newer build");
            Assert.IsFalse(Updater.IsNewer(null, new Version(0, 2, 7, 0)));
        }

        [TestMethod]
        public void Firewall_registry_rule_strings_are_read_regardless_of_language()
        {
            Assert.AreEqual(@"C:\Games\Ashita\bootloader\xiloader.exe", Firewall.ParseRegistryRule(@"v2.31|Action=Allow|Active=TRUE|Dir=In|Protocol=6|App=C:\Games\Ashita\bootloader\xiloader.exe|Name=Vanadreams - xiloader.exe|Desc=|"));
            Assert.IsNull(Firewall.ParseRegistryRule(@"v2.31|Action=Block|Active=TRUE|Dir=In|App=C:\x.exe|"), "a block rule is not an allow");
            Assert.IsNull(Firewall.ParseRegistryRule(@"v2.31|Action=Allow|Active=FALSE|Dir=In|App=C:\x.exe|"), "a disabled rule does not count");
            Assert.IsNull(Firewall.ParseRegistryRule(@"v2.31|Action=Allow|Active=TRUE|Dir=Out|App=C:\x.exe|"), "outbound is not inbound");
            Assert.IsNull(Firewall.ParseRegistryRule(@"v2.31|Action=Allow|Active=TRUE|Dir=In|Name=no program|"));
        }
    }
}
