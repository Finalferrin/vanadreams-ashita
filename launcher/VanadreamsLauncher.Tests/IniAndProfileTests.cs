using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class IniAndProfileTests
    {
        private const string Example = @"; example boot config
[ashita.launcher]
autoclose   = 1
name        = Example Configuration

[ashita.boot]
; Private Server Usage
file        = .\\bootloader\\pol.exe
command     = --server homepointxi.com
gamemodule  = ffximain.dll
script      = default.txt
args        =

[ashita.polplugins]
sandbox = 0

[ffxi.registry]
0000 = 6
0001 = 1920
0002 = 1080
0034 = 1
0037 = 1920
0038 = 1080
";

        private string _dir;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vdl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "config", "boot"));
        }

        [TestCleanup]
        public void Cleanup() { try { Directory.Delete(_dir, true); } catch { } }

        [TestMethod]
        public void Ini_reads_values_through_comments_and_padding()
        {
            var ini = IniFile.FromText(Example);
            Assert.AreEqual("Example Configuration", ini.Get("ashita.launcher", "name"));
            Assert.AreEqual(".\\\\bootloader\\\\pol.exe", ini.Get("ashita.boot", "file"));
            Assert.AreEqual("", ini.Get("ashita.boot", "args"));
            Assert.IsNull(ini.Get("ashita.boot", "missing"));
            Assert.AreEqual("1", ini.Get("ffxi.registry", "0034"));
        }

        [TestMethod]
        public void Ini_set_edits_in_place_and_keeps_everything_else()
        {
            var ini = IniFile.FromText(Example);
            var before = ini.Lines.Count;
            ini.Set("ashita.launcher", "name", "Claudette");
            ini.Set("ffxi.registry", "0034", "2");
            Assert.AreEqual(before, ini.Lines.Count, "in-place edits add no lines");
            Assert.AreEqual(1, ini.Lines.Count(l => l == "name = Claudette"));
            Assert.AreEqual(1, ini.Lines.Count(l => l.StartsWith("; example")), "comment preserved");
            Assert.AreEqual(1, ini.Lines.Count(l => l.Trim() == "[ffxi.registry]"), "no duplicate section");
        }

        [TestMethod]
        public void Ini_set_inserts_missing_key_and_appends_missing_section()
        {
            var ini = IniFile.FromText(Example);
            ini.Set("ffxi.registry", "0045", "0");
            Assert.AreEqual("0", ini.Get("ffxi.registry", "0045"));
            ini.Set("brand.new", "k", "v");
            Assert.AreEqual("v", ini.Get("brand.new", "k"));
            Assert.AreEqual(1, ini.Lines.Count(l => l.Trim() == "[brand.new]"));
        }

        [TestMethod]
        public void Profile_background_resolution_is_registry_0003_and_0004()
        {
            var path = Path.Combine(_dir, "config", "boot", "vanadreams.ini");
            File.WriteAllText(path, Example);
            var p = Profile.Load(path);
            Assert.AreEqual(-1, p.BackgroundWidth, "absent means the game's default");
            Assert.AreEqual(-1, p.BackgroundHeight);
            p.BackgroundWidth = 4096; p.BackgroundHeight = 4096;
            p.Save();
            var again = Profile.Load(path);
            Assert.AreEqual(4096, again.BackgroundWidth);
            Assert.AreEqual(4096, again.BackgroundHeight);
            Assert.AreEqual("4096", again.Ini.Get("ffxi.registry", "0003"));
            Assert.AreEqual("4096", again.Ini.Get("ffxi.registry", "0004"));
            Assert.AreEqual(1920, again.Width, "the window size is left as it was");
        }

        [TestMethod]
        public void Profile_round_trips_through_a_real_file()
        {
            var path = Path.Combine(_dir, "config", "boot", "vanadreams.ini");
            File.WriteAllText(path, Example);
            var p = Profile.Load(path);
            Assert.AreEqual("Example Configuration", p.Name);
            Assert.AreEqual(".\\bootloader\\pol.exe", p.BootFile, "path unescaped on read");
            Assert.AreEqual("homepointxi.com", p.Command.Server);
            Assert.AreEqual(1920, p.Width);
            Assert.AreEqual(WindowMode.Windowed, p.Mode);
            Assert.IsFalse(p.PolPlugins["sandbox"]);

            p.Name = "Vanadreams";
            p.BootFile = @"C:\Games\Vanadreams\bootloader\xiloader.exe";
            p.Command.Server = "vanadreams.fairywitch.ca";
            p.Command.User = "Ferrin"; p.Command.Password = "secret";
            p.Mode = WindowMode.Borderless;
            p.Height = -1;
            p.PolPlugins["pivot"] = true;
            p.Save();

            var raw = File.ReadAllText(path);
            StringAssert.Contains(raw, "file = C:\\\\Games\\\\Vanadreams\\\\bootloader\\\\xiloader.exe");
            StringAssert.Contains(raw, "command = --server vanadreams.fairywitch.ca");
            Assert.IsFalse(raw.Contains("secret"), "credentials never reach the ini");
            StringAssert.Contains(raw, "0034 = 2");
            StringAssert.Contains(raw, "0002 = -1");
            StringAssert.Contains(raw, "pivot = 1");

            var again = Profile.Load(path);
            Assert.AreEqual(@"C:\Games\Vanadreams\bootloader\xiloader.exe", again.BootFile);
            Assert.AreEqual("vanadreams.fairywitch.ca", again.Command.Server);
            Assert.IsTrue(again.PolPlugins["pivot"]);
        }

        [TestMethod]
        public void ProfileStore_orders_examples_last_and_duplicates()
        {
            var boot = Path.Combine(_dir, "config", "boot");
            File.WriteAllText(Path.Combine(boot, "example-privateserver.ini"), Example);
            File.WriteAllText(Path.Combine(boot, "zed.ini"), Example.Replace("Example Configuration", "Zed"));
            var store = new ProfileStore(_dir);
            var all = store.LoadAll();
            Assert.AreEqual("Zed", all[0].Name);
            Assert.IsTrue(all[1].IsExample);
            var copy = all[1].DuplicateTo(store.PathFor("mine"), "Mine");
            Assert.AreEqual("Mine", copy.Name);
            Assert.AreEqual(3, store.LoadAll().Count);
            Assert.IsFalse(ProfileStore.IsValidId("a:b"));
            Assert.IsTrue(ProfileStore.IsValidId("Ferrin on Vanadreams"));
        }
    }
}
