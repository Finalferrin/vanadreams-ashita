using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace Vanadreams.Services
{
    /// <summary>
    /// JSON through the serializer that ships inside .NET Framework, so the exe carries no
    /// extra DLL. Objects come back as Dictionary&lt;string, object&gt;, arrays as object[].
    /// </summary>
    public static class Json
    {
        private static JavaScriptSerializer Make() => new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return Make().DeserializeObject(StripBom(text)); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        public static Dictionary<string, object> ParseObject(string text) => Parse(text) as Dictionary<string, object>;

        public static string Stringify(object value) => Make().Serialize(value);

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;

        public static IList<object> List(object o)
        {
            if (o is object[] arr) return arr;
            if (o is ArrayList al) return al.Cast<object>().ToList();
            if (o is IList<object> l) return l;
            return new List<object>();
        }

        public static string Str(Dictionary<string, object> d, string key, string fallback = null)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            return v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int Int(Dictionary<string, object> d, string key, int fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        public static long Long(Dictionary<string, object> d, string key, long fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool b) return b;
            var s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> Strings(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return new List<string>();
            return List(v).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
        }

        private static string StripBom(string t) => t.Length > 0 && t[0] == '﻿' ? t.Substring(1) : t;
    }
}
