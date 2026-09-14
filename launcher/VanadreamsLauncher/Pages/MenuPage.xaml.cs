using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vanadreams.Services;

namespace Vanadreams.Pages
{
    public sealed class MenuCommand : INotifyPropertyChanged
    {
        private bool _selected;
        public string Label { get; set; }
        public string Key { get; set; }
        public bool Dim { get; set; }
        public bool Selected { get { return _selected; } set { _selected = value; Raise("Selected"); Raise("HandVisibility"); Raise("Opacity"); } }
        public Visibility HandVisibility => Selected ? Visibility.Visible : Visibility.Hidden;
        public double Opacity => Dim ? 0.45 : Selected ? 1.0 : 0.9;
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class MenuPage : UserControl
    {
        private readonly MainWindow _win;
        private readonly List<MenuCommand> _commands = new List<MenuCommand>
        {
            new MenuCommand { Label = "Play", Key = "play", Selected = true },
            new MenuCommand { Label = "Profiles", Key = "profiles" },
            new MenuCommand { Label = "Addons", Key = "addons" },
            new MenuCommand { Label = "Fishing", Key = "fishing" },
            new MenuCommand { Label = "Capture", Key = "capture" },
            new MenuCommand { Label = "Setup", Key = "setup" },
            new MenuCommand { Label = "Guide", Key = "guide" },
            new MenuCommand { Label = "Settings", Key = "settings" },
            new MenuCommand { Label = "Discord", Key = "discord", Dim = true },
            new MenuCommand { Label = "Exit", Key = "exit", Dim = true },
        };
        private Profile _profile;

        public MenuPage(MainWindow win)
        {
            InitializeComponent();
            _win = win;
            Commands.ItemsSource = _commands;
            KeyDown += OnKeyDown;
            Loaded += (s, e) => { LoadProfile(); Keyboard.Focus(this); };
            App.State.Changed += () => Dispatcher.BeginInvoke(new Action(LoadProfile));
        }

        private void LoadProfile()
        {
            var state = App.State;
            var all = state.Profiles.LoadAll();
            _profile = all.FirstOrDefault(p => string.Equals(p.Id, state.Settings.LastProfile, StringComparison.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(p => !p.IsExample) ?? all.FirstOrDefault();
            Facts.Children.Clear();
            Facts.RowDefinitions.Clear();
            if (_profile == null)
            {
                ProfileName.Text = "No profile yet";
                ProfileServer.Text = "Run Setup to write the Vanadreams profile.";
                PlayButton.IsEnabled = false;
                return;
            }
            PlayButton.IsEnabled = true;
            ProfileName.Text = _profile.Name;
            ProfileServer.Text = (_profile.Command.Server.IndexOf("vanadreams", StringComparison.OrdinalIgnoreCase) >= 0 ? "Vanadreams · " : "") + _profile.Command.Server;
            var v = state.Version;
            var mode = _profile.Mode == WindowMode.Registry ? "As the game remembers" : _profile.Mode.ToString();
            var size = _profile.Width > 0 && _profile.Height > 0 ? $" · {_profile.Width} × {_profile.Height}" : "";
            string lastPlayed;
            state.Settings.LastPlayed.TryGetValue(_profile.Id, out lastPlayed);
            DateTime when;
            var last = !string.IsNullOrEmpty(lastPlayed) && DateTime.TryParse(lastPlayed, null, DateTimeStyles.RoundtripKind, out when)
                ? (when.Date == DateTime.Today ? "Today, " + when.ToString("HH:mm") : when.ToString("d MMM, HH:mm")) : "Not yet";
            var cred = state.Credentials.Get(_profile.Id);
            AddFact("Server", state.Status.StateWord + (string.IsNullOrEmpty(state.Status.CheckedWord) ? "" : " · " + state.Status.CheckedWord), null);
            AddFact("Client",
                !v.ExpectedIsPublished ? (string.IsNullOrEmpty(v.Installed) ? "Version unknown" : v.Installed + " · server hasn't published its version")
                : v.Verdict == VersionVerdict.Ready ? "✓ " + v.Installed + " · matches the server"
                : v.Verdict == VersionVerdict.Unknown ? "Version unknown"
                : "✗ " + v.Installed + " · server expects " + v.Expected,
                !v.ExpectedIsPublished ? "Mist" : v.Verdict == VersionVerdict.Ready ? "Ok" : v.Verdict == VersionVerdict.Unknown ? "Warn" : "Bad");
            AddFact("Ashita", "v4 beta, updated " + state.AshitaUpdated() + " · " + state.AshitaRoot, null);
            AddFact("Loader", "xiloader " + state.LoaderVersion(), null);
            AddFact("Login", cred != null && !string.IsNullOrEmpty(cred.User) ? cred.User + " · remembered" : "Asked at launch", null);
            AddFact("Addons", state.EnabledCount + " enabled" + (state.UpdateCount > 0 ? " · " + state.UpdateCount + " update" + (state.UpdateCount > 1 ? "s" : "") : ""), null);
            AddFact("Window", mode + size, null);
            AddFact("Last played", last, null);
        }

        private void AddFact(string label, string value, string brushKey)
        {
            var row = Facts.RowDefinitions.Count;
            Facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 5) };
            var v = new TextBlock { Text = value, Style = (Style)FindResource("Mono"), Margin = new Thickness(0, 0, 0, 5), TextTrimming = TextTrimming.CharacterEllipsis };
            if (brushKey != null) v.Foreground = (Brush)FindResource(brushKey);
            Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            Facts.Children.Add(l); Facts.Children.Add(v);
        }

        private int SelectedIndex => _commands.FindIndex(c => c.Selected);

        private void Select(int index)
        {
            index = (index + _commands.Count) % _commands.Count;
            foreach (var c in _commands) c.Selected = false;
            _commands[index].Selected = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down) { Select(SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == Key.Up) { Select(SelectedIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Enter || e.Key == Key.Space) { Run(_commands[SelectedIndex].Key); e.Handled = true; }
        }

        private void Command_Hover(object sender, MouseEventArgs e)
        {
            var cmd = ((FrameworkElement)sender).Tag as MenuCommand;
            if (cmd != null) Select(_commands.IndexOf(cmd));
        }

        private void Command_Click(object sender, MouseButtonEventArgs e)
        {
            var cmd = ((FrameworkElement)sender).Tag as MenuCommand;
            if (cmd != null) Run(cmd.Key);
        }

        private void Play_Click(object sender, RoutedEventArgs e) => Run("play");
        private void Edit_Click(object sender, RoutedEventArgs e) => _win.Navigate(new ProfilesPage(_win, _profile?.Id));

        private void Run(string key)
        {
            switch (key)
            {
                case "play": Play(); break;
                case "profiles": _win.Navigate(new ProfilesPage(_win, _profile?.Id)); break;
                case "addons": _win.Navigate(new AddonsPage(_win)); break;
                case "fishing": _win.Navigate(new FishingPage(_win)); break;
                case "capture": _win.Navigate(new CapturePage(_win)); break;
                case "setup": _win.Navigate(new SetupPage(_win)); break;
                case "guide": new GuideWindow(App.State) { Owner = _win }.Show(); break;
                case "settings": _win.Navigate(new SettingsPage(_win)); break;
                case "discord": OpenUrl("https://fairywitch.ca/"); break;
                case "exit": Application.Current.Shutdown(); break;
            }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("open url: " + ex.Message); }
        }

        private void Play()
        {
            if (_profile == null) return;
            var state = App.State;
            state.CheckVersion();
            if (state.Version.BlocksPlay)
            {
                MessageBox.Show(state.Version.Sentence + "\n\nOpen the Guide from the menu for the update steps.", "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                _win.RefreshStrip();
                return;
            }
            try
            {
                GameLauncher.Launch(state.AshitaRoot, _profile, state.Credentials.Get(_profile.Id));
                state.Settings.LastProfile = _profile.Id;
                state.Settings.LastPlayed[_profile.Id] = DateTime.Now.ToString("o");
                state.Settings.Save();
                if (_profile.AutoClose) { var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; t.Tick += (s, e) => Application.Current.Shutdown(); t.Start(); }
                else LoadProfile();
            }
            catch (Exception ex)
            {
                Log.Error("launch failed", ex);
                MessageBox.Show(ex.Message, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
