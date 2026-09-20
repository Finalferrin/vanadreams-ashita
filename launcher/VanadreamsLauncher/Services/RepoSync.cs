using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>One file of a repo folder, as GitHub lists it.</summary>
    public sealed class RepoFile
    {
        public string Rel { get; set; }     // path under the folder, with forward slashes
        public string Url { get; set; }
        public string Sha { get; set; }     // git's blob hash of the content
        public long Size { get; set; }
    }

    /// <summary>
    /// Brings a folder on disk in step with a folder of a GitHub repo without fetching what is already there:
    /// a file is downloaded only when it is missing or its content differs, and a file the launcher put there
    /// on an earlier install is removed once the repo no longer has it. Anything else in the folder, such as
    /// a settings file an addon wrote beside itself, is never touched: only names in the launcher's own list of
    /// what it installed are candidates for removal.
    /// </summary>
    public static class RepoSync
    {
        public const string ManifestName = ".vanadreams-files.txt";

        public sealed class Plan
        {
            public List<RepoFile> Download { get; } = new List<RepoFile>();
            public List<RepoFile> Keep { get; } = new List<RepoFile>();
            public List<string> Delete { get; } = new List<string>();   // relative paths
        }

        /// <summary>The hash git gives a file's content: SHA-1 of "blob {length}\0" followed by the bytes.</summary>
        public static string GitBlobSha(string path)
        {
            var length = new FileInfo(path).Length;
            using (var sha = SHA1.Create())
            using (var file = File.OpenRead(path))
            {
                var header = Encoding.ASCII.GetBytes("blob " + length + "\0");
                sha.TransformBlock(header, 0, header.Length, null, 0);
                var buffer = new byte[81920];
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0) sha.TransformBlock(buffer, 0, read, null, 0);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return string.Concat(sha.Hash.Select(b => b.ToString("x2")));
            }
        }

        /// <summary>A listed path is only ever written under the target folder.</summary>
        public static bool IsSafeRel(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return false;
            if (Path.IsPathRooted(rel) || rel.Contains(":")) return false;
            return !rel.Split('/', '\\').Any(part => part == ".." || part == ".");
        }

        public static string LocalPath(string targetDir, string rel) => Path.Combine(targetDir, rel.Replace('/', '\\'));

        public static List<string> ReadManifest(string targetDir)
        {
            var path = Path.Combine(targetDir, ManifestName);
            if (!File.Exists(path)) return new List<string>();
            return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        public static void WriteManifest(string targetDir, IEnumerable<string> rels)
        {
            Directory.CreateDirectory(targetDir);
            File.WriteAllLines(Path.Combine(targetDir, ManifestName), rels.OrderBy(r => r, StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
        }

        public static Plan Make(IEnumerable<RepoFile> remote, string targetDir)
        {
            var plan = new Plan();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in remote ?? Enumerable.Empty<RepoFile>())
            {
                if (!IsSafeRel(f.Rel)) continue;
                listed.Add(f.Rel.Replace('\\', '/'));
                var local = LocalPath(targetDir, f.Rel);
                var same = File.Exists(local)
                           && (f.Size <= 0 || new FileInfo(local).Length == f.Size)
                           && !string.IsNullOrEmpty(f.Sha)
                           && string.Equals(GitBlobSha(local), f.Sha, StringComparison.OrdinalIgnoreCase);
                (same ? plan.Keep : plan.Download).Add(f);
            }
            foreach (var old in ReadManifest(targetDir))
            {
                var rel = old.Replace('\\', '/');
                if (!IsSafeRel(rel) || listed.Contains(rel)) continue;
                if (File.Exists(LocalPath(targetDir, rel))) plan.Delete.Add(rel);
            }
            return plan;
        }
    }
}
