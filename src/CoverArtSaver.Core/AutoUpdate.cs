using System.Security;

namespace CoverArtSaver.Core;

public enum AutoUpdateResult
{
    UpToDate,
    /// <summary>The screensaver or its settings are open; installing would close them, so try again tomorrow.</summary>
    ScreensaverRunning,
    /// <summary>A newer release exists but has no installer (or it isn't on GitHub).</summary>
    NoInstaller,
    /// <summary>The download isn't signed by the same publisher as the running copy, so it wasn't run.</summary>
    NotTrusted,
    /// <summary>The installer is running; it replaces this program, so the updater doesn't wait for it.</summary>
    InstallerStarted,
    /// <summary>The installer couldn't be started.</summary>
    Failed,
}

/// <summary>
/// Installs new versions unattended. The installer sets up a daily scheduled task (see <see cref="TaskXml"/>) that runs
/// "PCGameCoverArt.scr /update" as SYSTEM, so no administrator prompt appears. Each run:
/// <list type="number">
/// <item>skips the day if the screensaver or its settings are open (the installer would have to close them);</item>
/// <item>asks GitHub for the latest release, like the settings window does;</item>
/// <item>downloads its installer into a new folder only SYSTEM can write to, so nobody can swap the file;</item>
/// <item>checks the download is signed by the same publisher as the running copy (once releases are signed);</item>
/// <item>starts it silently, keeping the user's choices except "use it as my screensaver", which is per user.</item>
/// </list>
/// </summary>
public sealed class AutoUpdater(HttpClient http, Version currentVersion)
{
    public const string TaskName = "PC Game Cover Art Screensaver update";

    /// <summary>Inno Setup switches: no windows or questions, don't restart, keep previous task choices but don't set the screensaver.</summary>
    public const string SilentInstallArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /MERGETASKS=\"!activate\"";

    private const string DownloadFolderPrefix = "PCGameCoverArtUpdate-";

    public Func<bool> IsScreensaverRunning { get; init; } = () => false;

    /// <summary>Whether a downloaded installer may be run (signature check). Defaults to yes.</summary>
    public Func<string, bool> IsTrusted { get; init; } = _ => true;

    /// <summary>Starts the installer (path, arguments) without waiting; false if it couldn't be started.</summary>
    public Func<string, string, bool> StartInstaller { get; init; } = (_, _) => false;

    public Action<string> Log { get; init; } = _ => { };

    /// <summary>Where download folders go. The temp folder: for SYSTEM, one only administrators can write to.</summary>
    public string DownloadRoot { get; init; } = Path.GetTempPath();

    public async Task<AutoUpdateResult> RunAsync(CancellationToken cancel = default)
    {
        RemoveOldDownloads();
        if (IsScreensaverRunning())
        {
            Log("The screensaver or its settings are open; trying again next time.");
            return AutoUpdateResult.ScreensaverRunning;
        }

        var update = await UpdateCheck.CheckAsync(currentVersion, http, cancel);
        if (update == null)
        {
            Log($"Up to date ({currentVersion.ToString(3)}).");
            return AutoUpdateResult.UpToDate;
        }

        if (update.InstallerUrl is not { } url || !IsGitHubDownload(url))
        {
            Log($"Version {update.Version} has no installer to download; see {update.ReleaseUrl}.");
            return AutoUpdateResult.NoInstaller;
        }

        // A new, randomly named folder: created by this process, so nothing else can have planted a file in it.
        var folder = Path.Combine(DownloadRoot, DownloadFolderPrefix + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"PCGameCoverArtSetup_{update.Version}.exe");
        Log($"Downloading version {update.Version} from {url}");
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            await using var target = File.Create(path);
            await response.Content.CopyToAsync(target, cancel);
        }

        if (!IsTrusted(path))
        {
            Log("The download isn't signed by the same publisher as the installed version, so it wasn't run.");
            return AutoUpdateResult.NotTrusted;
        }

        var log = Path.Combine(folder, "install.log");
        if (!StartInstaller(path, $"{SilentInstallArguments} /LOG=\"{log}\""))
        {
            Log("Couldn't start the installer.");
            return AutoUpdateResult.Failed;
        }

        Log($"Installing version {update.Version} (installer log: {log}).");
        return AutoUpdateResult.InstallerStarted;
    }

    /// <summary>Only files served by GitHub's release downloads, over HTTPS.</summary>
    internal static bool IsGitHubDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal);

    /// <summary>Earlier runs can't delete their installer while it's running, so tidy up at the start of the next one.</summary>
    private void RemoveOldDownloads()
    {
        try
        {
            foreach (var old in Directory.EnumerateDirectories(DownloadRoot, DownloadFolderPrefix + "*"))
            {
                if (Directory.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1))
                {
                    Directory.Delete(old, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use or not ours; try again next time.
        }
    }

    /// <summary>
    /// Task Scheduler won't start a .scr file directly (the task fails with "file not found", 0x80070002, before the
    /// program starts), so the task runs it through cmd.exe, which starts it like any program.
    /// </summary>
    public static string TaskArguments(string executable) => $"/c \"\"{executable}\" /update\"";

    /// <summary>
    /// The daily scheduled task, in Task Scheduler's XML format. It runs as SYSTEM (no prompts), some time between noon
    /// and 4 pm, and catches up after the PC was off. Anyone signed in may read it, so the settings window can show
    /// whether it's on. <paramref name="runAsSystem"/> is false only in tests, which can't register SYSTEM tasks.
    /// </summary>
    public static string TaskXml(string executable, bool runAsSystem = true)
    {
        var principal = runAsSystem
            ? "<UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel>"
            : "<LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel>";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>PC Game Cover Art Screensaver</Author>
                <Description>Checks GitHub once a day for a new version of PC Game Cover Art Screensaver and installs it. Turn it off on the About tab of the screensaver's settings.</Description>
                <SecurityDescriptor>D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;AU)</SecurityDescriptor>
              </RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>2026-01-01T12:00:00</StartBoundary>
                  <RandomDelay>PT4H</RandomDelay>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                  <Enabled>true</Enabled>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">{principal}</Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>%SystemRoot%\System32\cmd.exe</Command>
                  <Arguments>{SecurityElement.Escape(TaskArguments(executable))}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
