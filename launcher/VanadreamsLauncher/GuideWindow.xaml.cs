using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vanadreams.Pages;
using Vanadreams.Services;

namespace Vanadreams
{
    public sealed class GuideStep : INotifyPropertyChanged
    {
        private bool _selected;
        private string _mark = "";
        private string _markBrush = "Mist";
        public string Key { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Link { get; set; }
        public string LinkLabel { get; set; }
        public string Picture { get; set; }
        public string PictureNote { get; set; }
        public bool HasCheck { get; set; }
        public Func<AppState, Tuple<bool?, string>> Check { get; set; }
        public bool Selected { get { return _selected; } set { _selected = value; Raise("HandVisibility"); } }
        public Visibility HandVisibility => Selected ? Visibility.Visible : Visibility.Hidden;
        public string Mark { get { return _mark; } set { _mark = value; Raise("Mark"); } }
        public string MarkBrushKey { get { return _markBrush; } set { _markBrush = value; Raise("MarkBrush"); } }
        public Brush MarkBrush => (Brush)Application.Current.FindResource(MarkBrushKey);
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class GuideWindow : Window
    {
        private readonly AppState _state;
        private readonly List<GuideStep> _steps;
        private GuideStep _current;

        public GuideWindow(AppState state)
        {
            InitializeComponent();
            _state = state;
            _steps = BuildSteps();
            Steps.ItemsSource = _steps;
            foreach (var s in _steps) if (state.Settings.GuideDone.Contains(s.Key)) { s.Mark = "✓"; s.MarkBrushKey = "Ok"; }
            Show(_steps[0]);
        }

        private List<GuideStep> BuildSteps() => new List<GuideStep>
        {
            new GuideStep
            {
                Key = "client", Title = "1 · Get the client",
                Body = "Final Fantasy XI's client is a free download from Square Enix. You need it installed before anything else works; the launcher cannot install it for you.\n\nThe download is a few gigabytes and the installer is PlayOnline's, from 2002. Let it run. Nothing you type into PlayOnline matters for Vanadreams: no account, no login, ever.",
                Link = "https://www.playonline.com/ff11us/download/media/install_win.html", LinkLabel = "Square Enix download page", Picture = "guide-1-download.png",
                PictureNote = "Square Enix's download page. Pick the Windows installer.",
            },
            new GuideStep
            {
                Key = "folder", Title = "2 · Install to a folder you own",
                Body = "When the installer asks where to put PlayOnline, choose a folder outside Program Files, such as C:\\Games\\PlayOnline.\n\nIt works in Program Files too, but every update and file check then asks for administrator permission, and the launcher's version check has to read files it may not be allowed to. A folder you own has neither problem.",
                Picture = "guide-2-folder.png", PictureNote = "The installer's folder prompt. Change it to C:\\Games\\PlayOnline.",
                HasCheck = true,
                Check = st =>
                {
                    var f = st.FfxiFolder;
                    if (f == null) return Tuple.Create<bool?, string>(false, "No FFXI install is registered yet. Finish the installer, then check again.");
                    if (ClientVersion.IsUnderProgramFiles(f)) return Tuple.Create<bool?, string>((bool?)null, "Installed at " + f + ". It works, but updates will prompt for administrator permission.");
                    return Tuple.Create<bool?, string>(true, "Installed at " + f + ".");
                },
            },
            new GuideStep
            {
                Key = "update", Title = "3 · Update it once",
                Body = "Open PlayOnline Viewer from the Start menu. It updates itself first, then shows the FINAL FANTASY XI entry. Choose it and let it download the game update; this is the long part, often an hour.\n\nDo not log in. Close the viewer when the update finishes. Then press Check: the launcher reads the version your client will report and compares it with what Vanadreams expects.",
                Picture = "guide-3-update.png", PictureNote = "PlayOnline Viewer on the FINAL FANTASY XI page with the update prompt.",
                HasCheck = true,
                Check = st =>
                {
                    var v = st.CheckVersion();
                    if (v.Verdict == VersionVerdict.Ready) return Tuple.Create<bool?, string>(true, v.Sentence);
                    if (v.Verdict == VersionVerdict.Unknown) return Tuple.Create<bool?, string>(false, v.Sentence);
                    return Tuple.Create<bool?, string>(false, v.Sentence);
                },
            },
            new GuideStep
            {
                Key = "firewall", Title = "4 · Let it through the firewall",
                Body = "The first time the loader and the game start, Windows asks whether to allow them through the firewall. Tick Private networks and press Allow access.\n\nIf that prompt was dismissed, the game may sit at 'Searching for lobby server'. Press Check: the launcher looks for the allow rules and can add them for you, which asks for administrator permission once.",
                Picture = "guide-4-firewall.png", PictureNote = "The Windows Security Alert. Tick Private networks, then Allow access.",
                HasCheck = true,
                Check = st =>
                {
                    if (!st.HasAshita) return Tuple.Create<bool?, string>(false, "Run Setup first so the loader exists to check.");
                    var exes = new List<string> { Path.Combine(st.AshitaRoot, "bootloader", "xiloader.exe") };
                    var ffxi = st.FfxiFolder;
                    var pol = ffxi != null ? Path.Combine(Path.GetDirectoryName(ffxi.TrimEnd('\\')), "PlayOnlineViewer", "pol.exe") : null;
                    if (pol != null && File.Exists(pol)) exes.Add(pol);
                    var result = Firewall.Check(exes);
                    var missing = result.Where(kv => !kv.Value).Select(kv => Path.GetFileName(kv.Key)).ToList();
                    if (missing.Count == 0) return Tuple.Create<bool?, string>(true, "Allow rules found for " + string.Join(" and ", result.Keys.Select(Path.GetFileName)) + ".");
                    return Tuple.Create<bool?, string>(false, "No allow rule for " + string.Join(", ", missing) + ". Press Check again to add them; Windows will ask for permission once.|ADD");
                },
            },
            new GuideStep
            {
                Key = "account", Title = "5 · Make your account",
                Body = "Press Play in the launcher. The loader's black window appears with a menu: choose Create New Account, pick a username and a password. This is your Vanadreams account, nothing to do with PlayOnline or Square Enix.\n\nAfter that, the launcher can remember the login for you on the Profiles page, and Play goes straight to character select.",
                Picture = "guide-5-account.png", PictureNote = "The loader's menu. Create New Account is option 2.",
            },
            new GuideStep
            {
                Key = "play", Title = "6 · Play",
                Body = "That's the whole setup. Pick who you are on the menu and press Play. The bottom strip always shows whether the server is up and whether your client still matches it.\n\nIf anything above turned amber or red, the Discord is where someone can look at it with you.",
                Picture = "guide-6-play.png", PictureNote = "Character select on Vanadreams.",
            },
        };

        private void Show(GuideStep step)
        {
            foreach (var s in _steps) s.Selected = false;
            step.Selected = true;
            _current = step;
            StepEyebrow.Text = "✦ Step " + (_steps.IndexOf(step) + 1) + " of " + _steps.Count;
            StepTitle.Text = step.Title.Substring(step.Title.IndexOf('·') + 2);
            StepBody.Text = step.Body;
            CheckResult.Text = "";
            LinkButton.Visibility = string.IsNullOrEmpty(step.Link) ? Visibility.Collapsed : Visibility.Visible;
            LinkButton.Content = step.LinkLabel ?? "Open the link";
            CheckButton.Visibility = step.HasCheck ? Visibility.Visible : Visibility.Collapsed;
            DoneButton.Visibility = step.HasCheck ? Visibility.Collapsed : Visibility.Visible;
            NextButton.Content = _steps.IndexOf(step) == _steps.Count - 1 ? "Close" : "Next ▸";
            Picture.Source = null;
            PictureNote.Text = step.PictureNote;
            try
            {
                var uri = new Uri("pack://application:,,,/assets/" + step.Picture);
                var info = Application.GetResourceStream(uri);
                if (info != null) { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.StreamSource = info.Stream; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); Picture.Source = bmp; PictureNote.Text = ""; }
            }
            catch (Exception) { /* no picture yet: the note stands in */ }
        }

        private void Step_Click(object sender, MouseButtonEventArgs e)
        {
            var s = ((FrameworkElement)sender).Tag as GuideStep;
            if (s != null) Show(s);
        }

        private void Link_Click(object sender, RoutedEventArgs e) { if (_current?.Link != null) MenuPage.OpenUrl(_current.Link); }

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            if (_current?.Check == null) return;
            var r = _current.Check(_state);
            var text = r.Item2;
            if (text.EndsWith("|ADD"))
            {
                text = text.Substring(0, text.Length - 4);
                if (CheckResult.Tag as string == "offered")
                {
                    var exes = new List<string> { Path.Combine(_state.AshitaRoot, "bootloader", "xiloader.exe") };
                    var ffxi = _state.FfxiFolder;
                    var pol = ffxi != null ? Path.Combine(Path.GetDirectoryName(ffxi.TrimEnd('\\')), "PlayOnlineViewer", "pol.exe") : null;
                    if (pol != null && File.Exists(pol)) exes.Add(pol);
                    var ok = Firewall.AddAllowRules(exes);
                    r = _current.Check(_state);
                    text = ok ? r.Item2.Replace("|ADD", "") : "Permission was refused, so nothing was added.";
                    CheckResult.Tag = null;
                }
                else CheckResult.Tag = "offered";
            }
            else CheckResult.Tag = null;
            CheckResult.Text = text;
            CheckResult.Foreground = (Brush)FindResource(r.Item1 == true ? "Ok" : r.Item1 == null ? "Warn" : "Bad");
            _current.Mark = r.Item1 == true ? "✓" : r.Item1 == null ? "!" : "✗";
            _current.MarkBrushKey = r.Item1 == true ? "Ok" : r.Item1 == null ? "Warn" : "Bad";
            if (r.Item1 != false) Remember(_current.Key);
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            _current.Mark = "✓"; _current.MarkBrushKey = "Ok";
            CheckResult.Text = "Taken on trust.";
            CheckResult.Foreground = (Brush)FindResource("Mist");
            Remember(_current.Key);
        }

        private void Remember(string key)
        {
            if (!_state.Settings.GuideDone.Contains(key)) { _state.Settings.GuideDone.Add(key); _state.Settings.Save(); }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            var i = _steps.IndexOf(_current);
            if (i >= _steps.Count - 1) Close(); else Show(_steps[i + 1]);
        }

        private void Wrong_Click(object sender, RoutedEventArgs e) => MenuPage.OpenUrl("https://fairywitch.ca/");
    }
}
