namespace CoverArtSaver.Core;

/// <summary>Well-known locations shared by the Playnite add-on and the screensaver.</summary>
public static class AppPaths
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataFolder { get; } = Path.Combine(LocalAppData, "PCGameCoverArt");

    /// <summary>Where data lived before the project was renamed from "Playnite Coverflow".</summary>
    public static string OldDataFolder { get; } = Path.Combine(LocalAppData, "PlayniteCoverflow");

    private static readonly string[] MigratedFiles = ["settings.json", "library.json", "cover-sizes.json"];

    /// <summary>Written by the Playnite add-on (CoverArtExporter).</summary>
    public static string DefaultLibraryFile => Path.Combine(DataFolder, "library.json");

    /// <summary>Written by the screensaver's settings dialog.</summary>
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    /// <summary>Cached pixel sizes of cover images, for the "vertical covers only" filter.</summary>
    public static string CoverSizesFile => Path.Combine(DataFolder, "cover-sizes.json");

    /// <summary>Simple error log, handy because a screensaver has no console to print to.</summary>
    public static string LogFile => Path.Combine(DataFolder, "screensaver.log");

    /// <summary>
    /// Carries data over from the old folder after the rename: settings, plus the last library export so the
    /// screensaver keeps working until the Playnite add-on is updated. Only copies files the new folder doesn't
    /// have yet, and leaves the old folder alone. Never throws.
    /// </summary>
    public static void MigrateOldDataFolder() => MigrateOldDataFolder(OldDataFolder, DataFolder);

    internal static void MigrateOldDataFolder(string oldFolder, string newFolder)
    {
        try
        {
            if (!Directory.Exists(oldFolder))
            {
                return;
            }

            foreach (var name in MigratedFiles)
            {
                var source = Path.Combine(oldFolder, name);
                var target = Path.Combine(newFolder, name);
                if (File.Exists(source) && !File.Exists(target))
                {
                    Directory.CreateDirectory(newFolder);
                    File.Copy(source, target);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing over: the screensaver just starts with default settings.
        }
    }
}
