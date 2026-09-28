using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CoverArtSaver.Core;
using CoverArtSaver.Windows;

namespace CoverArtSaver;

/// <summary>
/// Entry point. A Windows screensaver is just an .exe renamed to .scr; Windows tells it
/// what to do through command-line arguments (see <see cref="ScreensaverArgs"/>).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        AppPaths.MigrateOldDataFolder(); // before anything reads settings or writes the log
        var args = ScreensaverArgs.Parse(e.Args);
        var settings = SettingsStore.Load();
        Log.Info($"Starting: mode={args.Mode} handle={args.WindowHandle} args='{string.Join(' ', e.Args)}'");

        switch (args.Mode)
        {
            case SaverMode.Fullscreen:
                ScreensaverSession.Start(settings, windowed: false, onExit: Shutdown);
                break;

            case SaverMode.Windowed:
                ScreensaverSession.Start(settings, windowed: true, onExit: Shutdown);
                break;

            case SaverMode.Preview:
                PreviewHost.Start(args.WindowHandle, settings, onExit: Shutdown);
                break;

            default:
                var window = new SettingsWindow(settings);
                if (args.WindowHandle != 0)
                {
                    // Make the dialog modal-ish to the Windows Screen Saver Settings dialog.
                    new WindowInteropHelper(window).Owner = args.WindowHandle;
                }

                window.Closed += (_, _) => Shutdown();
                window.Show();
                break;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A crashing screensaver can leave a frozen black screen, so log and bail out cleanly.
        Log.Error("Unhandled exception", e.Exception);
        e.Handled = true;
        Shutdown(1);
    }
}
