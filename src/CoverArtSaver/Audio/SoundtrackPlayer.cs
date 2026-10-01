using System.Windows.Threading;
using CoverArtSaver.Core;
using CoverArtSaver.Core.Steam;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CoverArtSaver.Audio;

/// <summary>
/// Plays installed Steam soundtracks while the screensaver runs, fading in at the start. One per session, not one
/// per monitor. Decodes with Media Foundation, so it plays whatever Windows itself can (MP3, FLAC, M4A, WMA, WAV),
/// and plays through WASAPI so the music can go to a chosen output instead of Windows' default.
/// </summary>
internal sealed class SoundtrackPlayer
{
    private static readonly TimeSpan FadeIn = TimeSpan.FromSeconds(3);

    private readonly MusicSettings settings;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer fadeTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private SoundtrackPlaylist? playlist;
    private MMDevice? device;
    private bool deviceChecked;
    private WasapiPlayer? output;
    private MediaFoundationReader? reader;
    private VolumeSampleProvider? volume;
    private double currentVolume;
    private DateTime fadeStart;
    private int failuresInARow;
    private bool stopped;

    private SoundtrackPlayer(MusicSettings settings)
    {
        this.settings = settings;
        fadeTimer.Tick += (_, _) =>
        {
            var progress = Math.Min(1, (DateTime.UtcNow - fadeStart) / FadeIn);
            SetVolume(TargetVolume * progress);
            if (progress >= 1)
            {
                fadeTimer.Stop();
            }
        };
    }

    private double TargetVolume => settings.Volume / 100.0;

    private int MaxFailures => Math.Min(playlist!.Count, 10);

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
        CloseTrack();
        device?.Dispose();
        device = null;
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
        fadeStart = DateTime.UtcNow;
        fadeTimer.Start();
        PlayNext();
    }

    private void SetVolume(double value)
    {
        currentVolume = value;
        if (volume != null)
        {
            volume.Volume = (float)value;
        }
    }

    private async void PlayNext()
    {
        while (!stopped && playlist?.Next() is { } track)
        {
            if (await TryPlay(track.Path))
            {
                return;
            }

            // Skip unplayable files, but give up if nothing plays at all rather than spinning forever.
            if (++failuresInARow >= MaxFailures)
            {
                Log.Info("Music: nothing would play, so the music is off for this session.");
                return;
            }
        }
    }

    private async Task<bool> TryPlay(string path)
    {
        CloseTrack();
        MediaFoundationReader? newReader = null;
        WasapiPlayer? newOutput = null;
        try
        {
            // Looked up again after a playback error, in case the chosen device was unplugged.
            if (!deviceChecked)
            {
                device = AudioOutputs.OpenChosen(settings.OutputDeviceId);
                deviceChecked = true;
            }

            newReader = new MediaFoundationReader(path);
            var newVolume = new VolumeSampleProvider(newReader.ToSampleProvider()) { Volume = (float)currentVolume };
            var builder = new WasapiPlayerBuilder().WithCategory(AudioStreamCategory.Media);
            // No device chosen (or it's unplugged): follow Windows' default output, even if that changes mid-track.
            newOutput = device != null ? builder.WithDevice(device).Build() : await builder.WithDefaultDeviceStreamRouting().BuildAsync();
            newOutput.PlaybackStopped += OnPlaybackStopped;
            newOutput.Init(new SampleToWaveProvider(newVolume));
            if (stopped)
            {
                throw new OperationCanceledException();
            }

            newOutput.Play();
            reader = newReader;
            volume = newVolume;
            output = newOutput;
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Log.Error($"Couldn't play {path}", ex);
            }

            if (newOutput != null)
            {
                newOutput.PlaybackStopped -= OnPlaybackStopped;
                newOutput.Dispose();
            }

            newReader?.Dispose();
            return false;
        }
    }

    /// <summary>Raised when a track ends or fails, possibly on the audio thread.</summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnPlaybackStopped(sender, e));
            return;
        }

        if (stopped || sender != output)
        {
            return;
        }

        if (e.Exception == null)
        {
            failuresInARow = 0;
        }
        else
        {
            Log.Error("Music playback stopped", e.Exception);
            device?.Dispose();
            device = null;
            deviceChecked = false;
            if (++failuresInARow >= MaxFailures)
            {
                Log.Info("Music: nothing would play, so the music is off for this session.");
                return;
            }
        }

        PlayNext();
    }

    private void CloseTrack()
    {
        if (output != null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Stop();
            output.Dispose();
            output = null;
        }

        reader?.Dispose();
        reader = null;
        volume = null;
    }
}
