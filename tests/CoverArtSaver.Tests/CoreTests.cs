using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class ScreensaverArgsTests
{
    [Theory]
    [InlineData(new string[0], SaverMode.Configure, 0L)]
    [InlineData(new[] { "/s" }, SaverMode.Fullscreen, 0L)]
    [InlineData(new[] { "/S" }, SaverMode.Fullscreen, 0L)]
    [InlineData(new[] { "-s" }, SaverMode.Fullscreen, 0L)]
    [InlineData(new[] { "/p", "1234" }, SaverMode.Preview, 1234L)]
    [InlineData(new[] { "/p:1234" }, SaverMode.Preview, 1234L)]
    [InlineData(new[] { "/c:5678" }, SaverMode.Configure, 5678L)]
    [InlineData(new[] { "/c" }, SaverMode.Configure, 0L)]
    [InlineData(new[] { "/p" }, SaverMode.Configure, 0L)]
    [InlineData(new[] { "/w" }, SaverMode.Windowed, 0L)]
    public void ParsesWindowsCommandLines(string[] args, SaverMode mode, long handle)
    {
        var parsed = ScreensaverArgs.Parse(args);
        Assert.Equal(mode, parsed.Mode);
        Assert.Equal((nint)handle, parsed.WindowHandle);
    }
}

public class CoverflowMathTests
{
    private readonly CoverflowMath math = new() { SecondsPerCover = 4, TransitionSeconds = 1, SideCovers = 3 };

    [Fact]
    public void HoldsThenSlides()
    {
        Assert.Equal(0, math.PositionAt(0));
        Assert.Equal(0, math.PositionAt(2.9));
        Assert.Equal(0.5, math.PositionAt(3.5), 3);
        Assert.Equal(1, math.PositionAt(4.0), 3);
        Assert.Equal(2, math.PositionAt(8.0), 3);
    }

    [Fact]
    public void CenterCoverFacesViewer()
    {
        var pose = math.PoseFor(0);
        Assert.Equal(0, pose.X);
        Assert.Equal(0, pose.AngleDegrees);
        Assert.Equal(1, pose.Opacity);
    }

    [Fact]
    public void SideCoversAreMirrored()
    {
        var right = math.PoseFor(2);
        var left = math.PoseFor(-2);
        Assert.True(right.X > 0);
        Assert.Equal(-right.X, left.X, 6);
        Assert.Equal(-right.AngleDegrees, left.AngleDegrees, 6);
        Assert.True(right.AngleDegrees < 0, "right-hand covers turn to face the centre");
    }

    [Fact]
    public void PoseIsContinuousAcrossFirstSlot()
    {
        var justInside = math.PoseFor(0.9999);
        var justOutside = math.PoseFor(1.0001);
        Assert.Equal(justInside.X, justOutside.X, 2);
        Assert.Equal(justInside.AngleDegrees, justOutside.AngleDegrees, 1);
    }

    [Fact]
    public void OuterCoversFadeOut()
    {
        Assert.Equal(1, math.PoseFor(3).Opacity);
        Assert.Equal(0.5, math.PoseFor(3.5).Opacity, 6);
        Assert.Equal(0, math.PoseFor(4).Opacity);
    }

    [Theory]
    [InlineData(0L, 5, 0)]
    [InlineData(7L, 5, 2)]
    [InlineData(-1L, 5, 4)]
    [InlineData(-11L, 5, 4)]
    public void WrapIndexHandlesNegatives(long index, int count, int expected)
    {
        Assert.Equal(expected, CoverflowMath.WrapIndex(index, count));
    }
}

public class LibraryDataTests
{
    [Fact]
    public void ParsesExporterJson()
    {
        // Same shape and PascalCase names the Playnite add-on writes.
        const string json = """
        {
          "SchemaVersion": 1,
          "ExportedAt": "2026-09-27T12:00:00Z",
          "Games": [
            { "Id": "1", "Name": "Hades", "CoverPath": "C:\\covers\\hades.jpg", "Tags": ["Roguelike"], "AgeRatings": null }
          ]
        }
        """;
        var data = LibraryData.Parse(json);
        var game = Assert.Single(data.Games);
        Assert.Equal("Hades", game.Name);
        Assert.Empty(game.AgeRatings); // null normalised to empty
    }

    [Fact]
    public void RejectsNewerSchema()
    {
        Assert.Throws<InvalidDataException>(() => LibraryData.Parse("""{ "SchemaVersion": 99, "Games": [] }"""));
    }
}

public class OrderingTests
{
    private static readonly List<GameEntry> Games =
    [
        new() { Id = "1", Name = "Zelda", ReleaseYear = 1986 },
        new() { Id = "2", Name = "Apex", ReleaseYear = 2019 },
        new() { Id = "3", Name = "Doom", ReleaseYear = 1993 },
    ];

    [Fact]
    public void Alphabetical() =>
        Assert.Equal(new[] { "Apex", "Doom", "Zelda" }, GameOrdering.Order(Games, CoverOrder.Alphabetical, 0).Select(g => g.Name));

    [Fact]
    public void RandomIsRepeatableForTheSameSeed() =>
        Assert.Equal(
            GameOrdering.Order(Games, CoverOrder.Random, 42).Select(g => g.Id),
            GameOrdering.Order(Games, CoverOrder.Random, 42).Select(g => g.Id));
}

public class SettingsTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = new SaverSettings { SecondsPerCover = 9, Order = CoverOrder.RecentlyPlayed };
            settings.Filter.AdultKeywords = ["Custom"];
            SettingsStore.Save(settings, path);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(9, loaded.SecondsPerCover);
            Assert.Equal(CoverOrder.RecentlyPlayed, loaded.Order);
            Assert.Equal(new[] { "Custom" }, loaded.Filter.AdultKeywords); // replaced, not appended to defaults
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SanitizeClampsNonsense()
    {
        var s = new SaverSettings { SecondsPerCover = -5, SideCovers = 500, TransitionSeconds = 99, MosaicColumns = 0, MosaicFlipSeconds = 0 }.Sanitize();
        Assert.Equal(1, s.SecondsPerCover);
        Assert.Equal(12, s.SideCovers);
        Assert.Equal(2, s.MosaicColumns);
        Assert.Equal(0.2, s.MosaicFlipSeconds);
        Assert.True(s.TransitionSeconds <= s.SecondsPerCover);
    }
}

public class DataFolderMigrationTests
{
    [Fact]
    public void CopiesMissingFilesAndKeepsNewerOnes()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string oldFolder = Path.Combine(root, "PlayniteCoverflow"), newFolder = Path.Combine(root, "PCGameCoverArt");
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(Path.Combine(oldFolder, "settings.json"), "old settings");
            File.WriteAllText(Path.Combine(oldFolder, "library.json"), "old library");
            File.WriteAllText(Path.Combine(oldFolder, "screensaver.log"), "old log");
            Directory.CreateDirectory(newFolder);
            File.WriteAllText(Path.Combine(newFolder, "library.json"), "new library"); // the updated add-on already exported

            AppPaths.MigrateOldDataFolder(oldFolder, newFolder);

            Assert.Equal("old settings", File.ReadAllText(Path.Combine(newFolder, "settings.json")));
            Assert.Equal("new library", File.ReadAllText(Path.Combine(newFolder, "library.json")));
            Assert.False(File.Exists(Path.Combine(newFolder, "screensaver.log")));
            Assert.True(File.Exists(Path.Combine(oldFolder, "settings.json")), "old folder is left alone");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoesNothingWithoutAnOldFolder()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            AppPaths.MigrateOldDataFolder(Path.Combine(root, "missing"), Path.Combine(root, "new"));
            Assert.False(Directory.Exists(Path.Combine(root, "new")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
