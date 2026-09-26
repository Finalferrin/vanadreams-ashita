using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class VanatunesPage : UserControl
    {
        private const string Id = "vanatunes";
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a" };
        private readonly MainWindow _win;
        private bool _busy;

        public VanatunesPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            Refresh();
        }

        private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private static string MyFolder => Path.Combine(Desktop, "Vanadreams Music");
        private static string OurFolder => Path.Combine(App.State.AshitaRoot, "addons", Id, "music");

        private static int CountSongs(string folder) =>
            Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Count(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) : 0;

        private void Refresh()
        {
            var state = App.State;
            var item = state.Catalog.Find(Id);
            var installed = item != null && item.IsInstalled(state.AshitaRoot);
            EnabledBox.IsChecked = state.Settings.EnabledAddons.Contains(Id, StringComparer.OrdinalIgnoreCase);
            string version;
            state.Settings.InstalledVersions.TryGetValue(Id, out version);
            State.Text = item == null ? "Not in the catalogue yet." : installed ? "✓ Installed" + (string.IsNullOrEmpty(version) ? "" : " · " + version) : "Not installed yet: press Get new songs.";
            State.Foreground = (Brush)FindResource(installed ? "Ok" : "Warn");
            Songs.Text = $"{CountSongs(OurFolder)} Vanadreams songs · {CountSongs(MyFolder)} of yours";
        }

        private void Enabled_Click(object sender, RoutedEventArgs e)
        {
            var s = App.State.Settings;
            if (EnabledBox.IsChecked == true) { if (!s.EnabledAddons.Contains(Id, StringComparer.OrdinalIgnoreCase)) s.EnabledAddons.Add(Id); }
            else s.EnabledAddons.RemoveAll(x => string.Equals(x, Id, StringComparison.OrdinalIgnoreCase));
            s.Save();
            try { App.State.ApplyEnabledAddons(); } catch (Exception ex) { Log.Warn(ex.Message); }
            App.State.Notify();
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            try { Directory.CreateDirectory(MyFolder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + MyFolder + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        // the same install the Addons page does: only missing or changed files come down, songs the repo dropped go
        private async void Get_Click(object sender, RoutedEventArgs e)
        {
            var state = App.State;
            var item = state.Catalog.Find(Id);
            if (_busy || item == null) return;
            if (!state.HasAshita) { Note.Text = "Set the Ashita folder on Settings first."; return; }
            _busy = true; GetButton.IsEnabled = false; Note.Text = "Looking for new songs…";
            try
            {
                var plan = await AddonInstaller.InstallRepoFolderAsync(state.Downloader, state.Settings, state.AshitaRoot, item,
                    (name, n, of) => Dispatcher.Invoke(() => Note.Text = $"{name} ({n} of {of})"));
                if (!state.Settings.EnabledAddons.Contains(Id, StringComparer.OrdinalIgnoreCase)) state.Settings.EnabledAddons.Add(Id);
                state.Settings.Save();
                state.ApplyEnabledAddons();
                Note.Text = plan.Download.Count == 0 && plan.Delete.Count == 0 ? "You have every song already."
                    : $"{plan.Download.Count} new or changed" + (plan.Delete.Count > 0 ? $", {plan.Delete.Count} removed" : "") + ". In game, /vanatunes rescan picks them up.";
                Refresh();
            }
            catch (Exception ex) { Log.Error("vanatunes songs", ex); Note.Text = ex.Message; }
            finally { _busy = false; GetButton.IsEnabled = true; }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
    }
}
