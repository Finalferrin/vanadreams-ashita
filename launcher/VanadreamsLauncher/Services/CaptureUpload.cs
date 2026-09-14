using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    public sealed class CaptureSummary
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string CapturedAt { get; set; }
        public string CapturedOn { get; set; }
        public int MainJob { get; set; }
        public int MainLevel { get; set; }
        public int Items { get; set; }
        public long Gil { get; set; }
        public int Spells { get; set; }
        public int KeyItems { get; set; }
        public long Bytes { get; set; }

        public static CaptureSummary Read(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var d = Json.ParseObject(text);
            if (d == null) return null;
            var ch = Json.Obj(d.ContainsKey("character") ? d["character"] : null);
            var jobs = Json.Obj(d.ContainsKey("jobs") ? d["jobs"] : null);
            var inv = Json.Obj(d.ContainsKey("inventory") ? d["inventory"] : null);
            var items = 0;
            if (inv != null) foreach (var kv in inv) items += Json.List(kv.Value).Count;
            return new CaptureSummary
            {
                Path = path, Name = Json.Str(ch, "name", System.IO.Path.GetFileNameWithoutExtension(path)),
                CapturedAt = Json.Str(d, "captured_at", ""), CapturedOn = Json.Str(d, "captured_on", ""),
                MainJob = Json.Int(jobs, "main"), MainLevel = Json.Int(jobs, "main_level"),
                Items = items, Gil = Json.Long(d, "gil"),
                Spells = Json.List(d.ContainsKey("spells") ? d["spells"] : null).Count,
                KeyItems = Json.List(d.ContainsKey("key_items") ? d["key_items"] : null).Count,
                Bytes = new FileInfo(path).Length,
            };
        }
    }

    public sealed class UploadResult
    {
        public bool Ok { get; set; }
        public string Reference { get; set; }
        public string Message { get; set; }
    }

    /// <summary>Sends a snapshot to the site's capture route. Nothing is sent without the button.</summary>
    public sealed class CaptureUploader
    {
        private readonly HttpClient _http;
        public string Url { get; set; } = "https://fairywitch.ca/api/public/vanadreams/capture";
        public CaptureUploader(HttpClient http = null) { _http = http ?? Http.Shared; }

        public async Task<UploadResult> SendAsync(string snapshotPath, string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return new UploadResult { Ok = false, Message = "Remember your Vanadreams login on the Profiles page first; the snapshot is filed under it." };
            var body = File.ReadAllText(snapshotPath, Encoding.UTF8);
            if (body.Length > 2 * 1024 * 1024) return new UploadResult { Ok = false, Message = "That snapshot is over 2 MB, which the site refuses." };
            using (var req = new HttpRequestMessage(HttpMethod.Post, Url))
            {
                req.Headers.Add("X-Vanadreams-User", username);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                try
                {
                    using (var resp = await _http.SendAsync(req).ConfigureAwait(false))
                    {
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return Interpret((int)resp.StatusCode, text);
                    }
                }
                catch (Exception ex)
                {
                    return new UploadResult { Ok = false, Message = "Couldn't reach fairywitch.ca: " + ex.Message };
                }
            }
        }

        public static UploadResult Interpret(int status, string text)
        {
            var d = Json.ParseObject(text);
            if (status >= 200 && status < 300 && d != null && Json.Bool(d, "ok"))
                return new UploadResult { Ok = true, Reference = Json.Str(d, "reference", ""), Message = "Queued for import, reference " + Json.Str(d, "reference", "?") + "." + (Json.Int(d, "queued") > 1 ? " " + Json.Int(d, "queued") + " of yours are waiting." : "") };
            if (status == 404) return new UploadResult { Ok = false, Message = "The site has no capture route yet. Keep the file; it can be sent later." };
            if (status == 429) return new UploadResult { Ok = false, Message = "Too many sends this hour. Try again later." };
            var reason = d != null ? (Json.Str(d, "error") ?? Json.Str(d, "message")) : null;
            return new UploadResult { Ok = false, Message = string.IsNullOrEmpty(reason) ? "The site refused it (" + status + ")." : reason };
        }
    }
}
