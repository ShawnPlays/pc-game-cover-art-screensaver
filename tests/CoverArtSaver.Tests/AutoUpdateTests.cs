using System.Net;
using System.Xml.Linq;
using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class AutoUpdateTests
{
    private const string InstallerUrl =
        "https://github.com/ShawnPlays/pc-game-cover-art-screensaver/releases/download/v2.2.0/PCGameCoverArtSetup_2.2.0.exe";

    private static string Release(string installerUrl = InstallerUrl) => $$"""
        { "tag_name": "v2.2.0", "html_url": "https://github.com/x/releases/tag/v2.2.0",
          "assets": [ { "name": "PCGameCoverArtSetup_2.2.0.exe", "browser_download_url": "{{installerUrl}}" } ] }
        """;

    /// <summary>Answers the "latest release" call with <paramref name="json"/> and any download with a few bytes.</summary>
    private sealed class FakeGitHub(string json) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            var body = request.RequestUri!.Host == "api.github.com" ? new StringContent(json) : new ByteArrayContent([0x4D, 0x5A, 1, 2, 3]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    private static (AutoUpdater Updater, FakeGitHub GitHub, List<(string Path, string Args)> Started) Make(
        string json, string current = "2.1.0", bool running = false, bool trusted = true)
    {
        var github = new FakeGitHub(json);
        var started = new List<(string, string)>();
        var updater = new AutoUpdater(new HttpClient(github), Version.Parse(current))
        {
            IsScreensaverRunning = () => running,
            IsTrusted = _ => trusted,
            StartInstaller = (path, args) =>
            {
                started.Add((path, args));
                return true;
            },
        };
        return (updater, github, started);
    }

    [Fact]
    public async Task DownloadsAndStartsTheNewInstallerSilently()
    {
        var (updater, github, started) = Make(Release());

        Assert.Equal(AutoUpdateResult.InstallerStarted, await updater.RunAsync());

        var (path, args) = Assert.Single(started);
        Assert.Equal(new byte[] { 0x4D, 0x5A, 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.StartsWith(Path.GetTempPath(), path);
        Assert.Contains("/VERYSILENT", args);
        Assert.Contains("/MERGETASKS=\"!activate\"", args); // never changes the active screensaver
        Assert.Contains(InstallerUrl, github.Requests);
    }

    [Fact]
    public async Task NothingHappensWhenUpToDate()
    {
        var (updater, _, started) = Make(Release(), current: "2.2.0");
        Assert.Equal(AutoUpdateResult.UpToDate, await updater.RunAsync());
        Assert.Empty(started);
    }

    [Fact]
    public async Task WaitsWhileTheScreensaverIsRunning()
    {
        var (updater, github, started) = Make(Release(), running: true);
        Assert.Equal(AutoUpdateResult.ScreensaverRunning, await updater.RunAsync());
        Assert.Empty(started);
        Assert.Empty(github.Requests); // doesn't even ask GitHub
    }

    [Fact]
    public async Task RefusesAnInstallerThatFailsTheSignatureCheck()
    {
        var (updater, _, started) = Make(Release(), trusted: false);
        Assert.Equal(AutoUpdateResult.NotTrusted, await updater.RunAsync());
        Assert.Empty(started);
    }

    [Theory]
    [InlineData("http://github.com/ShawnPlays/x/releases/download/v2.2.0/PCGameCoverArtSetup_2.2.0.exe")] // not HTTPS
    [InlineData("https://example.com/releases/download/v2.2.0/PCGameCoverArtSetup_2.2.0.exe")]           // not GitHub
    public async Task OnlyDownloadsFromGitHubReleases(string url)
    {
        var (updater, _, started) = Make(Release(url));
        Assert.Equal(AutoUpdateResult.NoInstaller, await updater.RunAsync());
        Assert.Empty(started);
    }

    [Fact]
    public void TaskRunsTheScreensaverDailyAsSystem()
    {
        var xml = XDocument.Parse(AutoUpdater.TaskXml(@"C:\Windows\System32\PCGameCoverArt.scr"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal(@"C:\Windows\System32\PCGameCoverArt.scr", xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal("/update", xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("S-1-5-18", xml.Descendants(ns + "UserId").Single().Value); // SYSTEM
        Assert.Equal("1", xml.Descendants(ns + "DaysInterval").Single().Value);
        Assert.Equal("true", xml.Descendants(ns + "StartWhenAvailable").Single().Value);
    }

    [Theory]
    [InlineData("/update", SaverMode.Update)]
    [InlineData("/autoupdate on", SaverMode.EnableAutoUpdate)]
    [InlineData("/autoupdate off", SaverMode.DisableAutoUpdate)]
    [InlineData("/AUTOUPDATE ON", SaverMode.EnableAutoUpdate)]
    [InlineData("/autoupdate", SaverMode.DisableAutoUpdate)]
    public void ParsesTheUpdateOptions(string commandLine, SaverMode expected) =>
        Assert.Equal(expected, ScreensaverArgs.Parse(commandLine.Split(' ')).Mode);
}
