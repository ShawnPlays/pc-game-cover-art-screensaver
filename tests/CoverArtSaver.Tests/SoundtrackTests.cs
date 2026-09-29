using CoverArtSaver.Core;
using CoverArtSaver.Core.Steam;

namespace CoverArtSaver.Tests;

public class SteamSoundtrackTests : IDisposable
{
    private readonly FakeSteam steam = new();
    private readonly string otherLibrary = Directory.CreateTempSubdirectory("steamlib").FullName;

    public void Dispose()
    {
        steam.Dispose();
        Directory.Delete(otherLibrary, recursive: true);
    }

    private static void Touch(string root, params string[] relativePaths)
    {
        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
    }

    private string Music => Path.Combine(steam.Folder, "steamapps", "music");

    [Fact]
    public void PlaysOneFormatPerAlbum()
    {
        Touch(Music,
            @"Frostpunk OST\HQ\FLAC\01 - Theme.flac", @"Frostpunk OST\HQ\WAV\01 - Theme.wav",
            @"Frostpunk OST\Normal\MP3\01 - Theme.mp3", @"Frostpunk OST\Normal\MP3\02 - Alone.mp3",
            @"Frostpunk OST\cover.jpg", @"Frostpunk OST\Ringtones.zip");
        Touch(Music, @"Per Aspera\1- Main Theme.wav", @"Per Aspera\2- Mars.wav");

        var tracks = SteamSoundtracks.Find(steam.Folder);

        Assert.Equal(["01 - Theme.mp3", "02 - Alone.mp3", "1- Main Theme.wav", "2- Mars.wav"], tracks.Select(t => Path.GetFileName(t.Path)));
        Assert.Equal(["Frostpunk OST", "Frostpunk OST", "Per Aspera", "Per Aspera"], tracks.Select(t => t.Album));
    }

    [Fact]
    public void TracksAndDiscsAreInNumberOrder()
    {
        Touch(Music, @"Album\Disc 2\1 C.mp3", @"Album\Disc 10\1 D.mp3", @"Album\Disc 1\10 B.mp3", @"Album\Disc 1\2 A.mp3");

        var names = SteamSoundtracks.Find(steam.Folder).Select(t => Path.GetFileNameWithoutExtension(t.Path));

        Assert.Equal(["2 A", "10 B", "1 C", "1 D"], names);
    }

    [Fact]
    public void FindsSoundtracksInEveryLibraryFolder()
    {
        Touch(Music, @"Bastion\01.mp3");
        Touch(Path.Combine(otherLibrary, "steamapps", "music"), @"Alyx\01.mp3");
        Touch(steam.Folder, @"steamapps\libraryfolders.vdf");
        File.WriteAllText(Path.Combine(steam.Folder, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steam.Folder.Replace(@"\", @"\\")}\" }} \"1\" {{ \"path\" \"{otherLibrary.Replace(@"\", @"\\")}\" }} " +
            "\"2\" { \"path\" \"Z:\\\\Unplugged\" } }");

        Assert.Equal(["Alyx", "Bastion"], SteamSoundtracks.Find(steam.Folder).Select(t => t.Album));
    }

    [Fact]
    public void ScanReportsWhatItFound()
    {
        steam.Write(); // makes it look like a Steam folder
        var settings = new SaverSettings { Source = LibrarySource.Playnite, SteamFolderOverride = steam.Folder };
        Assert.Contains("No Steam soundtracks", SteamSoundtracks.Scan(settings).Status);

        Touch(Music, @"Bastion\01.mp3", @"Bastion\02.mp3");
        var scan = SteamSoundtracks.Scan(settings); // Steam is used even though games come from Playnite
        Assert.Equal(2, scan.Tracks.Count);
        Assert.Equal("✔ 2 tracks from 1 soundtrack.", scan.Status);
    }

    [Fact]
    public void ScanReportsMissingSteam()
    {
        var scan = SteamSoundtracks.Scan(new SaverSettings { SteamFolderOverride = otherLibrary });
        Assert.Empty(scan.Tracks);
        Assert.Contains("Steam wasn't found", scan.Status);
    }
}

public class SoundtrackPlaylistTests
{
    private static readonly SoundtrackTrack[] Tracks =
        [.. Enumerable.Range(1, 8).Select(i => new SoundtrackTrack("Album", $"track{i}.mp3"))];

    private static List<string> Play(SoundtrackPlaylist playlist, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => playlist.Next()!.Path)];

    [Fact]
    public void InOrderStartsAtTheFirstTrackAndLoops()
    {
        var played = Play(new SoundtrackPlaylist(Tracks, shuffle: false, startAtRandomTrack: false, seed: 1), 10);
        Assert.Equal([.. Tracks.Select(t => t.Path), "track1.mp3", "track2.mp3"], played);
    }

    [Fact]
    public void RandomStartThenCarriesOnInOrder()
    {
        var starts = new HashSet<string>();
        for (var seed = 0; seed < 50; seed++)
        {
            var played = Play(new SoundtrackPlaylist(Tracks, shuffle: false, startAtRandomTrack: true, seed), 8);
            starts.Add(played[0]);
            var first = Array.FindIndex(Tracks, t => t.Path == played[0]);
            Assert.Equal(Enumerable.Range(0, 8).Select(i => Tracks[(first + i) % 8].Path), played);
        }

        Assert.True(starts.Count > 3, "the first track should vary");
    }

    [Fact]
    public void ShufflePlaysEveryTrackOncePerRound()
    {
        var playlist = new SoundtrackPlaylist(Tracks, shuffle: true, startAtRandomTrack: false, seed: 3);
        var previous = "";
        for (var round = 0; round < 20; round++)
        {
            var played = Play(playlist, Tracks.Length);
            Assert.Equal(Tracks.Select(t => t.Path).Order(), played.Order());
            Assert.NotEqual(previous, played[0]); // no repeat where one round ends and the next begins
            previous = played[^1];
        }
    }

    [Fact]
    public void ShuffleIsNotJustAlbumOrder()
    {
        var played = Play(new SoundtrackPlaylist(Tracks, shuffle: true, startAtRandomTrack: false, seed: 5), Tracks.Length);
        Assert.NotEqual(Tracks.Select(t => t.Path), played);
    }

    [Fact]
    public void EmptyAndSingleTrackPlaylists()
    {
        Assert.Null(new SoundtrackPlaylist([], shuffle: true, startAtRandomTrack: true, seed: 1).Next());
        var one = new SoundtrackPlaylist([Tracks[0]], shuffle: true, startAtRandomTrack: false, seed: 1);
        Assert.Equal(["track1.mp3", "track1.mp3", "track1.mp3"], Play(one, 3));
    }

    [Fact]
    public void MusicSettingsAreClampedAndSurviveARoundTrip()
    {
        var settings = new SaverSettings { Music = { Enabled = true, Shuffle = false, StartAtRandomTrack = true, Volume = 250 } }.Sanitize();
        Assert.Equal(100, settings.Music.Volume);

        var copy = settings.Clone();
        Assert.True(copy.Music.Enabled);
        Assert.False(copy.Music.Shuffle);
        Assert.True(copy.Music.StartAtRandomTrack);
    }
}
