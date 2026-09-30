namespace CoverArtSaver.Core;

/// <summary>Minimal file logger. Screensavers run without a console, so this is how you debug them.</summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>Log somewhere other than the user's data folder (the updater runs as SYSTEM, which has no such folder).</summary>
    public static string? FileOverride { get; set; }

    private static string LogFile => FileOverride ?? AppPaths.LogFile;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
                var file = new FileInfo(LogFile);
                if (file.Exists && file.Length > 1_000_000)
                {
                    file.Delete(); // keep it small; this is a screensaver, not a server
                }

                File.AppendAllText(LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{(ex != null ? Environment.NewLine + ex : "")}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the screensaver.
        }
    }
}
