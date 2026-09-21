using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class LoaderVersionScriptTests
    {
        [TestMethod]
        public void LoaderCommand_parses_every_flag_and_keeps_the_rest()
        {
            var c = LoaderCommand.Parse("--server 192.168.0.19 --hairpin --user Ferrin --pass \"two words\" --lang 2");
            Assert.AreEqual("192.168.0.19", c.Server);
            Assert.IsTrue(c.Hairpin);
            Assert.AreEqual("Ferrin", c.User);
            Assert.AreEqual("two words", c.Password);
            Assert.AreEqual("--lang 2", c.Extra);
            Assert.AreEqual("--server 192.168.0.19 --hairpin --lang 2", c.ToIniCommand());
            Assert.AreEqual("--server 192.168.0.19 --hairpin --lang 2 --user Ferrin --pass \"two words\"", c.ToLaunchCommand());
        }

        [TestMethod]
        public void LoaderCommand_handles_empty_and_minimal()
        {
            Assert.AreEqual("", LoaderCommand.Parse("").Server);
            var c = new LoaderCommand { Server = "vanadreams.fairywitch.ca" };
            Assert.AreEqual("--server vanadreams.fairywitch.ca", c.ToLaunchCommand());
        }

        [TestMethod]
        public void LoaderCommand_switches_to_Tailscale_and_back_keeping_everything_else()
        {
            var c = LoaderCommand.Parse("--server vanadreams.fairywitch.ca --lang 2");
            Assert.IsFalse(c.IsTailscale);

            // over Tailscale the server's own tailnet address is used, and --hairpin keeps the client on it
            // for the zones too: the server hands every client its public address, which is the very thing
            // a player's router or provider is blocking when they need this
            c.UseTailscale(true);
            Assert.IsTrue(c.IsTailscale);
            Assert.AreEqual("--server 100.114.52.41 --hairpin --lang 2", c.ToIniCommand());

            c.UseTailscale(false);
            Assert.IsFalse(c.IsTailscale);
            Assert.AreEqual("--server vanadreams.fairywitch.ca --lang 2", c.ToIniCommand());

            // a profile for some other server is not Tailscale just because it uses --hairpin
            Assert.IsFalse(LoaderCommand.Parse("--server 192.168.0.19 --hairpin").IsTailscale);
        }

        [TestMethod]
        public void ClientVersion_takes_the_newest_stamp_from_patch_history()
        {
            var cfg = "file ROM/0/0.DAT {\n30260805_0 12 ab cd\n30260904_1 12 ab cd\n30260904_0 12 ab cd\n}\nend\n30210706_0 zzz\n";
            Assert.AreEqual("30260904_1", ClientVersion.NewestStamp(cfg));
            Assert.IsNull(ClientVersion.NewestStamp(""));
            Assert.IsNull(ClientVersion.NewestStamp("nothing here"));
        }

        [TestMethod]
        public void ClientVersion_compares_year_and_month_the_way_the_server_does()
        {
            Assert.AreEqual(VersionVerdict.Ready, ClientVersion.Compare("30260904_1", "30260805_0", VersionLock.MatchingOrNewer).Verdict);
            Assert.AreEqual(VersionVerdict.ClientTooOld, ClientVersion.Compare("30260415_0", "30260805_0", VersionLock.MatchingOrNewer).Verdict);
            Assert.AreEqual(VersionVerdict.Ready, ClientVersion.Compare("30260930_0", "30260904_1", VersionLock.Exact).Verdict, "same month passes exact");
            Assert.AreEqual(VersionVerdict.ClientNewerThanServerAllows, ClientVersion.Compare("30261001_0", "30260904_1", VersionLock.Exact).Verdict);
            Assert.AreEqual(VersionVerdict.Ready, ClientVersion.Compare("30250101_0", "30260904_1", VersionLock.Off).Verdict);
            var unknown = ClientVersion.Compare(null, "30260904_1", VersionLock.MatchingOrNewer);
            Assert.AreEqual(VersionVerdict.Unknown, unknown.Verdict);
            Assert.IsFalse(unknown.BlocksPlay, "unknown never blocks");
            var tooOld = ClientVersion.Compare("30260415_0", "30260805_0", VersionLock.MatchingOrNewer);
            Assert.IsFalse(tooOld.BlocksPlay, "a launcher default never blocks or claims a match");
            StringAssert.Contains(tooOld.Sentence, "hasn't published");
            tooOld.ExpectedIsPublished = true;
            Assert.IsTrue(tooOld.BlocksPlay, "a published version does block");
            StringAssert.Contains(tooOld.Sentence, "Update the client");
        }

        [TestMethod]
        public void ScriptWriter_orders_plugins_addons_wait_config_and_keeps_yours()
        {
            var entries = new List<ScriptEntry>
            {
                new ScriptEntry { Id = "distance", Kind = LoadKind.Addon, LoadName = "distance" },
                new ScriptEntry { Id = "lootwhore", Kind = LoadKind.Plugin, LoadName = "lootwhore", ConfigLines = new List<string> { "/lw load default" } },
                new ScriptEntry { Id = "pivot", Kind = LoadKind.PolPlugin, LoadName = "pivot" },
                new ScriptEntry { Id = "fps", Kind = LoadKind.Addon, LoadName = "fps", ConfigLines = new List<string> { "/bind F12 /fps" } },
            };
            var existing = "old stuff\n" + ScriptWriter.YoursMarker + "\n# Anything below this line is kept exactly as you wrote it.\n/bind ^v /paste\n/ambient 255 255 255 255\n\n";
            var text = ScriptWriter.Build(entries, existing);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int iPlugin = System.Array.IndexOf(lines, "/load lootwhore");
            int iAddons = System.Array.IndexOf(lines, "/load addons");
            int iDistance = System.Array.IndexOf(lines, "/addon load distance");
            int iWait = System.Array.IndexOf(lines, "/wait 3");
            int iCfg = System.Array.IndexOf(lines, "/lw load default");
            int iYours = System.Array.IndexOf(lines, ScriptWriter.YoursMarker);
            Assert.IsTrue(iPlugin >= 0 && iPlugin < iAddons && iAddons < iDistance && iDistance < iWait && iWait < iCfg && iCfg < iYours);
            Assert.IsFalse(text.Contains("pivot"), "POL plugins go to the ini, not the script");
            Assert.IsFalse(text.Contains("old stuff"), "generated part is replaced");
            StringAssert.Contains(text, "/bind ^v /paste");
            StringAssert.Contains(text, "/ambient 255 255 255 255");
            var second = ScriptWriter.Build(entries, text);
            Assert.AreEqual(text, second, "a second save is a no-op");
        }
    }
}
