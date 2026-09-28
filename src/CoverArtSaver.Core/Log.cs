namespace CoverArtSaver.Core;

/// <summary>Minimal file logger. Screensavers run without a console, so this is how you debug them.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > 1_000_000)
                {
                    file.Delete(); // keep it small; this is a screensaver, not a server
                }

                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{(ex != null ? Environment.NewLine + ex : "")}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the screensaver.
        }
    }
}
