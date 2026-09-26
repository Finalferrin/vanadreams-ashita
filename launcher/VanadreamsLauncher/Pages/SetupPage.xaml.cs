using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public partial class SetupPage : UserControl
    {
        private const string AshitaZipUrl = "https://github.com/AshitaXI/Ashita-v4beta/archive/refs/heads/main.zip";
        private const string XiloaderRepo = "LandSandBoat/xiloader";
        private readonly MainWindow _win;
        private string _v3;
        private bool _busy;

        public SetupPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            var state = App.State;
            FolderBox.Text = string.IsNullOrWhiteSpace(state.AshitaRoot) ? @"C:\Games\Vanadreams" : state.AshitaRoot;
            BackButton.Visibility = state.HasAshita ? Visibility.Visible : Visibility.Collapsed;
            if (state.HasAshita) { Title.Text = "Repair or update Ashita"; GoButton.Content = "Check and update"; }
            _v3 = V3Import.FindInstalls().FirstOrDefault();
            if (_v3 != null)
            {
                ImportPanel.Visibility = Visibility.Visible;
                ImportLabel.Text = "Ashita v3 found at " + _v3;
            }
            FolderBox.TextChanged += (s, e) => UpdateAdopt();
            UpdateAdopt();
        }

        /// <summary>The typed folder as a usable root: trailing slash off, except that a drive root keeps its slash.</summary>
        internal static string RootFrom(string text)
        {
            var t = (text ?? "").Trim().TrimEnd('\\', '/');
            if (t.Length == 2 && t[1] == ':') t += "\\";   // "D:" alone is drive-relative, which is never what was meant
            return t;
        }

        private void UpdateAdopt()
        {
            var root = RootFrom(FolderBox.Text);
            var existing = root.Length > 0 && File.Exists(Path.Combine(root, "Ashita-cli.exe"));
            AdoptButton.Visibility = existing && !string.Equals(root, App.State.AshitaRoot, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            if (existing) Say("An Ashita v4 install is already in that folder. Update it, or use it as it is.", "GoldSoft");
        }

        /// <summary>Register an Ashita v4 folder the player already has, write the profile, download nothing.</summary>
        private void Adopt_Click(object sender, RoutedEventArgs e)
        {
            var root = RootFrom(FolderBox.Text);
            if (!File.Exists(Path.Combine(root, "Ashita-cli.exe"))) return;
            var state = App.State;
            try
            {
                state.Settings.AshitaRoot = root;
                state.Settings.Save();
                var store = state.Profiles;
                var vdPath = store.PathFor("vanadreams");
                if (!File.Exists(vdPath))
                {
                    var template = store.TemplatePath();
                    if (template == null) throw new InvalidOperationException("No example profile in " + store.BootDir + " to copy from.");
                    var p = Profile.Load(template).DuplicateTo(vdPath, "Vanadreams");
                    p.Command = new LoaderCommand { Server = "vanadreams.fairywitch.ca" };
                    var loader = Path.Combine(root, "bootloader", "xiloader.exe");
                    p.BootFile = File.Exists(loader) ? loader : p.BootFile;
                    p.Script = "vanadreams.txt";
                    p.Save();
                    if (!File.Exists(loader)) Say("Profile written. No xiloader in bootloader\\ yet: press Check and update to fetch it, or browse to yours on the Profiles page.", "Warn");
                    else Say("Using " + root + ". Vanadreams profile written.", "Ok");
                }
                else Say("Using " + root + ". Vanadreams profile already there.", "Ok");
                state.Settings.LastProfile = "vanadreams";
                state.Settings.SetupDone = true;
                state.Settings.Save();
                state.ApplyEnabledAddons();
                state.CheckVersion();
                state.Notify();
                _win.RefreshStrip();
                BackButton.Visibility = Visibility.Visible;
                Title.Text = "Repair or update Ashita"; GoButton.Content = "Check and update";
                UpdateAdopt();
            }
            catch (Exception ex) { Log.Error("adopt", ex); Say(ex.Message, "Bad"); }
        }

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Pick or make the folder Ashita v4 lives in", SelectedPath = FolderBox.Text };
            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) FolderBox.Text = d.SelectedPath;
        }

        private async void Go_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var root = RootFrom(FolderBox.Text);
            if (string.IsNullOrWhiteSpace(root)) { Say("Choose a folder first.", "Warn"); return; }
            if (ClientVersion.IsUnderProgramFiles(root)) { Say("Not under Program Files: pick a folder you own, like C:\\Games\\Vanadreams.", "Warn"); return; }
            _busy = true; GoButton.IsEnabled = false;
            var state = App.State;
            try
            {
                Directory.CreateDirectory(root);
                var progress = new Progress<DownloadProgress>(p => { Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = false; Progress.Value = p.Fraction * 100; Status.Text = $"{p.Label}: {p.Done / 1048576.0:0.0} of {(p.Total > 0 ? p.Total / 1048576.0 : 0):0.0} MB"; });

                // 1. Ashita v4 beta
                Mark(Step1);
                var ashitaZip = Path.Combine(state.Settings.DownloadsFolder, "Ashita-v4beta-main.zip");
                Say("Downloading Ashita v4 beta…");
                await state.Downloader.DownloadFileAsync(AshitaZipUrl, ashitaZip, progress, "Ashita v4 beta");
                Say("Unpacking Ashita…");
                await Task.Run(() => UnpackStrippingTopFolder(ashitaZip, root));
                if (!File.Exists(Path.Combine(root, "Ashita-cli.exe"))) throw new InvalidOperationException("Ashita-cli.exe did not appear after unpacking; the archive layout changed.");
                Done(Step1);

                // 2. xiloader
                Mark(Step2);
                Say("Looking up the latest xiloader…");
                var asset = await state.Downloader.LatestReleaseAssetAsync(XiloaderRepo, "xiloader.exe") ?? await state.Downloader.LatestReleaseAssetAsync(XiloaderRepo, "*.exe");
                if (asset == null) throw new InvalidOperationException("No xiloader.exe found on LandSandBoat's releases.");
                var loaderTmp = Path.Combine(state.Settings.DownloadsFolder, "xiloader-" + asset.Tag + ".exe");
                if (!File.Exists(loaderTmp) || new FileInfo(loaderTmp).Length != asset.Size)
                    await state.Downloader.DownloadFileAsync(asset.Url, loaderTmp, progress, "xiloader " + asset.Tag, asset.Size);
                Directory.CreateDirectory(Path.Combine(root, "bootloader"));
                File.Copy(loaderTmp, Path.Combine(root, "bootloader", "xiloader.exe"), true);
                Done(Step2);

                // 3. profile
                Mark(Step3);
                state.Settings.AshitaRoot = root;
                state.Settings.Save();
                var store = state.Profiles;
                var vdPath = store.PathFor("vanadreams");
                if (!File.Exists(vdPath))
                {
                    var template = store.TemplatePath();
                    if (template == null) throw new InvalidOperationException("Ashita's example profiles are missing from " + store.BootDir + ".");
                    var p = Profile.Load(template).DuplicateTo(vdPath, "Vanadreams");
                    p.Command = new LoaderCommand { Server = "vanadreams.fairywitch.ca" };
                    p.BootFile = Path.Combine(root, "bootloader", "xiloader.exe");
                    p.Script = "vanadreams.txt";
                    p.AutoClose = true;
                    // a new player starts borderless at their own screen's size, never fullscreen
                    if (p.Width <= 0) { var s = ScreenSize.PrimaryPixels(); p.Width = s.Width; p.Height = s.Height; p.MenuWidth = s.Width; p.MenuHeight = s.Height; }
                    if (p.Mode == WindowMode.Registry || p.Mode == WindowMode.Fullscreen) p.Mode = WindowMode.Borderless;
                    p.Save();
                }
                state.Settings.LastProfile = "vanadreams";
                Done(Step3);

                // 4. v3 import
                Mark(Step4);
                if (_v3 != null && (ImportProfiles.IsChecked == true || ImportConfigs.IsChecked == true))
                {
                    var lines = new List<string>();
                    if (ImportProfiles.IsChecked == true)
                    {
                        List<Credential> logins; List<string> ids;
                        var rep = V3Import.ImportProfiles(_v3, root, out logins, out ids);
                        lines.AddRange(rep.Lines);
                        for (var i = 0; i < ids.Count && i < logins.Count; i++) if (logins[i] != null) state.Credentials.Set(ids[i], logins[i].User, logins[i].Password);
                    }
                    if (ImportConfigs.IsChecked == true) lines.AddRange(V3Import.ImportConfigs(_v3, root).Lines);
                    Log.Info("v3 import: " + string.Join(" | ", lines));
                    Step4.Text = "4. From v3: " + string.Join(" ", lines.Take(4));
                }
                else Step4.Text = "4. No v3 install found, nothing to bring over.";
                Done(Step4);

                // 5. addons: a new install starts with none switched on, and a repair run leaves the
                // player's ticks alone. Setup used to tick every addon that ships inside Ashita, all 86,
                // on every run - and some of those are wrong for this server (chatfix rewrites chat,
                // tells and menus for an older packet layout; ime makes typed text need Enter twice).
                // Players tick what they want on the Addons page. The launcher's own music is a
                // setting (MusicOn, on by default), not an addon, so it is not touched here.
                // The exception is whatever the catalogue marks onByDefault (the Vanadreams playlist):
                // given once, then the player's to untick.
                Mark(Step5);
                if (state.Catalog.Items.Count == 0) await state.RefreshCatalogAsync();
                await state.GiveDefaultsAsync();
                state.Settings.SetupDone = true;
                state.Settings.Save();
                state.ApplyEnabledAddons();
                state.CheckVersion();
                Done(Step5);
                Say("Done. Ashita is at " + root + ".", "Ok");
                state.Notify();
                _win.RefreshStrip();
                BackButton.Visibility = Visibility.Visible;
                Title.Text = "Repair or update Ashita"; GoButton.Content = "Check and update";
            }
            catch (Exception ex)
            {
                Log.Error("setup", ex);
                Say(ex.Message + " Press the button again to retry; nothing already fetched is fetched twice.", "Bad");
            }
            finally
            {
                _busy = false; GoButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>GitHub's branch zip nests everything under Ashita-v4beta-main\; drop that level.</summary>
        private static void UnpackStrippingTopFolder(string zip, string root)
        {
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var parts = entry.FullName.Split('/');
                    var rel = string.Join("\\", parts.Skip(1));
                    if (rel.Length == 0) continue;
                    var target = Path.GetFullPath(Path.Combine(fullRoot, rel));
                    if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    // never overwrite a player's own profiles or scripts on repair
                    if ((rel.StartsWith("config\\boot\\", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(rel).StartsWith("example", StringComparison.OrdinalIgnoreCase)) ||
                        rel.Equals("scripts\\vanadreams.txt", StringComparison.OrdinalIgnoreCase))
                        if (File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private void Say(string text, string brush = "Cream") { Status.Text = text; Status.Foreground = (Brush)FindResource(brush); }
        private void Mark(TextBlock step) { step.Foreground = (Brush)FindResource("GoldSoft"); }
        private void Done(TextBlock step) { step.Foreground = (Brush)FindResource("Ok"); if (!step.Text.StartsWith("✓")) step.Text = "✓ " + step.Text; }

        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
        private void Guide_Click(object sender, RoutedEventArgs e) => new GuideWindow(App.State) { Owner = _win }.Show();
    }
}
