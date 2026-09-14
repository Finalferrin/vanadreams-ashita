using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class AddonRow : INotifyPropertyChanged
    {
        private bool _selected, _enabled;
        public CatalogItem Item { get; set; }
        public bool Installed { get; set; }
        public string InstalledVersion { get; set; }
        public bool HasV4 => Item.HasV4;
        public bool Enabled { get { return _enabled; } set { _enabled = value; Raise("Enabled"); } }
        public bool Selected { get { return _selected; } set { _selected = value; Raise("Selected"); Raise("HandVisibility"); } }
        public Visibility HandVisibility => Selected ? Visibility.Visible : Visibility.Hidden;
        public string Label => Item.Name + (string.IsNullOrEmpty(Item.Version) ? "" : " " + Item.Version) + (!Item.HasV4 && !string.IsNullOrEmpty(Item.Replacement) ? " → " + Item.Replacement : "");
        public double Opacity => Item.HasV4 ? 1.0 : 0.5;
        public string Tag => Item.SourceTag;
        public Brush TagBrush => Item.Source == SourceType.None ? (Brush)Application.Current.FindResource("Bad") : Item.Source == SourceType.Bundled ? (Brush)Application.Current.FindResource("Mist") : (Brush)Application.Current.FindResource("GoldSoft");
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class AddonsPage : UserControl
    {
        private readonly MainWindow _win;
        private readonly List<AddonRow> _rows = new List<AddonRow>();
        private AddonRow _current;
        private bool _busy;

        public AddonsPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            _onChanged = OnStateChanged;
            Build();
            // listen only while on screen; a page that has been left must not keep rebuilding
            Loaded += (s, e) => App.State.Changed += _onChanged;
            Unloaded += (s, e) => App.State.Changed -= _onChanged;
        }

        private readonly Action _onChanged;
        private void OnStateChanged() => Dispatcher.BeginInvoke(new Action(Build));

        private void Build()
        {
            var state = App.State;
            var selectedId = _current?.Item.Id;
            _rows.Clear();
            var order = new[] { SourceType.Bundled, SourceType.RepoFolder, SourceType.GithubRelease, SourceType.None };
            foreach (var item in state.Catalog.Items.OrderBy(i => Array.IndexOf(order, i.Source)).ThenBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                string ver; state.Settings.InstalledVersions.TryGetValue(item.Id, out ver);
                _rows.Add(new AddonRow
                {
                    Item = item,
                    Installed = item.IsInstalled(state.AshitaRoot),
                    InstalledVersion = ver,
                    Enabled = state.Settings.EnabledAddons.Contains(item.Id, StringComparer.OrdinalIgnoreCase),
                });
            }
            List.ItemsSource = null; List.ItemsSource = _rows;
            var enabledCount = _rows.Count(r => r.Enabled && r.HasV4);
            Heading.Text = $"✦ Your addons · {enabledCount} of {_rows.Count(r => r.HasV4)} enabled" + (state.CatalogFromCache ? " · catalogue from cache" : "");
            var sel = _rows.FirstOrDefault(r => r.Item.Id == selectedId) ?? _rows.FirstOrDefault();
            Select(sel);
        }

        private void Select(AddonRow row)
        {
            foreach (var r in _rows) r.Selected = false;
            _current = row;
            if (row == null) { ItemName.Text = "No catalogue yet"; ItemMeta.Text = "The catalogue could not be loaded. Check Settings for the catalogue address."; return; }
            row.Selected = true;
            var i = row.Item;
            ItemName.Text = i.Name;
            var kind = i.Kind == "polplugin" ? "POL plugin" : i.Kind == "plugin" ? "Plugin" : "Addon";
            ItemMeta.Text = kind + (string.IsNullOrEmpty(i.Maintainer) && string.IsNullOrEmpty(i.Repo) ? "" : " by " + (i.Maintainer ?? i.Repo.Split('/')[0])) + (string.IsNullOrEmpty(i.Version) ? "" : " · " + i.Version) + (i.Source == SourceType.GithubRelease ? " · built for interface " + App.State.Catalog.Interface : "");
            ItemDesc.Text = i.Description ?? "";
            if (!i.HasV4)
            {
                ItemState.Text = "No v4 version exists." + (string.IsNullOrEmpty(i.Replacement) ? "" : " Use " + i.Replacement + " instead.");
                ItemState.Foreground = (Brush)FindResource("Bad");
            }
            else if (i.Source == SourceType.Bundled)
            {
                ItemState.Text = row.Installed ? "✓ Ships with Ashita v4" : "Ships with Ashita v4, but the file is missing. Run Repair.";
                ItemState.Foreground = (Brush)FindResource(row.Installed ? "Ok" : "Warn");
            }
            else if (row.Installed)
            {
                var stale = !string.IsNullOrEmpty(i.Version) && !string.Equals(row.InstalledVersion, i.Version, StringComparison.OrdinalIgnoreCase);
                ItemState.Text = stale ? $"Installed {row.InstalledVersion ?? "(unknown)"} · {i.Version} available" : $"✓ Installed {row.InstalledVersion ?? i.Version ?? ""}";
                ItemState.Foreground = (Brush)FindResource(stale ? "Warn" : "Ok");
            }
            else
            {
                ItemState.Text = "Not installed";
                ItemState.Foreground = (Brush)FindResource("Mist");
            }
            ItemState.Text += string.IsNullOrEmpty(i.Load) ? "" : "\nLoads as " + (i.LoadKind == LoadKind.PolPlugin ? "POL plugin " + i.LoadName + " (boot ini)" : i.Load);
            ItemV3.Text = i.V3Note != null ? "From v3: " + i.V3Note : i.V3Name != null ? "From v3: " + i.V3Name + (i.V3Carry != null ? ", " + i.V3Carry + " carried over" : "") : "";
            InstallButton.Visibility = i.HasV4 && i.Source != SourceType.Bundled ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.Content = !row.Installed ? "Install" : (!string.IsNullOrEmpty(i.Version) && !string.Equals(row.InstalledVersion, i.Version, StringComparison.OrdinalIgnoreCase)) ? "Update to " + i.Version : "Reinstall";
            DocsButton.Visibility = string.IsNullOrEmpty(i.Docs) ? Visibility.Collapsed : Visibility.Visible;
            ConfigButton.Visibility = string.IsNullOrEmpty(i.Config) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Row_Click(object sender, MouseButtonEventArgs e)
        {
            var row = ((FrameworkElement)sender).Tag as AddonRow;
            if (row != null) Select(row);
        }

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            var row = ((FrameworkElement)sender).Tag as AddonRow;
            if (row != null) Select(row);
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _busy) return;
            var item = _current.Item;
            var state = App.State;
            if (!state.HasAshita) { ProgressText.Visibility = Visibility.Visible; ProgressText.Text = "Set the Ashita folder on Settings first."; return; }
            _busy = true; InstallButton.IsEnabled = false;
            Progress.Visibility = ProgressText.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true; ProgressText.Text = "Looking up " + item.Name + "…";
            try
            {
                var progress = new Progress<DownloadProgress>(p => { Progress.IsIndeterminate = false; Progress.Value = p.Fraction * 100; ProgressText.Text = $"{p.Label}: {p.Done / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB"; });
                if (item.Source == SourceType.GithubRelease)
                {
                    var asset = await state.Downloader.LatestReleaseAssetAsync(item.Repo, item.Asset);
                    if (asset == null) throw new InvalidOperationException("No release asset matching " + item.Asset + " on " + item.Repo + ".");
                    var zip = Path.Combine(state.Settings.DownloadsFolder, item.Id + "-" + asset.Tag + "-" + asset.Name);
                    if (!File.Exists(zip) || new FileInfo(zip).Length != asset.Size)
                        await state.Downloader.DownloadFileAsync(asset.Url, zip, progress, asset.Name, asset.Size);
                    ProgressText.Text = "Unpacking…";
                    // the catalogue's install field says where the archive goes; the default is the Ashita root
                    var unzipTo = item.Install == InstallAction.CopyToAddons ? Path.Combine(state.AshitaRoot, "addons", item.LoadName ?? item.Id)
                                : item.Install == InstallAction.PivotOverlay ? Path.Combine(PivotConfig.OverlaysRoot(state.AshitaRoot), item.Id)
                                : state.AshitaRoot;
                    await Task.Run(() => Downloader.ExtractZipOverwrite(zip, unzipTo));
                    if (item.Install == InstallAction.PivotOverlay) PivotConfig.AddOverlay(state.AshitaRoot, item.Id);
                    state.Settings.InstalledVersions[item.Id] = item.Version ?? asset.Tag;
                }
                else if (item.Source == SourceType.RepoFolder)
                {
                    var files = await state.Downloader.ListRepoFolderAsync(item.Repo, item.Path, item.Branch);
                    if (files.Count == 0) throw new InvalidOperationException("Nothing found at " + item.Repo + "/" + item.Path + ".");
                    var overlay = item.Install == InstallAction.PivotOverlay;
                    var target = overlay ? Path.Combine(PivotConfig.OverlaysRoot(state.AshitaRoot), item.Id)
                                         : Path.Combine(state.AshitaRoot, "addons", item.LoadName ?? item.Id);
                    var n = 0;
                    foreach (var f in files)
                    {
                        n++;
                        ProgressText.Text = $"{f.Key} ({n} of {files.Count})";
                        Progress.IsIndeterminate = false; Progress.Value = 100.0 * n / files.Count;
                        var dest = Path.Combine(target, f.Key.Replace('/', '\\'));
                        await state.Downloader.DownloadFileAsync(f.Value, dest);
                    }
                    state.Settings.InstalledVersions[item.Id] = item.Version ?? DateTime.Now.ToString("yyyy-MM-dd");
                    if (overlay) PivotConfig.AddOverlay(state.AshitaRoot, item.Id);
                }
                // enabled, saved and applied in one motion, so Play right after Install loads it
                _current.Enabled = true;
                if (!state.Settings.EnabledAddons.Contains(item.Id, StringComparer.OrdinalIgnoreCase)) state.Settings.EnabledAddons.Add(item.Id);
                state.Settings.Save();
                state.ApplyEnabledAddons();
                Log.Info("installed " + item.Id);
                var pivot = item.Install == InstallAction.PivotOverlay ? state.Catalog.Find("pivot") : null;
                ProgressText.Text = pivot != null && !pivot.IsInstalled(state.AshitaRoot) ? "Installed. It needs XIPivot to show in game: install that too, then Save." : "Installed.";
                Build();
            }
            catch (Exception ex)
            {
                Log.Error("install " + item.Id, ex);
                ProgressText.Text = ex.Message;
            }
            finally
            {
                _busy = false; InstallButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed; Progress.IsIndeterminate = false;
            }
        }

        private void Docs_Click(object sender, RoutedEventArgs e) { if (_current?.Item.Docs != null) MenuPage.OpenUrl(_current.Item.Docs); }

        private void Config_Click(object sender, RoutedEventArgs e)
        {
            if (_current?.Item.Config == null) return;
            var path = Path.Combine(App.State.AshitaRoot, _current.Item.Config);
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch (Exception ex) { Log.Warn(ex.Message); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var state = App.State;
            state.Settings.EnabledAddons = _rows.Where(r => r.Enabled && r.HasV4).Select(r => r.Item.Id).ToList();
            state.Settings.Save();
            try
            {
                state.ApplyEnabledAddons();
                Footer.Text = "Saved: scripts\\vanadreams.txt written for " + state.Settings.EnabledAddons.Count + " item(s).";
                Footer.Foreground = (Brush)FindResource("Ok");
            }
            catch (Exception ex)
            {
                Log.Error("save script", ex);
                Footer.Text = ex.Message; Footer.Foreground = (Brush)FindResource("Bad");
            }
            state.Notify();
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
    }
}
