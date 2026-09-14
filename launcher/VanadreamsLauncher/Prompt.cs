using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Vanadreams
{
    /// <summary>A one-line question in the launcher's own style.</summary>
    public static class Prompt
    {
        public static string Ask(Window owner, string title, string label, string initial)
        {
            var win = new Window
            {
                Title = title, Owner = owner, Width = 420, Height = 190, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Night"),
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Body")
            };
            var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 8, 0, 16) };
            var ok = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("GoldButton"), IsDefault = true, MinWidth = 90 };
            var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("GhostButton"), IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            string result = null;
            ok.Click += (s, e) => { result = box.Text; win.DialogResult = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = label });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            var border = new Border { Style = (Style)Application.Current.FindResource("Box"), Margin = new Thickness(12), Child = panel };
            win.Content = border;
            win.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            box.KeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };
            return win.ShowDialog() == true ? result : null;
        }
    }
}
