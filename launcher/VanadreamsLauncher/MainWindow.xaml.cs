using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vanadreams.Pages;
using Vanadreams.Services;

namespace Vanadreams
{
    public partial class MainWindow : Window
    {
        public AppState State => App.State;
        private readonly DispatcherTimer _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };

        public MainWindow()
        {
            InitializeComponent();
            DrawStars();
            State.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshStrip));
            Loaded += OnLoaded;
            _statusTimer.Tick += async (s, e) => await State.RefreshStatusAsync();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (State.HasAshita) GameLauncher.Sweep(State.AshitaRoot);
            State.CheckVersion();
            RefreshStrip();
            RefreshMute();
            if (!string.IsNullOrEmpty(App.SnapshotPath)) { await SnapshotAndExit(); return; }
            if (State.HasAshita) Navigate(new MenuPage(this)); else Navigate(new SetupPage(this));
            // music after the first frame, so the unpack on a first run never delays the window
            if (State.Settings.MusicOn) Dispatcher.BeginInvoke(new Action(Music.Start), System.Windows.Threading.DispatcherPriority.Background);
            _statusTimer.Start();
            await State.RefreshStatusAsync();
            await State.RefreshCatalogAsync();
            await CheckForUpdateAsync();
        }

        private string _updatedExe;

        /// <summary>Once per start: a newer signed release is fetched and swapped in; the strip offers the restart.</summary>
        private async Task CheckForUpdateAsync()
        {
            try
            {
                var asset = await Updater.CheckAsync(State.Downloader);
                if (asset == null) return;
                NewsLine.Text = "Update " + asset.Tag + " found, downloading…";
                var exe = await Updater.FetchAsync(State.Downloader, asset, State.Settings.DownloadsFolder);
                if (exe == null) { NewsLine.Text = "Update " + asset.Tag + " could not be fetched; it is on fairywitch.ca."; return; }
                _updatedExe = Updater.Apply(exe);
                NewsLine.Text = "Launcher " + asset.Tag + " is ready.";
                UpdateButton.Content = "Restart into " + asset.Tag;
                UpdateButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { Log.Warn("update: " + ex.Message); }
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_updatedExe == null) return;
            Updater.Restart(_updatedExe);
        }

        public void Navigate(UserControl page)
        {
            Page.Content = page;
            page.Focus();
            RefreshMute();
        }

        /// <summary>The corner button: music on or off, remembered in settings, and the Settings page follows it.</summary>
        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            var s = State.Settings;
            if (s.MusicOn && !Music.Playing) { Music.Start(); RefreshMute(); return; }   // faded out after Play: bring it back
            s.MusicOn = !s.MusicOn;
            s.Save();
            if (s.MusicOn) Music.Start(); else Music.Stop();
            RefreshMute();
            var settings = Page.Content as Pages.SettingsPage;
            if (settings != null) settings.SyncMusic();
        }

        public void RefreshMute()
        {
            var on = State.Settings.MusicOn;
            MuteButton.Content = !on ? "♪ music off" : Music.Playing ? "♪ mute" : "♪ play";
            MuteButton.Opacity = on && Music.Playing ? 0.75 : 1.0;
        }

        public void RefreshStrip()
        {
            var s = State.Status;
            StatusWord.Text = s.StateWord;
            StatusWhen.Text = s.CheckedWord + (s.FromCache && s.CheckedAt.HasValue ? " (last seen)" : "")
                            + (s.Online.HasValue && !s.FromCache ? " · " + s.Online.Value + (s.Online.Value == 1 ? " online" : " online") : "");
            StatusNote.Text = string.IsNullOrWhiteSpace(s.Note) ? (s.Error != null ? "Couldn't reach fairywitch.ca." : "") : s.Note;
            StatusNote.ToolTip = string.IsNullOrWhiteSpace(StatusNote.Text) ? null : StatusNote.Text;   // the whole note, however long
            Brush dot;
            switch (s.State)
            {
                case ServerState.Online: dot = (Brush)FindResource("Ok"); break;
                case ServerState.Maintenance: dot = (Brush)FindResource("Warn"); break;
                case ServerState.Offline: dot = (Brush)FindResource("Bad"); break;
                default: dot = (Brush)FindResource("Mist"); break;
            }
            StatusDot.Fill = dot;
            StatusDot.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ((SolidColorBrush)dot).Color, BlurRadius = 8, ShadowDepth = 0 };
            var v = State.Version;
            VersionLine.Text = v.Sentence;
            VersionLine.Foreground = (Brush)FindResource(!v.ExpectedIsPublished ? "Mist" : v.Verdict == VersionVerdict.Ready ? "Ok" : v.Verdict == VersionVerdict.Unknown ? "Warn" : "Bad");
            // the note already sits beside the status pill; News keeps its own line
        }

        private void DrawStars()
        {
            var rnd = new Random(1313);
            for (var i = 0; i < 90; i++)
            {
                var size = rnd.NextDouble() < 0.2 ? 2.0 : 1.0;
                var star = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.35 + rnd.NextDouble() * 0.5 };
                Canvas.SetLeft(star, rnd.NextDouble() * 1000);
                Canvas.SetTop(star, rnd.NextDouble() * 600);
                Stars.Children.Add(star);
            }
        }

        private async Task SnapshotAndExit()
        {
            UserControl page;
            switch ((App.SnapshotPage ?? "menu").ToLowerInvariant())
            {
                case "profiles": page = new ProfilesPage(this); break;
                case "addons": page = new AddonsPage(this); break;
                case "setup": page = new SetupPage(this); break;
                case "settings": page = new SettingsPage(this); break;
                case "fishing": page = new FishingPage(this); break;
                case "capture": page = new CapturePage(this); break;
                default: page = new MenuPage(this); break;
            }
            Navigate(page);
            await Task.Delay(700);
            var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(this);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(App.SnapshotPath)) enc.Save(f);
            if (App.SnapshotPage == "guide")
            {
                var g = new GuideWindow(State); g.Show(); await Task.Delay(500);
                var gb = new RenderTargetBitmap((int)g.ActualWidth, (int)g.ActualHeight, 96, 96, PixelFormats.Pbgra32); gb.Render(g);
                var ge = new PngBitmapEncoder(); ge.Frames.Add(BitmapFrame.Create(gb));
                using (var f = File.Create(System.IO.Path.ChangeExtension(App.SnapshotPath, ".guide.png"))) ge.Save(f);
            }
            Application.Current.Shutdown();
        }
    }
}
