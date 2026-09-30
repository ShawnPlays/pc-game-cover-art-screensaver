using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class UpdateCheckTests
{
    private const string Release = """
        {
          "tag_name": "v2.1.0",
          "html_url": "https://github.com/ShawnPlays/pc-game-cover-art-screensaver/releases/tag/v2.1.0",
          "assets": [
            { "name": "PCGameCoverArt.scr", "browser_download_url": "https://example.com/PCGameCoverArt.scr" },
            { "name": "PCGameCoverArtSetup_2.1.0.exe", "browser_download_url": "https://example.com/PCGameCoverArtSetup_2.1.0.exe" },
            { "name": "installer.yaml", "browser_download_url": "https://example.com/installer.yaml" }
          ]
        }
        """;

    [Fact]
    public void FindsANewerReleaseAndItsInstaller()
    {
        var update = UpdateCheck.Parse(Release, new Version(2, 0, 0, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 1, 0), update.Version);
        Assert.Equal("https://example.com/PCGameCoverArtSetup_2.1.0.exe", update.InstallerUrl);
        Assert.Equal("PCGameCoverArtSetup_2.1.0.exe", update.InstallerName);
        Assert.EndsWith("/releases/tag/v2.1.0", update.ReleaseUrl);
    }

    [Theory]
    [InlineData(2, 1, 0, 0)]  // same version (the app's own version has four parts)
    [InlineData(2, 1, 1, 0)]  // newer than GitHub's (a local build)
    [InlineData(3, 0, 0, 0)]
    public void NoUpdateWhenAlreadyCurrentOrNewer(int major, int minor, int build, int revision) =>
        Assert.Null(UpdateCheck.Parse(Release, new Version(major, minor, build, revision)));

    [Fact]
    public void OlderReleasesWithoutAnInstallerStillCount()
    {
        var update = UpdateCheck.Parse("""{ "tag_name": "v1.3.2", "html_url": "https://x/r", "assets": [] }""", new Version(1, 3, 1));
        Assert.NotNull(update);
        Assert.Null(update.InstallerUrl); // the settings window offers the release page instead
    }

    [Theory]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    public void OddAnswersAreNotUpdates(string json) =>
        Assert.Null(UpdateCheck.Parse(json, new Version(1, 0, 0)));

    [Fact]
    public void OnByDefaultAndCanBeTurnedOff()
    {
        Assert.True(new SaverSettings().CheckForUpdates);
        var off = new SaverSettings { CheckForUpdates = false }.Clone();
        Assert.False(off.CheckForUpdates);
    }
}
