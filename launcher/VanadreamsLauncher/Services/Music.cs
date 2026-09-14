using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Vanadreams.Services
{
    /// <summary>
    /// The launcher's music: one track, looped quietly while the launcher is open, faded out when the game starts.
    /// The track ships inside the exe and is unpacked to the data folder on first play, because MediaPlayer
    /// needs a real file. Credits are in CREDITS.md at the repo root and on the Settings page.
    /// </summary>
    public static class Music
    {
        public const string Title = "The Lanterns Are Lit";
        public const string Credit = "Music: \"The Lanterns Are Lit\", made for Vanadreams with SoundBreak.";
        private const string ResourcePath = "assets/music/the-lanterns-are-lit.mp3";
        private const double Volume = 0.35;

        private static MediaPlayer _player;
        private static DispatcherTimer _fade;

        public static bool Playing => _player != null && _fade == null;

        public static string TrackPath => Path.Combine(LauncherSettings.DataFolder, "music", "the-lanterns-are-lit.mp3");

        /// <summary>Start the loop if music is on. Safe to call more than once.</summary>
        public static void Start()
        {
            if (_player != null && _fade != null) { _fade.Stop(); _fade = null; _player.Volume = Volume; return; }   // mid-fade: keep it
            if (_player != null) return;
            try
            {
                var path = Unpack();
                if (path == null) return;
                var p = new MediaPlayer { Volume = Volume };
                p.MediaEnded += (s, e) => { p.Position = TimeSpan.Zero; p.Play(); };
                p.MediaFailed += (s, e) => { Log.Warn("music: " + e.ErrorException.Message); Stop(); };
                p.Open(new Uri(path));
                p.Play();
                _player = p;
            }
            catch (Exception ex) { Log.Warn("music: " + ex.Message); _player = null; }
        }

        /// <summary>Stop at once.</summary>
        public static void Stop()
        {
            if (_fade != null) { _fade.Stop(); _fade = null; }
            var p = _player; _player = null;
            if (p == null) return;
            try { p.Stop(); p.Close(); } catch (Exception) { }
        }

        /// <summary>Bring the volume down over a few seconds, then stop. Used when the game is starting.</summary>
        public static void FadeOut(double seconds = 3)
        {
            var p = _player;
            if (p == null || _fade != null) return;
            var steps = Math.Max(1, (int)(seconds * 20));
            var step = p.Volume / steps;
            _fade = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds / steps) };
            var timer = _fade;
            timer.Tick += (s, e) =>
            {
                if (_fade != timer) { timer.Stop(); return; }   // cancelled by Start or Stop
                if (_player == null) { timer.Stop(); _fade = null; return; }
                var v = p.Volume - step;
                if (v <= 0.01) { Stop(); return; }
                p.Volume = v;
            };
            timer.Start();
        }

        /// <summary>Copy the track out of the exe into the data folder. Returns the file path, or null if the resource is missing.</summary>
        private static string Unpack()
        {
            var target = TrackPath;
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/" + ResourcePath));
            if (res == null) return File.Exists(target) ? target : null;
            using (var s = res.Stream)
            {
                if (File.Exists(target) && new FileInfo(target).Length == s.Length) return target;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var f = File.Create(target)) s.CopyTo(f);
            }
            return target;
        }
    }
}
