using System.Windows.Threading;

namespace CoverArtSaver.Windows;

/// <summary>
/// When covers can be clicked, moving the mouse shows the pointer instead of closing the screensaver. Shared by
/// every monitor's window so the pointer stays visible as it crosses between them, and hides again once the mouse
/// has been still for a while.
/// </summary>
internal sealed class PointerWake
{
    private static readonly TimeSpan HideAfter = TimeSpan.FromSeconds(4);

    private readonly DispatcherTimer idle;

    /// <summary>Raised when the pointer appears or hides.</summary>
    public event Action? Changed;

    public bool IsAwake { get; private set; }

    public PointerWake()
    {
        idle = new DispatcherTimer { Interval = HideAfter };
        idle.Tick += (_, _) => Sleep();
    }

    /// <summary>The mouse moved: show the pointer (if hidden) and restart the hide countdown.</summary>
    public void Poke()
    {
        idle.Stop();
        idle.Start();
        if (!IsAwake)
        {
            IsAwake = true;
            Changed?.Invoke();
        }
    }

    public void Stop() => idle.Stop();

    private void Sleep()
    {
        idle.Stop();
        IsAwake = false;
        Changed?.Invoke();
    }
}
