using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using CoverArtSaver.Core;
using CoverArtSaver.Interop;

namespace CoverArtSaver.Updates;

/// <summary>
/// The Windows side of automatic updates: running <see cref="AutoUpdater"/> for "/update", and adding or removing the
/// daily scheduled task for "/autoupdate on|off" (the installer and the settings window's About tab use those).
/// </summary>
internal static class AutoUpdateService
{
    /// <summary>Where the installer puts the screensaver; the scheduled task runs this copy.</summary>
    public static string InstalledScreensaver => Path.Combine(Environment.SystemDirectory, "PCGameCoverArt.scr");

    /// <summary>
    /// The updater runs as SYSTEM, which has no user data folder, so it logs next to the uninstaller, where only
    /// administrators can write but anyone can read.
    /// </summary>
    public static string UpdateLogFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PC Game Cover Art", "update.log");

    private static Version CurrentVersion => typeof(AutoUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"/update": install a newer version if there is one. Returns the exit code.</summary>
    public static async Task<int> RunAsync()
    {
        var self = Environment.ProcessPath ?? InstalledScreensaver;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var updater = new AutoUpdater(http, CurrentVersion)
        {
            IsScreensaverRunning = () => Process.GetProcessesByName("PCGameCoverArt").Any(p => p.Id != Environment.ProcessId),
            IsTrusted = installer => Authenticode.MayRun(installer, self),
            StartInstaller = StartInstaller,
            Log = Log.Info,
        };

        try
        {
            await updater.RunAsync();
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException
                                     or System.Text.Json.JsonException)
        {
            Log.Error("Automatic update failed; trying again next time.", ex);
            return 1;
        }
    }

    private static bool StartInstaller(string path, string arguments)
    {
        try
        {
            // Not waited for: the installer replaces this very program, and closes it if it's still running.
            using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false });
            return process != null;
        }
        catch (Win32Exception ex)
        {
            Log.Error("Couldn't start the installer", ex);
            return false;
        }
    }

    /// <summary>"/autoupdate on|off" (needs administrator rights). Returns the exit code.</summary>
    public static int SetEnabled(bool on)
    {
        if (!on)
        {
            var existed = IsEnabled() != false;
            var code = Schtasks($"/Delete /TN \"{AutoUpdater.TaskName}\" /F");
            Log.Info(code == 0 ? "Automatic updates turned off." : $"Automatic updates: couldn't remove the task (schtasks exit code {code}).");
            return code == 0 || !existed ? 0 : code;
        }

        var executable = File.Exists(InstalledScreensaver) ? InstalledScreensaver : Environment.ProcessPath ?? InstalledScreensaver;
        var xml = Path.Combine(Path.GetTempPath(), $"PCGameCoverArtUpdateTask-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xml, AutoUpdater.TaskXml(executable), Encoding.Unicode); // Task Scheduler wants UTF-16
            var code = Schtasks($"/Create /TN \"{AutoUpdater.TaskName}\" /XML \"{xml}\" /F");
            Log.Info(code == 0 ? $"Automatic updates turned on (runs {executable} /update daily)."
                               : $"Automatic updates: couldn't create the task (schtasks exit code {code}).");
            return code;
        }
        finally
        {
            File.Delete(xml);
        }
    }

    /// <summary>Whether the daily task exists: true or false, or null if Windows won't say (then the checkbox is disabled).</summary>
    public static bool? IsEnabled()
    {
        dynamic? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
            service.Connect();
            var task = service.GetFolder("\\").GetTask(AutoUpdater.TaskName);
            return task != null;
        }
        catch (FileNotFoundException)
        {
            return false; // no such task
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            return false;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or TypeLoadException)
        {
            return null;
        }
        finally
        {
            if (service != null)
            {
                Marshal.FinalReleaseComObject(service);
            }
        }
    }

    /// <summary>
    /// Turns automatic updates on or off from the settings window: asks for administrator rights, then runs this
    /// program with /autoupdate. Returns false if the person said no, or it failed.
    /// </summary>
    public static bool SetEnabledAsAdministrator(bool on)
    {
        var program = File.Exists(InstalledScreensaver) ? InstalledScreensaver : Environment.ProcessPath!;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, on ? "/autoupdate on" : "/autoupdate off")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // "No" on the administrator prompt
        }
    }

    private static int Schtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Log.Info("schtasks " + arguments + ": " + output.Trim());
        }

        return process.ExitCode;
    }
}
