using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly MainWindow _win;

        public SettingsPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            var s = App.State.Settings;
            AshitaBox.Text = s.AshitaRoot; FfxiBox.Text = s.FfxiFolderOverride; CatalogBox.Text = s.CatalogUrl; StatusBox.Text = s.StatusUrl;
            VerBox.Text = s.ExpectedClientVer; LockBox.SelectedIndex = Math.Max(0, Math.Min(2, s.VerLock));
            DetectedBox.Text = ClientVersion.FindFfxiFolder() ?? "not registered by PlayOnline";
            MusicBox.IsChecked = s.MusicOn;
            MusicCredit.Text = "made for Vanadreams with SoundBreak";
            About.Text = "Vanadreams Launcher " + typeof(App).Assembly.GetName().Version + " · github.com/Finalferrin/vanadreams-ashita · no telemetry";
        }

        /// <summary>The corner mute changed the setting; show it.</summary>
        public void SyncMusic() => MusicBox.IsChecked = App.State.Settings.MusicOn;

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = App.State.Settings;
            s.AshitaRoot = AshitaBox.Text.Trim(); s.FfxiFolderOverride = FfxiBox.Text.Trim(); s.CatalogUrl = CatalogBox.Text.Trim(); s.StatusUrl = StatusBox.Text.Trim();
            s.ExpectedClientVer = VerBox.Text.Trim(); s.VerLock = LockBox.SelectedIndex;
            s.MusicOn = MusicBox.IsChecked == true;
            s.Save();
            if (s.MusicOn) Music.Start(); else Music.Stop();
            _win.RefreshMute();
            App.State.CheckVersion();
            Note.Text = "Saved.";
            App.State.Notify();
            await App.State.RefreshStatusAsync();
        }

        private async void Catalog_Click(object sender, RoutedEventArgs e)
        {
            Note.Text = "Fetching…";
            await App.State.RefreshCatalogAsync();
            Note.Text = App.State.CatalogFromCache ? "Could not fetch; using the cached copy." : "Catalogue updated: " + App.State.Catalog.Items.Count + " items.";
        }

        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!SelfInstall.IsInstalledCopy) { SelfInstall.Install(); Note.Text = "Installed to " + SelfInstall.InstallDir + " with shortcuts. Use the shortcut from now on."; }
                else { SelfInstall.MakeShortcuts(); Note.Text = "Shortcuts made."; }
            }
            catch (Exception ex) { Note.Text = ex.Message; }
        }

        private void Data_Click(object sender, RoutedEventArgs e) => Open(LauncherSettings.DataFolder);
        private void Log_Click(object sender, RoutedEventArgs e) => Open(App.State.Settings.LogPath);
        private void Open(string path)
        {
            try { if (!File.Exists(path) && !Directory.Exists(path)) Directory.CreateDirectory(LauncherSettings.DataFolder); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Note.Text = ex.Message; }
        }
        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
    }
}
