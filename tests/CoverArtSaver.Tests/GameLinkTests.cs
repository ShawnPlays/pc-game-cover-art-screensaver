using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class GameLinkTests
{
    private const string PlayniteId = "0f6a8c3e-5b1d-4c2a-9e7f-1a2b3c4d5e6f";

    [Theory]
    [InlineData(GameClickAction.Show, "playnite://playnite/showgame/" + PlayniteId)]
    [InlineData(GameClickAction.Play, "playnite://playnite/start/" + PlayniteId)]
    public void PlayniteGamesOpenInPlaynite(GameClickAction action, string expected)
    {
        Assert.Equal(expected, GameLinks.For(new GameEntry { Id = PlayniteId }, action));
    }

    [Theory]
    [InlineData(GameClickAction.Show, "steam://nav/games/details/620")]
    [InlineData(GameClickAction.Play, "steam://rungameid/620")]
    public void SteamGamesOpenInSteam(GameClickAction action, string expected)
    {
        Assert.Equal(expected, GameLinks.For(new GameEntry { Id = "steam:620" }, action));
    }

    [Theory]
    [InlineData(PlayniteId)]
    [InlineData("steam:620")]
    public void OffMeansNoLink(string id)
    {
        Assert.Null(GameLinks.For(new GameEntry { Id = id }, GameClickAction.Off));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("steam:")]
    [InlineData("steam:620/../../evil")]
    [InlineData("steam:-1")]
    public void UnknownIdsHaveNoLink(string id)
    {
        Assert.Null(GameLinks.For(new GameEntry { Id = id }, GameClickAction.Play));
    }

    [Fact]
    public void ClickActionRoundTripsAndDefaultsToOff()
    {
        Assert.Equal(GameClickAction.Off, new SaverSettings().ClickAction);

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            SettingsStore.Save(new SaverSettings { ClickAction = GameClickAction.Play }, path);
            Assert.Equal(GameClickAction.Play, SettingsStore.Load(path).ClickAction);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SanitizeResetsAnUnknownClickAction()
    {
        Assert.Equal(GameClickAction.Off, new SaverSettings { ClickAction = (GameClickAction)42 }.Sanitize().ClickAction);
    }
}
