using CoverArtSaver.Core;
using NAudio.CoreAudioApi;

namespace CoverArtSaver.Audio;

/// <summary>A speaker, headphone or other playback device Windows knows about.</summary>
internal sealed record AudioOutput(string Id, string Name);

/// <summary>Lists the playback devices and opens the one the music should use.</summary>
internal static class AudioOutputs
{
    /// <summary>The devices that are plugged in and switched on, by name.</summary>
    public static List<AudioOutput> List()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return [.. enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => new AudioOutput(d.ID, d.FriendlyName))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't list the audio outputs", ex);
            return [];
        }
    }

    /// <summary>The chosen device if it's plugged in; null means use Windows' default output.</summary>
    public static MMDevice? OpenChosen(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(id);
            if (device.State == DeviceState.Active)
            {
                return device;
            }

            Log.Info($"Music: {device.FriendlyName} isn't connected; using the default output instead.");
            device.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Music: the chosen output is gone; using the default output instead.", ex);
        }

        return null;
    }
}
