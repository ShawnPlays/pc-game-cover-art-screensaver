namespace CoverArtSaver.Core;

/// <summary>The playnite:// and steam:// links that open or start a game from outside its launcher.</summary>
public static class GameLinks
{
    /// <summary>
    /// The link for a game, or null if there isn't one (Off, or an ID neither launcher knows).
    /// Playnite games are identified by their Playnite ID (a GUID); Steam ones by "steam:" + their app ID.
    /// </summary>
    public static string? For(GameEntry game, GameClickAction action)
    {
        if (action == GameClickAction.Off)
        {
            return null;
        }

        if (game.Id.StartsWith("steam:", StringComparison.Ordinal))
        {
            var appId = game.Id["steam:".Length..];
            if (!ulong.TryParse(appId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return null;
            }

            return action == GameClickAction.Play ? $"steam://rungameid/{appId}" : $"steam://nav/games/details/{appId}";
        }

        if (Guid.TryParse(game.Id, out var playniteId))
        {
            return action == GameClickAction.Play ? $"playnite://playnite/start/{playniteId}" : $"playnite://playnite/showgame/{playniteId}";
        }

        return null;
    }
}
