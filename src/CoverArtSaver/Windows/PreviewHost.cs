using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CoverArtSaver.Core;
using CoverArtSaver.Interop;
using CoverArtSaver.Rendering;

namespace CoverArtSaver.Windows;

/// <summary>
/// Handles "/p &lt;hwnd&gt;": Windows gives us the handle of the little monitor picture in the
/// Screen Saver Settings dialog, and we render into it as a child window.
/// </summary>
internal static class PreviewHost
{
    public static void Start(nint parentHandle, SaverSettings settings, Action onExit)
    {
        NativeMethods.GetClientRect(parentHandle, out var rect);

        var parameters = new HwndSourceParameters("PCGameCoverArtPreview")
        {
            ParentWindow = parentHandle,
            WindowStyle = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN,
            PositionX = 0,
            PositionY = 0,
            Width = rect.Width,
            Height = rect.Height,
        };
        var source = new HwndSource(parameters);

        var library = LibraryLoader.Load(settings);
        var content = library.Problem != null
            ? CoverflowView.CreateMessageView("Game library not found")
            : ScreensaverSession.BuildContent(settings, library, isPrimary: true, 0, 1, Environment.TickCount, Stopwatch.StartNew(), isPreview: true);

        // HwndSource sizes in pixels, WPF content in DIPs: convert so the content fills the preview exactly.
        var toDip = source.CompositionTarget.TransformFromDevice;
        var size = toDip.Transform(new Point(rect.Width, rect.Height));
        source.RootVisual = new Border { Width = size.X, Height = size.Y, Child = (UIElement)content };

        // Windows doesn't tell us when the preview closes; it just destroys the parent window.
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        watchdog.Tick += (_, _) =>
        {
            if (!NativeMethods.IsWindow(parentHandle) || !NativeMethods.IsWindowVisible(parentHandle))
            {
                watchdog.Stop();
                source.Dispose(); // raises Disposed below, which exits
            }
        };
        watchdog.Start();
        source.Disposed += (_, _) => onExit();
    }
}
