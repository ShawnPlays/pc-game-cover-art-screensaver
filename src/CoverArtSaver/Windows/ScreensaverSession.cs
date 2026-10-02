using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoverArtSaver.Audio;
using CoverArtSaver.Core;
using CoverArtSaver.Interop;
using CoverArtSaver.Rendering;

namespace CoverArtSaver.Windows;

/// <summary>Creates one full-screen window per monitor and tears them all down together.</summary>
internal static class ScreensaverSession
{
    public static void Start(SaverSettings settings, bool windowed, Action onExit)
    {
        var library = LibraryLoader.Load(settings);
        if (library.Problem != null)
        {
            Log.Info("Library problem: " + library.Problem);
        }

        var clock = Stopwatch.StartNew();       // shared so mirrored monitors stay in lockstep
        var seed = Environment.TickCount;       // new shuffle every time the screensaver starts
        List<NativeMethods.MonitorInfo> monitors = windowed
            ? [new NativeMethods.MonitorInfo(default, IsPrimary: true)]
            : NativeMethods.GetMonitors();

        var windows = new List<ScreensaverWindow>();
        var wake = settings.ClickAction != GameClickAction.Off ? new PointerWake() : null;
        SoundtrackPlayer? music = null;
        var exiting = false;
        void ExitAll()
        {
            if (exiting)
            {
                return;
            }

            exiting = true;
            wake?.Stop();
            music?.Stop();
            foreach (var w in windows.Where(w => !w.IsClosed))
            {
                w.Close();
            }

            onExit();
        }

        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var content = BuildContent(settings, library, monitor.IsPrimary, i, monitors.Count, seed, clock, isPreview: false);
            var window = new ScreensaverWindow(content, monitor.Bounds, windowed, settings.MouseMoveThreshold, wake);
            window.ExitRequested += ExitAll;
            window.GameChosen += game =>
            {
                ExitAll(); // get out of the way first, so the game or launcher comes up in front
                OpenGame(game, settings.ClickAction);
            };
            windows.Add(window);
        }

        foreach (var window in windows)
        {
            window.Show();
        }

        windows[0].Activate(); // make sure keyboard input reaches us
        music = SoundtrackPlayer.Start(settings); // not in the small preview, which doesn't come through here
    }

    private static void OpenGame(GameEntry game, GameClickAction action)
    {
        var link = GameLinks.For(game, action);
        if (link == null)
        {
            Log.Info($"No {action} link for '{game.Name}' ({game.Id}).");
            return;
        }

        try
        {
            Log.Info($"Opening '{game.Name}': {link}");
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            // Usually Playnite or Steam isn't installed, so nothing handles the link.
            Log.Error($"Could not open {link}", ex);
        }
    }

    internal static UIElement BuildContent(
        SaverSettings settings, LoadedLibrary library, bool isPrimary, int monitorIndex, int monitorCount, int seed, Stopwatch clock, bool isPreview)
    {
        if (library.Problem != null)
        {
            return isPrimary ? CoverflowView.CreateMessageView(library.Problem) : new Grid { Background = Brushes.Black };
        }

        if (settings.MultiMonitor == MultiMonitorMode.PrimaryOnly && !isPrimary)
        {
            return new Grid { Background = Brushes.Black };
        }

        var independent = settings.MultiMonitor == MultiMonitorMode.Independent;
        var monitorSeed = independent ? seed + monitorIndex : seed;

        // Mirrored monitors must change together to stay identical, so only Independent ones can take turns.
        var turn = independent && settings.MonitorsTakeTurns && monitorCount > 1
            ? new MonitorTurn(monitorIndex, monitorCount, seed)
            : MonitorTurn.Solo;
        var games = GameOrdering.Order(library.Games, settings.Order, monitorSeed);

        if (settings.Layout == SaverLayout.Mosaic)
        {
            // For non-random orders, "Independent" starts each monitor one screenful further into the list.
            var startScreen = independent && settings.Order != CoverOrder.Random ? monitorIndex : 0;
            return new MosaicView(games, settings, clock, monitorSeed, startScreen, turn, isPreview);
        }

        // For non-random orders, "Independent" just starts each monitor at a different point in the list.
        var startOffset = independent && settings.Order != CoverOrder.Random ? monitorIndex * 7 : 0;
        // Coverflow slides on a fixed beat, so taking turns means spreading the monitors' beats evenly apart.
        var timeOffset = turn.Index * settings.SecondsPerCover / turn.Count;
        return new CoverflowView(games, settings, clock, startOffset, timeOffset, isPreview);
    }
}
