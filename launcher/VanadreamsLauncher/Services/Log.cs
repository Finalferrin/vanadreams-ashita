using System;
using System.IO;
using System.Text;

namespace Vanadreams.Services
{
    /// <summary>A rolling text log, one megabyte, the detail behind every sentence the window shows.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        public static string Path { get; set; }

        public static void Info(string message) => Write("info", message);
        public static void Warn(string message) => Write("warn", message);
        public static void Error(string message, Exception ex = null) => Write("error", ex == null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message);

        private static void Write(string level, string message)
        {
            if (string.IsNullOrEmpty(Path)) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                    if (File.Exists(Path) && new FileInfo(Path).Length > 1024 * 1024)
                    {
                        var old = Path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Path, old);
                    }
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch (Exception) { /* logging never breaks the launcher */ }
        }
    }
}
