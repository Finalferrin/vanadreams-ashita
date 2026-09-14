using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    public sealed class DownloadProgress
    {
        public long Done { get; set; }
        public long Total { get; set; }
        public string Label { get; set; }
        public double Fraction => Total > 0 ? Math.Min(1.0, (double)Done / Total) : 0;
    }

    public sealed class ReleaseAsset
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Tag { get; set; }
    }

    /// <summary>HTTP downloads with progress, GitHub release and folder lookups, and unzip with overwrite.</summary>
    public sealed class Downloader
    {
        private readonly HttpClient _http;
        public Downloader(HttpClient http = null) { _http = http ?? Http.Shared; }

        public async Task<string> GetStringAsync(string url, CancellationToken ct = default(CancellationToken))
        {
            using (var resp = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Download to a file, resuming a partial file if the server allows it.</summary>
        public async Task DownloadFileAsync(string url, string destination, IProgress<DownloadProgress> progress = null, string label = null, long expectedSize = 0, CancellationToken ct = default(CancellationToken))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var partial = destination + ".part";
            long already = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (already > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(already, null);
                using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (already > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) { already = 0; File.Delete(partial); }
                    resp.EnsureSuccessStatusCode();
                    var total = (resp.Content.Headers.ContentLength ?? 0) + already;
                    if (total == 0) total = expectedSize;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(partial, already > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        var buf = new byte[1 << 16];
                        long done = already;
                        int n;
                        var last = DateTime.UtcNow;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            done += n;
                            if (progress != null && (DateTime.UtcNow - last).TotalMilliseconds > 100)
                            {
                                progress.Report(new DownloadProgress { Done = done, Total = total, Label = label });
                                last = DateTime.UtcNow;
                            }
                        }
                        progress?.Report(new DownloadProgress { Done = done, Total = total, Label = label });
                    }
                }
            }
            if (expectedSize > 0 && new FileInfo(partial).Length != expectedSize)
            {
                File.Delete(partial);
                throw new IOException($"{label ?? Path.GetFileName(destination)} came down the wrong size; press the button again to retry.");
            }
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(partial, destination);
        }

        /// <summary>The newest release's asset whose name matches the glob, e.g. "Bellhop.*Interface.4.30.zip".</summary>
        public async Task<ReleaseAsset> LatestReleaseAssetAsync(string repo, string assetGlob, CancellationToken ct = default(CancellationToken))
        {
            var text = await GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=10", ct).ConfigureAwait(false);
            var rx = GlobToRegex(assetGlob);
            foreach (var r in Json.List(Json.Parse(text)))
            {
                var rel = Json.Obj(r);
                if (rel == null || Json.Bool(rel, "draft")) continue;
                foreach (var a in Json.List(rel.ContainsKey("assets") ? rel["assets"] : null))
                {
                    var asset = Json.Obj(a);
                    var name = Json.Str(asset, "name", "");
                    if (rx.IsMatch(name))
                        return new ReleaseAsset { Name = name, Url = Json.Str(asset, "browser_download_url"), Size = Json.Long(asset, "size"), Tag = Json.Str(rel, "tag_name") };
                }
            }
            return null;
        }

        /// <summary>Every file under a folder of a GitHub repo: (relative path, download url).</summary>
        public async Task<List<KeyValuePair<string, string>>> ListRepoFolderAsync(string repo, string path, string branch, CancellationToken ct = default(CancellationToken))
        {
            var result = new List<KeyValuePair<string, string>>();
            await WalkAsync(repo, path.Trim('/'), branch, "", result, ct).ConfigureAwait(false);
            return result;
        }

        private async Task WalkAsync(string repo, string path, string branch, string rel, List<KeyValuePair<string, string>> into, CancellationToken ct)
        {
            var text = await GetStringAsync($"https://api.github.com/repos/{repo}/contents/{path}?ref={branch}", ct).ConfigureAwait(false);
            foreach (var e in Json.List(Json.Parse(text)))
            {
                var d = Json.Obj(e);
                var name = Json.Str(d, "name", "");
                var type = Json.Str(d, "type", "");
                var childRel = string.IsNullOrEmpty(rel) ? name : rel + "/" + name;
                if (type == "dir") await WalkAsync(repo, path + "/" + name, branch, childRel, into, ct).ConfigureAwait(false);
                else if (type == "file") into.Add(new KeyValuePair<string, string>(childRel, Json.Str(d, "download_url")));
            }
        }

        public static Regex GlobToRegex(string glob)
        {
            var pattern = "^" + Regex.Escape(glob ?? "*").Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(pattern, RegexOptions.IgnoreCase);
        }

        /// <summary>Unzip over an existing tree, replacing files that are already there.</summary>
        public static List<string> ExtractZipOverwrite(string zipPath, string destinationRoot)
        {
            var written = new List<string>();
            var root = Path.GetFullPath(destinationRoot).TrimEnd('\\') + "\\";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // a folder entry
                    var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never write outside the folder
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    written.Add(target.Substring(root.Length));
                }
            }
            return written;
        }
    }
}
