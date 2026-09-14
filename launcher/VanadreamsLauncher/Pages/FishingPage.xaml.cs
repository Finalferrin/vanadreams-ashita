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
    public sealed class CatchRow
    {
        public string When { get; set; }
        public string Kind { get; set; }
        public string What { get; set; }
        public string Bite { get; set; }
        public string Skill { get; set; }
        public Brush Brush => (Brush)Application.Current.FindResource(Kind == "catch" || Kind == "skillup" ? "Ok" : Kind == "lost" || Kind == "break" ? "Bad" : "Mist");
    }

    public partial class FishingPage : UserControl
    {
        private readonly MainWindow _win;
        private string LogPath => Path.Combine(App.State.AshitaRoot, "config", "vanafish", "catchlog.csv");

        public FishingPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            var state = App.State;
            var item = state.Catalog.Find("vanafish");
            var installed = item != null && item.IsInstalled(state.AshitaRoot);
            EnabledBox.IsChecked = state.Settings.EnabledAddons.Contains("vanafish", StringComparer.OrdinalIgnoreCase);
            State.Text = item == null ? "Not in the catalogue yet." : installed ? "✓ Installed" : "Not installed: tick it on the Addons page and press Install.";
            State.Foreground = (Brush)FindResource(installed ? "Ok" : "Warn");
            LoadLog();
        }

        private void LoadLog()
        {
            var rows = new List<CatchRow>();
            if (File.Exists(LogPath))
            {
                foreach (var line in File.ReadAllLines(LogPath).Reverse().Take(500))
                {
                    var f = line.Split(',');
                    if (f.Length < 6) continue;
                    double tenths;
                    rows.Add(new CatchRow { When = f[0], Kind = f[1], What = f[2], Bite = f[3] + (f[4].Length > 0 ? " · " + f[4] : ""), Skill = double.TryParse(f[5], out tenths) ? (tenths / 10).ToString("0.0") : f[5] });
                }
            }
            Log.ItemsSource = rows;
            Heading.Text = rows.Count == 0 ? "✦ Catch log · nothing yet" : "✦ Catch log · " + rows.Count + " entries";
            var catches = rows.Count(r => r.Kind == "catch");
            var top = rows.Where(r => r.Kind == "catch").GroupBy(r => r.What).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key + " ×" + g.Count());
            Totals.Text = rows.Count == 0 ? "" : $"{catches} caught · {rows.Count(r => r.Kind == "lost")} lost · {rows.Count(r => r.Kind == "skillup")} skill-ups\n" + string.Join(", ", top);
        }

        private void Enabled_Click(object sender, RoutedEventArgs e)
        {
            var s = App.State.Settings;
            if (EnabledBox.IsChecked == true) { if (!s.EnabledAddons.Contains("vanafish", StringComparer.OrdinalIgnoreCase)) s.EnabledAddons.Add("vanafish"); }
            else s.EnabledAddons.RemoveAll(x => string.Equals(x, "vanafish", StringComparison.OrdinalIgnoreCase));
            s.Save();
            try { App.State.ApplyEnabledAddons(); } catch (Exception ex) { Services.Log.Warn(ex.Message); }
            App.State.Notify();
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            try { var d = Path.GetDirectoryName(LogPath); Directory.CreateDirectory(d); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + d + "\"") { UseShellExecute = true }); } catch (Exception ex) { Services.Log.Warn(ex.Message); }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
    }
}
