using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CoverArtSaver.Core;
using CoverArtSaver.Interop;
using CoverArtSaver.Rendering;

namespace CoverArtSaver.Windows;

/// <summary>
/// A borderless, topmost window that exactly covers one monitor and exits on user input. When covers can be
/// clicked (<see cref="SaverSettings.ClickAction"/>), moving the mouse shows the pointer instead of exiting.
/// </summary>
internal sealed class ScreensaverWindow : Window
{
    private readonly NativeMethods.RECT bounds;
    private readonly bool windowed;
    private readonly int mouseThreshold;
    private readonly IGamePicker? picker; // null when covers can't be clicked
    private readonly PointerWake? wake;
    private Point? firstMousePosition;

    public event Action? ExitRequested;

    /// <summary>A cover was clicked.</summary>
    public event Action<GameEntry>? GameChosen;

    public bool IsClosed { get; private set; }

    /// <param name="wake">Shared by all the windows; null when covers can't be clicked.</param>
    public ScreensaverWindow(UIElement content, NativeMethods.RECT bounds, bool windowed, int mouseThreshold, PointerWake? wake)
    {
        this.bounds = bounds;
        this.windowed = windowed;
        this.mouseThreshold = mouseThreshold;
        this.wake = wake;
        picker = wake != null ? content as IGamePicker : null;

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

            if (wake != null)
            {
                wake.Changed += OnWakeChanged;
            }
        }

        PreviewKeyDown += OnKeyDown;
        PreviewMouseDown += OnMouseDown;
        PreviewMouseWheel += (_, _) => { if (!windowed) Exit(); };
        PreviewMouseMove += OnMouseMove;
        Closed += (_, _) =>
        {
            IsClosed = true;
            if (wake != null)
            {
                wake.Changed -= OnWakeChanged;
            }

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

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // A click only opens a game when the pointer is showing; a blind click (pointer hidden) just exits as usual.
        var pointerShowing = windowed || wake?.IsAwake == true;
        if (picker != null && pointerShowing && e.ChangedButton == MouseButton.Left
            && picker.GameAt(e.GetPosition((UIElement)Content)) is { } game)
        {
            e.Handled = true;
            GameChosen?.Invoke(game);
        }
        else if (!windowed)
        {
            Exit();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (windowed)
        {
            UpdateHoverCursor(e);
            return;
        }

        if (wake?.IsAwake == true)
        {
            wake.Poke();
            UpdateHoverCursor(e);
            return;
        }

        // Windows often sends a mouse-move right after the window appears, and optical mice jitter,
        // so remember where the cursor started and only react once it has really moved.
        var position = e.GetPosition(this);
        if (firstMousePosition is not Point start)
        {
            firstMousePosition = position;
            return;
        }

        if ((position - start).Length > mouseThreshold)
        {
            if (wake != null)
            {
                wake.Poke();
                UpdateHoverCursor(e);
            }
            else
            {
                Exit();
            }
        }
    }

    /// <summary>A hand over a cover that can be clicked, the normal arrow elsewhere.</summary>
    private void UpdateHoverCursor(MouseEventArgs e)
    {
        if (picker != null)
        {
            Cursor = picker.GameAt(e.GetPosition((UIElement)Content)) != null ? Cursors.Hand : windowed ? null : Cursors.Arrow;
        }
    }

    private void OnWakeChanged()
    {
        if (wake!.IsAwake)
        {
            Cursor = Cursors.Arrow;
        }
        else
        {
            // Hidden again: the next movement has to pass the threshold again, from wherever the mouse is then.
            Cursor = Cursors.None;
            firstMousePosition = null;
        }
    }

    private void Exit() => ExitRequested?.Invoke();
}
