using System;
using System.Collections.Generic;

namespace CoverArtExporter
{
    /// <summary>
    /// The JSON file the screensaver reads. This is the "contract" between the two halves of
    /// the project, so bump <see cref="SchemaVersion"/> if you ever make a breaking change.
    /// The screensaver has a matching copy of these classes in CoverArtSaver.Core/LibraryData.cs.
    /// </summary>
    public class ExportedLibrary
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTime ExportedAt { get; set; }
        public string ExporterVersion { get; set; }
        public List<ExportedGame> Games { get; set; } = new List<ExportedGame>();
    }

    public class ExportedGame
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SortingName { get; set; }

        /// <summary>Absolute path to the cover image on disk, or null if the game has none.</summary>
        public string CoverPath { get; set; }

        public bool Hidden { get; set; }
        public bool Favorite { get; set; }
        public bool IsInstalled { get; set; }
        public DateTime? Added { get; set; }
        public DateTime? LastActivity { get; set; }
        public int? ReleaseYear { get; set; }
        public string Source { get; set; }

        public List<string> Platforms { get; set; } = new List<string>();
        public List<string> Genres { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> Features { get; set; } = new List<string>();
        public List<string> Categories { get; set; } = new List<string>();

        /// <summary>E.g. "ESRB M", "PEGI 18". Used by the content filter.</summary>
        public List<string> AgeRatings { get; set; } = new List<string>();
    }
}
