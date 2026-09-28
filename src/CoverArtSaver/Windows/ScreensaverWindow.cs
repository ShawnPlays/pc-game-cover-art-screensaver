using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CoverArtSaver.Interop;

namespace CoverArtSaver.Windows;

/// <summary>A borderless, topmost window that exactly covers one monitor and exits on user input.</summary>
internal sealed class ScreensaverWindow : Window
{
    private readonly NativeMethods.RECT bounds;
    private readonly bool windowed;
    private readonly int mouseThreshold;
    private Point? firstMousePosition;

    public event Action? ExitRequested;

    public bool IsClosed { get; private set; }

    public ScreensaverWindow(UIElement content, NativeMethods.RECT bounds, bool windowed, int mouseThreshold)
    {
        this.bounds = bounds;
        this.windowed = windowed;
        this.mouseThreshold = mouseThreshold;

        Content = content;
        Background = Brushes.Black;

        if (windowed)
        {
            // Developer mode (/w): a normal window you can resize, move and close. Esc also closes.
            Title = "PC Game Cover Art (test window)";
            Width = 1280;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = Cursors.None;

            // Position in physical pixels via Win32; WPF's Left/Top are DPI-scaled and awkward across monitors.
            SourceInitialized += (_, _) => CoverMonitor();
            Loaded += (_, _) => CoverMonitor();
            DpiChanged += (_, _) => CoverMonitor();
        }

        PreviewKeyDown += OnKeyDown;
        PreviewMouseDown += (_, _) => { if (!windowed) Exit(); };
        PreviewMouseWheel += (_, _) => { if (!windowed) Exit(); };
        PreviewMouseMove += OnMouseMove;
        Closed += (_, _) =>
        {
            IsClosed = true;
            ExitRequested?.Invoke(); // e.g. Alt+F4 or closing the test window
        };
    }

    private void CoverMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!windowed || e.Key == Key.Escape)
        {
            Exit();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (windowed)
        {
            return;
        }

        // Windows often sends a mouse-move right after the window appears, and optical mice jitter,
        // so remember where the cursor started and only exit once it has really moved.
        var position = e.GetPosition(this);
        if (firstMousePosition is not Point start)
        {
            firstMousePosition = position;
            return;
        }

        if ((position - start).Length > mouseThreshold)
        {
            Exit();
        }
    }

    private void Exit() => ExitRequested?.Invoke();
}
