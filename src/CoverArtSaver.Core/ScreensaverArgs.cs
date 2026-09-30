using System.Globalization;

namespace CoverArtSaver.Core;

public enum SaverMode
{
    /// <summary>No args, or /c — show the settings dialog.</summary>
    Configure,
    /// <summary>/p &lt;hwnd&gt; — draw inside the little monitor in Windows' Screen Saver Settings.</summary>
    Preview,
    /// <summary>/s — run full screen for real.</summary>
    Fullscreen,
    /// <summary>/w — developer mode: runs in a normal resizable window that doesn't exit on mouse moves.</summary>
    Windowed,
    /// <summary>/update — no window: install a newer version if there is one (run by the daily scheduled task).</summary>
    Update,
    /// <summary>/autoupdate on — register the daily update task (needs administrator rights).</summary>
    EnableAutoUpdate,
    /// <summary>/autoupdate off — remove the daily update task (needs administrator rights).</summary>
    DisableAutoUpdate,
}

/// <summary>
/// Windows launches a screensaver (.scr) with one of these command lines:
///   /s            run full screen
///   /p 1234       preview inside window handle 1234   (also seen as /p:1234)
///   /c:1234       show settings, owned by window 1234 (also /c 1234, or just /c)
///   (nothing)     show settings (e.g. double-clicking the .scr / choosing "Configure")
/// plus this app's own: /w (windowed), /update and /autoupdate on|off (see <see cref="AutoUpdater"/>).
/// Case and "-" vs "/" both vary in the wild, so parse loosely.
/// </summary>
public sealed record ScreensaverArgs(SaverMode Mode, nint WindowHandle)
{
    public static ScreensaverArgs Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new(SaverMode.Configure, 0);
        }

        var first = args[0].Trim().TrimStart('/', '-').ToLowerInvariant();
        if (first.Length == 0)
        {
            return new(SaverMode.Configure, 0);
        }

        // This app's own options (Windows never sends these).
        if (first == "update")
        {
            return new(SaverMode.Update, 0);
        }

        if (first == "autoupdate")
        {
            var on = args.Count > 1 && args[1].Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
            return new(on ? SaverMode.EnableAutoUpdate : SaverMode.DisableAutoUpdate, 0);
        }

        // The handle may be glued on with ':' (/c:1234) or be the next argument (/p 1234).
        string? handleText = null;
        var colon = first.IndexOf(':');
        if (colon >= 0)
        {
            handleText = first[(colon + 1)..];
        }
        else if (args.Count > 1)
        {
            handleText = args[1];
        }

        var handle = ParseHandle(handleText);

        return first[0] switch
        {
            's' => new(SaverMode.Fullscreen, 0),
            'p' or 'l' when handle != 0 => new(SaverMode.Preview, handle),
            'p' or 'l' => new(SaverMode.Configure, 0), // preview without a handle is meaningless
            'w' => new(SaverMode.Windowed, 0),
            _ => new(SaverMode.Configure, handle),
        };
    }

    private static nint ParseHandle(string? text) =>
        long.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (nint)value : 0;
}
