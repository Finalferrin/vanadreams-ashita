using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Vanadreams.Services
{
    public sealed class Credential
    {
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// Per-profile login, protected with the Windows Data Protection API in current-user scope.
    /// Nothing here is ever written into a boot ini.
    /// </summary>
    public sealed class CredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("vanadreams-launcher-v1");
        private readonly string _path;
        private readonly Dictionary<string, Credential> _items = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);

        public CredentialStore(string path)
        {
            _path = path;
            Load();
        }

        public Credential Get(string profileId)
        {
            Credential c;
            return _items.TryGetValue(profileId, out c) ? c : null;
        }

        public void Set(string profileId, string user, string password)
        {
            if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(password)) { Remove(profileId); return; }
            _items[profileId] = new Credential { User = user ?? "", Password = password ?? "" };
            Save();
        }

        public void Remove(string profileId)
        {
            if (_items.Remove(profileId)) Save();
        }

        private void Load()
        {
            _items.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                var root = Json.ParseObject(File.ReadAllText(_path, Encoding.UTF8));
                if (root == null) return;
                foreach (var kv in root)
                {
                    var d = Json.Obj(kv.Value);
                    if (d == null) continue;
                    var user = Json.Str(d, "user", "");
                    var blob = Json.Str(d, "password", "");
                    _items[kv.Key] = new Credential { User = user, Password = Unprotect(blob) };
                }
            }
            catch (Exception)
            {
                // a damaged store is treated as empty; the player types the password once more
                _items.Clear();
            }
        }

        private void Save()
        {
            var root = new Dictionary<string, object>();
            foreach (var kv in _items)
                root[kv.Key] = new Dictionary<string, object> { { "user", kv.Value.User }, { "password", Protect(kv.Value.Password) } };
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, Json.Stringify(root), new UTF8Encoding(false));
        }

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        public static string Unprotect(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return "";
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
