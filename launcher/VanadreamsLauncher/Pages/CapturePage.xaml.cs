using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class CaptureRow
    {
        public CaptureSummary Summary { get; set; }
        public string Title => Summary.Name + "  " + JobName(Summary.MainJob) + Summary.MainLevel;
        public string Detail => $"{Summary.CapturedOn} · {Summary.CapturedAt} · {Summary.Items} items · {Summary.Gil:N0} gil · {Summary.Spells} spells · {Summary.KeyItems} key items";
        public static string JobName(int id)
        {
            var names = new[] { "", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU", "COR", "PUP", "DNC", "SCH", "GEO", "RUN" };
            return id > 0 && id < names.Length ? names[id] : "";
        }
    }

    public partial class CapturePage : UserControl
    {
        private readonly MainWindow _win;
        private readonly List<CaptureRow> _rows = new List<CaptureRow>();
        private string Folder => Path.Combine(App.State.AshitaRoot, "config", "charcapture");
        private CaptureRow Current => List.SelectedItem as CaptureRow;

        public CapturePage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            Load();
        }

        private void Load()
        {
            _rows.Clear();
            if (Directory.Exists(Folder))
                foreach (var f in Directory.GetFiles(Folder, "*.json").OrderByDescending(File.GetLastWriteTime))
                {
                    try { var s = CaptureSummary.Read(f); if (s != null) _rows.Add(new CaptureRow { Summary = s }); }
                    catch (Exception ex) { Log.Warn("snapshot unreadable " + f + ": " + ex.Message); }
                }
            List.ItemsSource = null; List.ItemsSource = _rows;
            Heading.Text = _rows.Count == 0 ? "✦ Snapshots · none yet" : "✦ Snapshots · " + _rows.Count;
            if (_rows.Count > 0) List.SelectedIndex = 0; else Show(null);
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => Show(Current);

        private void Show(CaptureRow row)
        {
            Result.Text = "";
            if (row == null) { SelName.Text = "Nothing to send"; SelMeta.Text = "Take a snapshot in game first."; SelUser.Text = ""; SendButton.IsEnabled = false; return; }
            var s = row.Summary;
            SelName.Text = s.Name;
            SelMeta.Text = $"Captured on {s.CapturedOn}, {s.CapturedAt}. {s.Bytes / 1024} KB.";
            var user = CurrentUser();
            SelUser.Text = string.IsNullOrEmpty(user) ? "No Vanadreams login remembered: set one on the Profiles page, it's what the snapshot is filed under." : "Files under Vanadreams login " + user + ".";
            SelUser.Foreground = (Brush)FindResource(string.IsNullOrEmpty(user) ? "Warn" : "Cream");
            SendButton.IsEnabled = !string.IsNullOrEmpty(user);
        }

        private string CurrentUser()
        {
            var state = App.State;
            var id = state.Settings.LastProfile;
            var cred = string.IsNullOrEmpty(id) ? null : state.Credentials.Get(id);
            if (cred != null && !string.IsNullOrEmpty(cred.User)) return cred.User;
            foreach (var p in state.Profiles.LoadAll().Where(p => !p.IsExample && p.Command.Server.IndexOf("vanadreams", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var c = state.Credentials.Get(p.Id);
                if (c != null && !string.IsNullOrEmpty(c.User)) return c.User;
            }
            return null;
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var row = Current;
            if (row == null) return;
            SendButton.IsEnabled = false;
            Result.Text = "Sending…"; Result.Foreground = (Brush)FindResource("Mist");
            var uploader = new CaptureUploader { Url = App.State.Settings.CaptureUrl };
            var r = await uploader.SendAsync(row.Summary.Path, CurrentUser());
            Result.Text = r.Message;
            Result.Foreground = (Brush)FindResource(r.Ok ? "Ok" : "Bad");
            Log.Info("capture send " + row.Summary.Name + ": " + r.Message);
            SendButton.IsEnabled = true;
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            try { Directory.CreateDirectory(Folder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Folder + "\"") { UseShellExecute = true }); } catch (Exception ex) { Log.Warn(ex.Message); }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
    }
}
