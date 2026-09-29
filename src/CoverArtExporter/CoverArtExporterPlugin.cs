using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoverArtExporter
{
    /// <summary>
    /// A tiny Playnite "generic plugin" whose only job is to keep a JSON snapshot of your
    /// library up to date in a well-known folder, so the screensaver never has to touch
    /// Playnite's own database (which Playnite keeps locked while it runs).
    /// </summary>
    public class CoverArtExporterPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        // Must match the GUID in extension.yaml's Id. Never change it after release.
        public override Guid Id { get; } = Guid.Parse("68c44f4f-7701-4ea9-9cac-23b29fb1431d");

        /// <summary>%LOCALAPPDATA%\PCGameCoverArt — shared with the screensaver.</summary>
        public static string ExportFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCGameCoverArt");

        public static string ExportFile => Path.Combine(ExportFolder, "library.json");

        // Library edits arrive as bursts of events (e.g. a metadata download touches every game).
        // A debounce timer collapses a burst into a single export a few seconds after it settles.
        private readonly Timer debounceTimer;
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(5);
        private readonly object exportLock = new object();

        public CoverArtExporterPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
            debounceTimer = new Timer(_ => SafeExport(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemCollectionChanged += OnGamesCollectionChanged;
            PlayniteApi.Database.Games.ItemUpdated += OnGamesUpdated;
            ScheduleExport();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemCollectionChanged -= OnGamesCollectionChanged;
            PlayniteApi.Database.Games.ItemUpdated -= OnGamesUpdated;
            debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            SafeExport(); // final snapshot on the way out
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) => ScheduleExport();

        private void OnGamesCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e) => ScheduleExport();

        private void OnGamesUpdated(object sender, ItemUpdatedEventArgs<Game> e) => ScheduleExport();

        private void ScheduleExport() => debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            // "@" puts the section under Playnite's Extensions menu.
            yield return new MainMenuItem
            {
                MenuSection = "@PC Game Cover Art Screensaver",
                Description = "Export library for screensaver now",
                Action = _ =>
                {
                    var count = SafeExport();
                    PlayniteApi.Dialogs.ShowMessage(
                        count >= 0
                            ? $"Exported {count} games to:\n{ExportFile}"
                            : "Export failed. See Playnite's extensions.log for details.",
                        "PC Game Cover Art Screensaver");
                }
            };
            yield return new MainMenuItem
            {
                MenuSection = "@PC Game Cover Art Screensaver",
                Description = "Open export folder",
                Action = _ =>
                {
                    Directory.CreateDirectory(ExportFolder);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{ExportFolder}\"");
                }
            };
        }

        /// <summary>Exports and returns the number of games, or -1 on failure. Never throws.</summary>
        private int SafeExport()
        {
            try
            {
                lock (exportLock)
                {
                    return Export();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Coverflow export failed");
                return -1;
            }
        }

        private int Export()
        {
            // Read the database on Playnite's UI thread; that's where Playnite expects access to happen.
            var dispatcher = PlayniteApi.MainView.UIDispatcher;
            List<ExportedGame> games = dispatcher.CheckAccess()
                ? BuildSnapshot()
                : dispatcher.Invoke(new Func<List<ExportedGame>>(BuildSnapshot));

            var library = new ExportedLibrary
            {
                ExportedAt = DateTime.UtcNow,
                ExporterVersion = GetType().Assembly.GetName().Version.ToString(),
                Games = games
            };

            Directory.CreateDirectory(ExportFolder);

            // Write to a temp file, then swap it in. The screensaver can never see a half-written file.
            var tempFile = ExportFile + ".tmp";
            File.WriteAllText(tempFile, Serialization.ToJson(library, formatted: true));
            if (File.Exists(ExportFile))
            {
                File.Replace(tempFile, ExportFile, null);
            }
            else
            {
                File.Move(tempFile, ExportFile);
            }

            logger.Info($"Coverflow: exported {games.Count} games to {ExportFile}");
            return games.Count;
        }

        private List<ExportedGame> BuildSnapshot()
        {
            var db = PlayniteApi.Database;
            return db.Games.Select(g => new ExportedGame
            {
                Id = g.Id.ToString(),
                Name = g.Name,
                SortingName = string.IsNullOrWhiteSpace(g.SortingName) ? g.Name : g.SortingName,
                CoverPath = ResolveCoverPath(db, g.CoverImage),
                Hidden = g.Hidden,
                Favorite = g.Favorite,
                IsInstalled = g.IsInstalled,
                Added = g.Added,
                LastActivity = g.LastActivity,
                ReleaseYear = g.ReleaseYear,
                Source = g.Source?.Name,
                Platforms = Names(g.Platforms?.Select(p => p.Name)),
                Genres = Names(g.Genres?.Select(x => x.Name)),
                Tags = Names(g.Tags?.Select(x => x.Name)),
                Features = Names(g.Features?.Select(x => x.Name)),
                Categories = Names(g.Categories?.Select(x => x.Name)),
                AgeRatings = Names(g.AgeRatings?.Select(x => x.Name)),
                PlaytimeSeconds = (long)g.Playtime,
                CommunityScore = g.CommunityScore,
                CriticScore = g.CriticScore,
            }).ToList();
        }

        private static List<string> Names(IEnumerable<string> names) =>
            names?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();

        /// <summary>
        /// Game.CoverImage is usually a database file id like "{gameId}\\abc.jpg" which
        /// GetFullFilePath turns into an absolute path. It can also be a full path or a URL
        /// (not yet downloaded). The screensaver works offline, so URLs are skipped.
        /// </summary>
        private static string ResolveCoverPath(IGameDatabaseAPI db, string coverImage)
        {
            if (string.IsNullOrWhiteSpace(coverImage))
            {
                return null;
            }

            if (coverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.IsPathRooted(coverImage) ? coverImage : db.GetFullFilePath(coverImage);
        }
    }
}
