using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class ProfileRow
    {
        public Profile Profile { get; set; }
        public string Name => Profile.IsExample ? Profile.Id : Profile.Name;
        public double Opacity => Profile.IsExample ? 0.45 : 1.0;
    }

    public partial class ProfilesPage : UserControl
    {
        private readonly MainWindow _win;
        private readonly List<ProfileRow> _rows = new List<ProfileRow>();
        private readonly (string label, WindowMode mode)[] _modes =
        {
            ("Fullscreen", WindowMode.Fullscreen), ("Windowed", WindowMode.Windowed), ("Borderless windowed", WindowMode.Borderless),
            ("Fullscreen windowed", WindowMode.FullscreenWindowed), ("As the game remembers", WindowMode.Registry)
        };
        private Profile Current => (List.SelectedItem as ProfileRow)?.Profile;

        public ProfilesPage(MainWindow win, string selectId = null)
        {
            InitializeComponent();
            _win = win;
            foreach (var m in _modes) ModeBox.Items.Add(m.label);
            Reload(selectId);
        }

        private void Reload(string selectId)
        {
            _rows.Clear();
            foreach (var p in App.State.Profiles.LoadAll()) _rows.Add(new ProfileRow { Profile = p });
            List.ItemsSource = null;
            List.ItemsSource = _rows;
            var sel = _rows.FirstOrDefault(r => string.Equals(r.Profile.Id, selectId, StringComparison.OrdinalIgnoreCase)) ?? _rows.FirstOrDefault(r => !r.Profile.IsExample) ?? _rows.FirstOrDefault();
            List.SelectedItem = sel;
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => Show(Current);

        private void Show(Profile p)
        {
            var enabled = p != null;
            Form.IsEnabled = enabled;
            Note.Text = "";
            if (!enabled) return;
            NameBox.Text = p.Name;
            ServerBox.Text = p.Command.Server;
            var cred = App.State.Credentials.Get(p.Id);
            UserBox.Text = cred?.User ?? p.Command.User;
            PassBox.Password = cred?.Password ?? p.Command.Password;
            BootBox.Text = p.BootFile;
            ExtraBox.Text = p.Command.Extra + (p.Command.Hairpin ? (p.Command.Extra.Length > 0 ? " " : "") + "--hairpin" : "");
            ModeBox.SelectedIndex = Array.FindIndex(_modes, m => m.mode == p.Mode);
            WidthBox.Text = p.Width > 0 ? p.Width.ToString() : ""; HeightBox.Text = p.Height > 0 ? p.Height.ToString() : "";
            MenuWBox.Text = p.MenuWidth > 0 ? p.MenuWidth.ToString() : ""; MenuHBox.Text = p.MenuHeight > 0 ? p.MenuHeight.ToString() : "";
            AutoCloseBox.IsChecked = p.AutoClose;
            if (p.IsExample) Note.Text = "A shipped example. Duplicate it, then edit the copy.";
            else if (!string.IsNullOrEmpty(p.BootFile))
            {
                var bf = Path.IsPathRooted(p.BootFile) ? p.BootFile : Path.Combine(App.State.AshitaRoot, p.BootFile);
                if (!File.Exists(bf)) Note.Text = "Boot file not found. Run Setup or browse to xiloader.";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var p = Current;
            if (p == null) return;
            if (p.IsExample) { Note.Text = "Duplicate this example first."; return; }
            p.Name = NameBox.Text.Trim();
            var extra = LoaderCommand.Parse(ExtraBox.Text);
            p.Command = new LoaderCommand { Server = ServerBox.Text.Trim(), Hairpin = extra.Hairpin, Extra = extra.Extra };
            p.BootFile = BootBox.Text.Trim();
            p.Mode = _modes[Math.Max(0, ModeBox.SelectedIndex)].mode;
            int w, h, mw, mh;
            p.Width = int.TryParse(WidthBox.Text, out w) ? w : -1; p.Height = int.TryParse(HeightBox.Text, out h) ? h : -1;
            p.MenuWidth = int.TryParse(MenuWBox.Text, out mw) ? mw : (p.Width > 0 ? p.Width : -1);
            p.MenuHeight = int.TryParse(MenuHBox.Text, out mh) ? mh : (p.Height > 0 ? p.Height : -1);
            p.AutoClose = AutoCloseBox.IsChecked == true;
            p.Save();
            App.State.Credentials.Set(p.Id, UserBox.Text.Trim(), PassBox.Password);
            App.State.Settings.LastProfile = p.Id;
            App.State.Settings.Save();
            Reload(p.Id);
            Note.Text = "Saved.";
            App.State.Notify();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            var template = App.State.Profiles.TemplatePath();
            if (template == null) { Note.Text = "No profile to copy from; run Setup first."; return; }
            CreateFrom(template, "New profile");
        }

        private void Retail_Click(object sender, RoutedEventArgs e)
        {
            var retail = App.State.Profiles.PathFor("example-retail");
            if (!File.Exists(retail)) { Note.Text = "Ashita's retail example is missing; run Repair."; return; }
            CreateFrom(retail, "New retail profile", retail: true);
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (Current == null) return;
            CreateFrom(Current.Path, "Copy profile");
        }

        private void CreateFrom(string sourcePath, string title, bool retail = false)
        {
            var id = Prompt.Ask(_win, title, "Profile name:", "");
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            if (!ProfileStore.IsValidId(id)) { Note.Text = "That name has characters a file name can't take."; return; }
            var store = App.State.Profiles;
            if (store.Exists(id)) { Note.Text = id + " already exists."; return; }
            var copy = Profile.Load(sourcePath).DuplicateTo(store.PathFor(id), id);
            if (retail)
            {
                // Ashita's own PlayOnline bootloader, retail's quick-play command, the shared startup script
                copy.BootFile = @".\bootloader\pol.exe";
                copy.Command = new LoaderCommand { Extra = "/game eAZcFcB" };
                copy.Script = "vanadreams.txt";
                copy.Save();
            }
            else if (Path.GetFileName(sourcePath).StartsWith("example", StringComparison.OrdinalIgnoreCase))
            {
                copy.Command = new LoaderCommand { Server = "" };
                copy.BootFile = Path.Combine(App.State.AshitaRoot, "bootloader", "xiloader.exe");
                copy.Script = "vanadreams.txt";
                copy.Save();
            }
            Reload(id);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var p = Current;
            if (p == null) return;
            if (p.IsExample) { Note.Text = "The shipped examples stay."; return; }
            if (MessageBox.Show("Send " + p.FileName + " to the Recycle Bin?", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            App.State.Credentials.Remove(p.Id);
            Reload(null);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*", InitialDirectory = Path.Combine(App.State.AshitaRoot, "bootloader") };
            if (d.ShowDialog() == true) BootBox.Text = d.FileName;
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _win.Navigate(new MenuPage(_win));
    }
}
