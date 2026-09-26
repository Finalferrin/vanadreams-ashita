using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class InstallPage : UserControl
    {
        private readonly MainWindow _win;
        private string _root = ClientInstall.DefaultRoot;
        private ClientManifest _manifest;
        private CancellationTokenSource _cancel;

        private readonly bool _asNewPlayer;

        /// <param name="asNewPlayer">Snapshot hook: show the page as a PC with no game on it sees it.</param>
        public InstallPage(MainWindow win, bool asNewPlayer = false)
        {
            InitializeComponent();
            _win = win;
            _asNewPlayer = asNewPlayer;
            Loaded += async (s, e) => { Refresh(); await LoadManifestAsync(); };
        }

        private static string Gb(long bytes) => (bytes / 1073741824.0).ToString("0.0") + " GB";
        private string Expected => App.State.Version.ExpectedIsPublished ? App.State.Version.Expected : null;

        private void Refresh()
        {
            var found = _asNewPlayer ? null : ClientVersion.FindFfxiFolder();
            var have = ClientVersion.ReadInstalled(found);
            var want = Expected;
            FolderText.Text = ClientInstall.GameFolder(_root);
            VersionText.Text = want == null ? "the server hasn't published its version yet" : want + " · the version the server runs";
            FoundText.Text = found == null ? "none on this PC" : (have ?? "version unknown") + " · " + found.TrimEnd('\\');
            var matches = found != null && have != null && want != null && string.CompareOrdinal(have.Substring(0, 6), want.Substring(0, 6)) >= 0;
            State.Text = found == null ? "Not installed yet." : matches ? "✓ Installed · matches the server" : "Installed, but older than the server needs. Install game fetches the right one.";
            State.Foreground = (Brush)FindResource(found == null ? "Warn" : matches ? "Ok" : "Warn");
            InstallButton.Content = _cancel != null ? "Stop" : found == null ? "Install game" : "Install a fresh copy";
        }

        private async Task LoadManifestAsync()
        {
            if (Expected == null) { SizeText.Text = "known once the server's version is published"; return; }
            SizeText.Text = "checking…";
            try
            {
                _manifest = ClientManifest.Parse(await App.State.Downloader.GetStringAsync(ClientInstall.ManifestUrl(Expected)));
                SizeText.Text = Gb(_manifest.TotalSize) + " · " + Gb(_manifest.TotalUnpacked) + " on disk";
            }
            catch (Exception ex)
            {
                Log.Warn("client manifest: " + ex.Message);
                SizeText.Text = "the download isn't available right now";
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_cancel != null) { _cancel.Cancel(); return; }
            if (_manifest == null) { await LoadManifestAsync(); if (_manifest == null) { ProgressText.Text = "The game download isn't available right now. Try again in a while."; return; } }

            var drive = new DriveInfo(Path.GetPathRoot(_root));
            var needed = _manifest.TotalSize + _manifest.TotalUnpacked;
            if (drive.AvailableFreeSpace < needed) { ProgressText.Text = $"{drive.Name} has {Gb(drive.AvailableFreeSpace)} free and the install needs {Gb(needed)}. Pick another folder."; return; }

            var state = App.State;
            _cancel = new CancellationTokenSource();
            FolderButton.IsEnabled = false; Refresh();
            Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = false;
            var started = DateTime.UtcNow; long startedAt = -1;
            var progress = new Progress<InstallProgress>(p =>
            {
                Progress.Value = 100.0 * p.Done / Math.Max(1, p.Total);
                if (startedAt < 0) startedAt = p.Done;
                var secs = (DateTime.UtcNow - started).TotalSeconds;
                var rate = secs > 5 ? (p.Done - startedAt) / secs : 0;
                var left = rate > 0 ? " · about " + Math.Max(1, (int)Math.Ceiling((p.Total - p.Done) / rate / 60)) + " min left" : "";
                ProgressText.Text = $"{p.Stage} part {p.Part} of {p.Parts} · {Gb(p.Done)} of {Gb(p.Total)}{left}";
            });
            try
            {
                await ClientInstall.InstallFilesAsync(state.Downloader, _manifest, _root, state.Settings.DownloadsFolder, progress, _cancel.Token);
                ProgressText.Text = "Files in place. Windows will ask for permission to register the game…";
                var ok = await Task.Run(() => ClientInstall.RunRegister(_root, state.Settings.DownloadsFolder));
                ProgressText.Text = ok ? "Installed. Run Setup if you haven't, then press Play."
                                       : "The permission prompt was refused, so the game isn't registered yet. Press Install game again to finish; nothing downloads twice.";
                state.CheckVersion();
                state.Notify();
            }
            catch (OperationCanceledException) { ProgressText.Text = "Stopped. Press Install game to carry on where it left off."; }
            catch (Exception ex) { Log.Error("client install", ex); ProgressText.Text = ex.Message; }
            finally
            {
                _cancel = null; FolderButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed;
                Refresh();
            }
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Pick the folder the game goes in (Final Fantasy XI and PlayOnlineViewer are made inside it)", SelectedPath = _root };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _root = d.SelectedPath;
            Refresh();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_cancel != null) { ProgressText.Text = "Stop the install first."; return; }
            _win.Navigate(new MenuPage(_win));
        }
    }
}
