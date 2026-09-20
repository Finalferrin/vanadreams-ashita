using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanadreams.Services;

namespace Vanadreams.Tests
{
    [TestClass]
    public class RepoSyncTests
    {
        private string _dir;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vdl-sync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void Cleanup() { try { Directory.Delete(_dir, true); } catch { } }

        private string Put(string rel, string text)
        {
            var path = RepoSync.LocalPath(_dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes(text));
            return path;
        }

        private RepoFile Remote(string rel, string text) =>
            new RepoFile { Rel = rel, Url = "https://example.invalid/" + rel, Size = text.Length, Sha = ShaOf(text) };

        private string ShaOf(string text)
        {
            var tmp = Path.Combine(_dir, "sha-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(tmp, Encoding.ASCII.GetBytes(text));
            try { return RepoSync.GitBlobSha(tmp); } finally { File.Delete(tmp); }
        }

        [TestMethod]
        public void GitBlobSha_IsGitsOwnHash()
        {
            // `printf 'hello\n' | git hash-object --stdin` and the well-known hash of the empty blob
            Assert.AreEqual("ce013625030ba8dba906f756967f9e9ca394464a", RepoSync.GitBlobSha(Put("hello.txt", "hello\n")));
            Assert.AreEqual("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", RepoSync.GitBlobSha(Put("empty.txt", "")));
        }

        [TestMethod]
        public void Plan_DownloadsOnlyWhatIsMissingOrChanged()
        {
            Put("vanatunes.lua", "old code");
            Put("music/one.mp3", "song one");
            var plan = RepoSync.Make(new[]
            {
                Remote("vanatunes.lua", "new code"),      // same length, different content: the hash catches it
                Remote("music/one.mp3", "song one"),      // already here
                Remote("music/two.mp3", "song two"),      // new
            }, _dir);

            CollectionAssert.AreEquivalent(new[] { "vanatunes.lua", "music/two.mp3" }, plan.Download.Select(f => f.Rel).ToList());
            CollectionAssert.AreEquivalent(new[] { "music/one.mp3" }, plan.Keep.Select(f => f.Rel).ToList());
            Assert.AreEqual(0, plan.Delete.Count);
        }

        [TestMethod]
        public void Plan_RemovesOnlyWhatTheLauncherInstalledAndTheRepoDropped()
        {
            Put("music/one.mp3", "song one");
            Put("music/gone.mp3", "pulled from the playlist");
            Put("settings.json", "the addon wrote this beside itself");      // never in any manifest
            RepoSync.WriteManifest(_dir, new[] { "music/one.mp3", "music/gone.mp3" });

            var plan = RepoSync.Make(new[] { Remote("music/one.mp3", "song one") }, _dir);

            CollectionAssert.AreEquivalent(new[] { "music/gone.mp3" }, plan.Delete);
            Assert.IsTrue(File.Exists(RepoSync.LocalPath(_dir, "settings.json")));
        }

        [TestMethod]
        public void Plan_WithNoManifestRemovesNothing()
        {
            Put("music/mine.mp3", "put here by hand");
            var plan = RepoSync.Make(new[] { Remote("vanatunes.lua", "code") }, _dir);
            Assert.AreEqual(0, plan.Delete.Count);
        }

        [TestMethod]
        public void UnsafePathsAreIgnored()
        {
            Assert.IsFalse(RepoSync.IsSafeRel("../outside.txt"));
            Assert.IsFalse(RepoSync.IsSafeRel("music/../../outside.txt"));
            Assert.IsFalse(RepoSync.IsSafeRel(@"C:\Windows\x.dll"));
            Assert.IsFalse(RepoSync.IsSafeRel(""));
            Assert.IsTrue(RepoSync.IsSafeRel("music/songs go here.txt"));

            RepoSync.WriteManifest(_dir, new[] { "../outside.txt" });
            var plan = RepoSync.Make(new[] { new RepoFile { Rel = "../evil.txt", Url = "x", Sha = "0", Size = 1 } }, _dir);
            Assert.AreEqual(0, plan.Download.Count);
            Assert.AreEqual(0, plan.Delete.Count);
        }

        [TestMethod]
        public void ParseFolderListing_ReadsFilesHashesSizesAndFolders()
        {
            const string json = @"[
  { ""name"": ""README.md"", ""type"": ""file"", ""sha"": ""abc"", ""size"": 12, ""download_url"": ""https://raw.example/README.md"" },
  { ""name"": ""music"", ""type"": ""dir"", ""sha"": ""def"", ""size"": 0, ""download_url"": null },
  { ""name"": ""vanatunes.lua"", ""type"": ""file"", ""sha"": ""123"", ""size"": 4096, ""download_url"": ""https://raw.example/vanatunes.lua"" }
]";
            var files = Downloader.ParseFolderListing(json, "vanatunes", out var folders);
            CollectionAssert.AreEqual(new[] { "music" }, folders);
            CollectionAssert.AreEqual(new[] { "vanatunes/README.md", "vanatunes/vanatunes.lua" }, files.Select(f => f.Rel).ToList());
            Assert.AreEqual("123", files[1].Sha);
            Assert.AreEqual(4096, files[1].Size);
        }

        [TestMethod]
        public void ConfigPath_DesktopTokenOrUnderAshita()
        {
            Assert.AreEqual(@"C:\Users\x\Desktop\Vanadreams Music\", CatalogItem.ResolveConfigPath(@"C:\Ashita", @"{desktop}\Vanadreams Music\", @"C:\Users\x\Desktop"));
            Assert.AreEqual(@"C:\Ashita\config\addons\x\", CatalogItem.ResolveConfigPath(@"C:\Ashita", @"config\addons\x\", @"C:\Users\x\Desktop"));
            Assert.IsNull(CatalogItem.ResolveConfigPath(@"C:\Ashita", null, @"C:\Users\x\Desktop"));
        }

        [TestMethod]
        public void OnByDefault_IsGivenOnceAndNeverAHeldBackItem()
        {
            var cat = Catalog.Parse(@"{ ""schema"": 1, ""items"": [
  { ""id"": ""vanatunes"", ""kind"": ""addon"", ""onByDefault"": true, ""source"": { ""type"": ""repo-folder"", ""repo"": ""VanaDreams/vanatunes"", ""path"": ""vanatunes"" }, ""install"": ""copy-to-addons"", ""load"": ""/addon load vanatunes"" },
  { ""id"": ""distance"", ""kind"": ""addon"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load distance"" },
  { ""id"": ""chatfix"", ""kind"": ""addon"", ""onByDefault"": true, ""heldBack"": ""wrong here"", ""source"": { ""type"": ""bundled"" }, ""load"": ""/addon load chatfix"" }
] }");
            Assert.IsTrue(cat.Find("vanatunes").OnByDefault);
            Assert.IsFalse(cat.Find("distance").OnByDefault);

            CollectionAssert.AreEqual(new[] { "vanatunes" }, AddonInstaller.DefaultsToGive(cat, new List<string>()).Select(i => i.Id).ToList());
            // given once already: the player may have unticked it since, and that sticks
            Assert.AreEqual(0, AddonInstaller.DefaultsToGive(cat, new List<string> { "VanaTunes" }).Count);
        }
    }
}
