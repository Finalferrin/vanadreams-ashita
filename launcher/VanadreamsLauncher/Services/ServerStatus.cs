using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Vanadreams.Services
{
    public enum ServerState { Unknown, SettingUp, Online, Offline, Maintenance }

    public sealed class StatusInfo
    {
        public ServerState State { get; set; } = ServerState.Unknown;
        public DateTimeOffset? CheckedAt { get; set; }
        public string Note { get; set; } = "";
        public string ClientVer { get; set; }
        public VersionLock? Lock { get; set; }
        /// <summary>Players online right now, when the site has a fresh figure; null otherwise.</summary>
        public int? Online { get; set; }
        public bool FromCache { get; set; }
        public string Error { get; set; }

        public string StateWord
        {
            get
            {
                switch (State)
                {
                    case ServerState.Online: return "Online";
                    case ServerState.Offline: return "Offline";
                    case ServerState.Maintenance: return "Maintenance";
                    case ServerState.SettingUp: return "Being set up";
                    default: return "Status unknown";
                }
            }
        }

        public string CheckedWord => CheckedAt.HasValue ? "checked " + CheckedAt.Value.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant() : "";

        public static StatusInfo Parse(string json)
        {
            var d = Json.ParseObject(json);
            var s = new StatusInfo();
            if (d == null) return s;
            switch ((Json.Str(d, "state") ?? "").ToLowerInvariant())
            {
                case "online": s.State = ServerState.Online; break;
                case "offline": s.State = ServerState.Offline; break;
                case "maintenance": s.State = ServerState.Maintenance; break;
                case "setting-up": s.State = ServerState.SettingUp; break;
                default: s.State = ServerState.Unknown; break;
            }
            DateTimeOffset when;
            var at = Json.Str(d, "checked_at");
            if (!string.IsNullOrWhiteSpace(at) && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out when)) s.CheckedAt = when;
            s.Note = Json.Str(d, "note", "") ?? "";
            s.ClientVer = Json.Str(d, "client_ver");
            var lockValue = Json.Str(d, "ver_lock");
            int l;
            if (!string.IsNullOrWhiteSpace(lockValue) && int.TryParse(lockValue, out l) && Enum.IsDefined(typeof(VersionLock), l)) s.Lock = (VersionLock)l;
            var stats = d.ContainsKey("stats") ? Json.Obj(d["stats"]) : null;
            int online;
            if (stats != null && int.TryParse(Json.Str(stats, "online") ?? "", out online) && online >= 0) s.Online = online;
            return s;
        }
    }

    /// <summary>The fairywitch.ca status route, with the last good answer kept on disk.</summary>
    public sealed class ServerStatusClient
    {
        public const string DefaultUrl = "https://fairywitch.ca/api/public/vanadreams/status";
        private readonly string _cachePath;
        private readonly HttpClient _http;

        public string Url { get; set; } = DefaultUrl;

        public ServerStatusClient(string cachePath, HttpClient http = null)
        {
            _cachePath = cachePath;
            _http = http ?? Http.Shared;
        }

        public async Task<StatusInfo> FetchAsync()
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5)))
                using (var resp = await _http.GetAsync(Url, cts.Token).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var info = StatusInfo.Parse(text);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
                        File.WriteAllText(_cachePath, text, new UTF8Encoding(false));
                    }
                    catch (Exception) { /* cache is a convenience */ }
                    return info;
                }
            }
            catch (Exception ex)
            {
                var cached = ReadCache();
                cached.FromCache = true;
                cached.Error = ex.Message;
                return cached;
            }
        }

        public StatusInfo ReadCache()
        {
            try
            {
                if (File.Exists(_cachePath)) return StatusInfo.Parse(File.ReadAllText(_cachePath, Encoding.UTF8));
            }
            catch (Exception) { }
            return new StatusInfo();
        }
    }

    public static class Http
    {
        private static HttpClient _shared;
        public static HttpClient Shared
        {
            get
            {
                if (_shared == null)
                {
                    System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                    _shared = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                    _shared.DefaultRequestHeaders.UserAgent.ParseAdd("VanadreamsLauncher/0.1 (+https://github.com/Finalferrin/vanadreams-ashita)");
                    _shared.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json, application/json, */*");
                }
                return _shared;
            }
        }
    }
}
