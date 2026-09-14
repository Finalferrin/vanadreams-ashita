using System;
using System.Linq;
using System.Windows;
using Vanadreams.Services;

namespace Vanadreams
{
    public partial class App : Application
    {
        public static AppState State { get; private set; }
        public static string SnapshotPath { get; private set; }
        public static string SnapshotPage { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // test hook: VanadreamsLauncher.exe --snapshot <png> [page] renders the window to a file and exits
            var args = e.Args;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--snapshot" && i + 1 < args.Length) { SnapshotPath = args[i + 1]; if (i + 2 < args.Length) SnapshotPage = args[i + 2]; }
                if (args[i] == "--ashita" && i + 1 < args.Length) { State = State ?? new AppState(); State.Settings.AshitaRoot = args[i + 1]; }
            }
            State = State ?? new AppState();
            Log.Info("Launcher " + typeof(App).Assembly.GetName().Version + " starting from " + SelfInstall.CurrentExe);

            // Installing is asked for, never automatic: VanadreamsLauncher.exe --install
            var wantsInstall = Array.IndexOf(args, "--install") >= 0;
            if (wantsInstall && string.IsNullOrEmpty(SnapshotPath))
            {
                var answer = MessageBox.Show(
                    "Install the Vanadreams Launcher?\n\nIt copies itself to your apps folder and adds Start menu and desktop shortcuts. Nothing else changes. Choose No to run it from here just this once.",
                    "Vanadreams Launcher", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) { Shutdown(); return; }
                if (answer == MessageBoxResult.Yes)
                {
                    try { SelfInstall.Install(); SelfInstall.HandOver(); Shutdown(); return; }
                    catch (Exception ex) { Log.Error("self-install", ex); MessageBox.Show("Couldn't install: " + ex.Message + "\n\nRunning from here instead.", "Vanadreams Launcher"); }
                }
            }
            DispatcherUnhandledException += (s, ex) =>
            {
                Log.Error("unhandled", ex.Exception);
                MessageBox.Show(ex.Exception.Message, "Vanadreams Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
            if (string.IsNullOrEmpty(SnapshotPath) && State.Settings.MusicOn) Music.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Music.Stop();
            base.OnExit(e);
        }
    }
}
