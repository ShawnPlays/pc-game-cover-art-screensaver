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
}

/// <summary>
/// Windows launches a screensaver (.scr) with one of these command lines:
///   /s            run full screen
///   /p 1234       preview inside window handle 1234   (also seen as /p:1234)
///   /c:1234       show settings, owned by window 1234 (also /c 1234, or just /c)
///   (nothing)     show settings (e.g. double-clicking the .scr / choosing "Configure")
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
