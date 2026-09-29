using System.Windows.Media;
using System.Windows.Threading;
using CoverArtSaver.Core;
using CoverArtSaver.Core.Steam;

namespace CoverArtSaver.Audio;

/// <summary>
/// Plays installed Steam soundtracks while the screensaver runs, fading in at the start. One per session, not one
/// per monitor. Uses WPF's MediaPlayer, which plays whatever Windows itself can (MP3, FLAC, M4A, WMA, WAV).
/// </summary>
internal sealed class SoundtrackPlayer
{
    private static readonly TimeSpan FadeIn = TimeSpan.FromSeconds(3);

    private readonly MediaPlayer player = new();
    private readonly MusicSettings settings;
    private readonly DispatcherTimer fadeTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private SoundtrackPlaylist? playlist;
    private DateTime fadeStart;
    private int failuresInARow;
    private bool stopped;

    private SoundtrackPlayer(MusicSettings settings)
    {
        this.settings = settings;
        player.Volume = 0;
        player.MediaEnded += (_, _) => PlayNext();
        player.MediaOpened += (_, _) => failuresInARow = 0;
        player.MediaFailed += (_, e) =>
        {
            Log.Error($"Couldn't play {player.Source?.LocalPath}", e.ErrorException);
            // Skip unplayable files, but give up if nothing plays at all rather than spinning forever.
            if (++failuresInARow < Math.Min(playlist!.Count, 10))
            {
                PlayNext();
            }
        };
        fadeTimer.Tick += (_, _) =>
        {
            var progress = Math.Min(1, (DateTime.UtcNow - fadeStart) / FadeIn);
            player.Volume = TargetVolume * progress;
            if (progress >= 1)
            {
                fadeTimer.Stop();
            }
        };
    }

    private double TargetVolume => settings.Volume / 100.0;

    /// <summary>Starts the music if it's switched on; returns null otherwise. Call on the UI thread.</summary>
    public static SoundtrackPlayer? Start(SaverSettings settings)
    {
        if (!settings.Music.Enabled || settings.Music.Volume == 0)
        {
            return null;
        }

        var music = new SoundtrackPlayer(settings.Music);
        music.Begin(settings);
        return music;
    }

    public void Stop()
    {
        stopped = true;
        fadeTimer.Stop();
        player.Stop();
        player.Close();
    }

    private async void Begin(SaverSettings settings)
    {
        // Listing the files can take a moment if a library folder is on a drive that has to spin up.
        var scan = await Task.Run(() => SteamSoundtracks.Scan(settings));
        Log.Info("Music: " + scan.Status);
        if (stopped || scan.Tracks.Count == 0)
        {
            return;
        }

        playlist = new SoundtrackPlaylist(scan.Tracks, settings.Music.Shuffle, settings.Music.StartAtRandomTrack, Environment.TickCount);
        PlayNext();
        fadeStart = DateTime.UtcNow;
        fadeTimer.Start();
    }

    private void PlayNext()
    {
        if (stopped || playlist?.Next() is not { } track)
        {
            return;
        }

        player.Open(new Uri(track.Path));
        player.Play();
    }
}
