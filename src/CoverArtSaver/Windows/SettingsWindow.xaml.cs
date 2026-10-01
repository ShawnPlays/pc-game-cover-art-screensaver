using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
using System.Windows.Threading;
using CoverArtSaver.Audio;
using CoverArtSaver.Core;
using CoverArtSaver.Core.Steam;
using Microsoft.Win32;

namespace CoverArtSaver.Windows;

/// <summary>The dialog shown for "Settings…" in Windows' Screen Saver Settings, or when the .scr is double-clicked.</summary>
public partial class SettingsWindow : Window
{
    // Change this after you create your GitHub repository.
    public const string RepositoryUrl = "https://github.com/ShawnPlays/pc-game-cover-art-screensaver";

    private readonly SaverSettings working;
    private readonly DispatcherTimer summaryTimer;
    private LibraryData? library;
    private string? libraryStatus;
    private string? loadedLibraryKey;
    private CoverSizeCache? coverSizes;
    private bool measuringCovers;
    private string? musicFolderKey;
    private string? skipLibrariesKey;
    private AvailableUpdate? update;

    /// <summary>Long timeout because it also downloads the installer (about 70 MB); the check itself has its own.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static Version CurrentVersion => typeof(SettingsWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public SettingsWindow(SaverSettings settings)
    {
        InitializeComponent();

        working = settings.Clone(); // edit a copy so Cancel really cancels
        DataContext = working;

        SourceCombo.ItemsSource = new Dictionary<LibrarySource, string>
        {
            [LibrarySource.Playnite] = "Playnite (needs the PC Game Cover Art Exporter add-on)",
            [LibrarySource.Steam] = "Steam",
        };
        LayoutCombo.ItemsSource = Enum.GetValues<SaverLayout>();
        OrderCombo.ItemsSource = Enum.GetValues<CoverOrder>();
        MonitorCombo.ItemsSource = Enum.GetValues<MultiMonitorMode>();
        ShapeCombo.ItemsSource = new Dictionary<CoverShape, string>
        {
            [CoverShape.All] = "Use all cover art",
            [CoverShape.Vertical] = "Only vertical cover art (box art)",
            [CoverShape.Square] = "Only square cover art",
            [CoverShape.Horizontal] = "Only horizontal cover art (landscape)",
        };
        AdultCombo.ItemsSource = new Dictionary<ContentFilterMode, string>
        {
            [ContentFilterMode.Off] = "Show them",
            [ContentFilterMode.Hide] = "Hide them",
            [ContentFilterMode.Only] = "Show only these",
        };
        MatureCombo.ItemsSource = AdultCombo.ItemsSource;
        FeaturedModeCombo.ItemsSource = new Dictionary<FeaturedTileMode, string>
        {
            [FeaturedTileMode.BarelyPlayed] = "Games you've barely played",
            [FeaturedTileMode.Random] = "Any game, at random",
        };
        RatingCombo.ItemsSource = new Dictionary<RatingLimit, string>
        {
            [RatingLimit.Any] = "Any rating (don't check)",
            [RatingLimit.Mixed] = "Mixed or better",
            [RatingLimit.MostlyPositive] = "Mostly Positive or better",
            [RatingLimit.Positive] = "Positive or better",
            [RatingLimit.VeryPositive] = "Very Positive or better",
            [RatingLimit.OverwhelminglyPositive] = "Overwhelmingly Positive only",
        };
        ShowAudioOutputs();
        AdultKeywordsBox.Text = string.Join(Environment.NewLine, working.Filter.AdultKeywords);
        MatureKeywordsBox.Text = string.Join(Environment.NewLine, working.Filter.MatureKeywords);

        VersionText.Text = "Version " + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "?");
        CopyrightText.Text = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        RepoLink.NavigateUri = new Uri(RepositoryUrl);
        DataFolderLink.NavigateUri = new Uri(AppPaths.DataFolder);

        // Recalculate "N games will be shown" shortly after any change, without hammering the disk on every keystroke.
        summaryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        summaryTimer.Tick += (_, _) =>
        {
            summaryTimer.Stop();
            UpdateSummary();
        };
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ScheduleSummary));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(ScheduleSummary));
        // Each routed event needs its own delegate type; TextChanged uses TextChangedEventHandler.
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(ScheduleSummary));
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>(ScheduleSummary));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(ScheduleSummary));

        Loaded += (_, _) =>
        {
            UpdateSummary();
            UpdateSourcePanels();
            UpdateLayoutSections();
            UpdateKeywordBoxes();
            UpdateMosaicRows(working.MosaicColumns);
            UpdateFeaturedOptions();
            ShowAutoUpdateState();
            if (working.CheckForUpdates)
            {
                _ = CheckForUpdatesAsync(userAsked: false);
            }
        };
    }

    private void OnFeaturedModeChanged(object sender, SelectionChangedEventArgs e) => UpdateFeaturedOptions();

    /// <summary>Play time and rating only matter when large tiles go to games you've barely played.</summary>
    private void UpdateFeaturedOptions() =>
        BarelyPlayedOptions.IsEnabled = FeaturedModeCombo.SelectedValue is not FeaturedTileMode.Random;

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e) => UpdateLayoutSections();

    private void OnShapeChanged(object sender, SelectionChangedEventArgs e)
    {
        ScheduleSummary(sender, e);
        UpdateMosaicRows(working.MosaicColumns); // mosaic tiles take the shape of the chosen covers
    }

    private void OnContentModeChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateKeywordBoxes();
        ScheduleSummary(sender, e);
    }

    /// <summary>The word lists only matter while their filter is on.</summary>
    private void UpdateKeywordBoxes()
    {
        AdultKeywordsBox.IsEnabled = AdultCombo.SelectedValue is not ContentFilterMode.Off;
        MatureKeywordsBox.IsEnabled = MatureCombo.SelectedValue is not ContentFilterMode.Off;
    }

    private void OnMonitorModeChanged(object sender, SelectionChangedEventArgs e) =>
        TakeTurnsCheck.IsEnabled = MonitorCombo.SelectedItem is MultiMonitorMode.Independent;

    /// <summary>Only show the options that apply to the chosen style.</summary>
    private void UpdateLayoutSections()
    {
        var mosaic = LayoutCombo.SelectedItem is SaverLayout.Mosaic;
        MosaicGroup.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        CoverflowGroup.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
        CoverflowAppearance.Visibility = CoverflowGroup.Visibility;
        LayoutDescription.Text = mosaic
            ? "A wall of covers that flip over one at a time, like iTunes' Album Artwork screensaver."
            : "Covers slide past in 3D with angled side covers and reflections.";
    }

    private void OnMosaicColumnsChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateMosaicRows((int)e.NewValue);

    private void UpdateMosaicRows(int columns)
    {
        if (MosaicRowsText == null)
        {
            return; // the slider fires while the window is still being built
        }

        var layout = MosaicLayout.Fit(columns, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight,
            CoverShapes.TileAspect(working.Filter.CoverShape, working.Source));
        MosaicRowsText.Text = $"{layout.Rows} rows ({layout.TileCount} covers) on your main screen";
    }

    private void ScheduleSummary(object sender, RoutedEventArgs e)
    {
        summaryTimer.Stop();
        summaryTimer.Start();
    }

    /// <summary>Copies the keyword text boxes back into the settings object.</summary>
    private void CommitKeywordLists()
    {
        working.Filter.AdultKeywords = SplitLines(AdultKeywordsBox.Text);
        working.Filter.MatureKeywords = SplitLines(MatureKeywordsBox.Text);
    }

    private static List<string> SplitLines(string text) =>
        [.. text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private void UpdateSummary()
    {
        CommitKeywordLists();
        UpdateMusicStatus();

        // Only re-read when something that changes the game list changes. The cover shape is included because
        // it decides which of Steam's images each game uses.
        var key = string.Join("|", working.Source, working.LibraryFile, working.SteamFolderOverride, working.Filter.CoverShape);
        if (key != loadedLibraryKey)
        {
            var read = LibrarySources.Read(working);
            library = read.Data;
            libraryStatus = read.Status;
            loadedLibraryKey = key;
        }

        LibraryStatus.Text = libraryStatus;
        if (library == null)
        {
            FilterSummary.Text = "No games to preview yet.";
            FeaturedSummary.Text = "";
            return;
        }

        if (working.Filter.CoverShape != CoverShape.All && coverSizes == null)
        {
            MeasureCoversThenUpdate(library.Games);
            FilterSummary.Text = "Checking the shape of your cover art…";
            FeaturedSummary.Text = "";
            return;
        }

        UpdateSkipLibraries(library);
        var result = new GameFilter(working.Filter, coverAspect: coverSizes == null ? null : coverSizes.GetAspect).Apply(library.Games);
        var reasons = result.ExcludedCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Value} {DescribeReason(kv.Key, working.Filter.CoverShape, working.Source)}");
        FilterSummary.Text =
            $"{result.Included.Count} of {library.Games.Count} games will appear in the screensaver." +
            (result.TotalExcluded > 0 ? "\nHidden: " + string.Join(", ", reasons) + "." : "");

        var featured = result.Included.Count(g => FeaturedGames.Qualifies(g, working.MosaicFeatured));
        FeaturedSummary.Text = working.MosaicFeatured.Mode == FeaturedTileMode.Random
            ? $"Any of the {result.Included.Count} games in the screensaver can get a large tile."
            : $"{featured} of the {result.Included.Count} games in the screensaver qualify." +
              (featured == 0 ? " With none, there are no large tiles." : "");
    }

    /// <summary>
    /// One checkbox per library integration in the export, plus any already chosen, with how many games each has.
    /// Rebuilt only when that list changes.
    /// </summary>
    private void UpdateSkipLibraries(LibraryData data)
    {
        var chosen = working.MosaicFeatured.SkipLibraries;
        var counts = data.Games
            .Where(g => g.Library != null)
            .GroupBy(g => g.Library!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var names = counts.Keys.Union(chosen, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var key = string.Join("|", names.Select(n => $"{n}:{counts.GetValueOrDefault(n)}"));
        if (key == skipLibrariesKey)
        {
            return;
        }

        skipLibrariesKey = key;
        SkipLibrariesPanel.Children.Clear();
        foreach (var name in names)
        {
            var box = new CheckBox
            {
                Content = counts.TryGetValue(name, out var count) ? $"{name} ({count})" : name,
                IsChecked = chosen.Contains(name, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 2, 16, 2),
            };
            box.Checked += (_, _) =>
            {
                if (!chosen.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    chosen.Add(name);
                }
            };
            box.Unchecked += (_, _) => chosen.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            SkipLibrariesPanel.Children.Add(box);
        }
    }

    /// <summary>Windows' default output, every device that's plugged in, and the chosen one even if it isn't.</summary>
    private void ShowAudioOutputs()
    {
        var music = working.Music;
        var outputs = AudioOutputs.List();
        if (!string.IsNullOrEmpty(music.OutputDeviceId) && outputs.All(o => o.Id != music.OutputDeviceId))
        {
            outputs.Add(new AudioOutput(music.OutputDeviceId, $"{music.OutputDeviceName ?? "Chosen output"} (not connected)"));
        }

        outputs.Insert(0, new AudioOutput("", "Windows default output"));
        OutputCombo.ItemsSource = outputs;
        OutputCombo.SelectedValue = music.OutputDeviceId ?? "";
    }

    private void OnOutputChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputCombo.SelectedItem is not AudioOutput chosen || chosen.Id == (working.Music.OutputDeviceId ?? ""))
        {
            return; // keeps the saved name of a device that isn't plugged in right now
        }

        working.Music.OutputDeviceId = chosen.Id == "" ? null : chosen.Id;
        working.Music.OutputDeviceName = chosen.Id == "" ? null : chosen.Name;
    }

    /// <summary>Lists the installed soundtracks in the background, again only if the Steam folder changes.</summary>
    private async void UpdateMusicStatus()
    {
        var key = working.SteamFolderOverride ?? "";
        if (key == musicFolderKey)
        {
            return;
        }

        musicFolderKey = key;
        MusicStatus.Text = "Looking for Steam soundtracks…";
        var snapshot = working.Clone();
        var scan = await Task.Run(() => SteamSoundtracks.Scan(snapshot));
        if (key == musicFolderKey) // otherwise a newer lookup has started
        {
            MusicStatus.Text = scan.Status;
        }
    }

    /// <summary>The first time, reading thousands of image headers takes a few seconds, so do it off the UI thread.</summary>
    private async void MeasureCoversThenUpdate(IReadOnlyList<GameEntry> games)
    {
        if (measuringCovers)
        {
            return;
        }

        measuringCovers = true;
        try
        {
            coverSizes = await Task.Run(() =>
            {
                var sizes = CoverSizeCache.LoadDefault();
                sizes.Measure(games.Select(g => g.CoverPath));
                sizes.Save(); // the screensaver itself then starts fast
                return sizes;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Measuring cover sizes failed", ex);
            coverSizes = CoverSizeCache.Load(null); // fall back to reading headers on demand
        }
        finally
        {
            measuringCovers = false;
        }

        UpdateSummary();
    }

    private static string DescribeReason(ExclusionReason reason, CoverShape shape, LibrarySource source) => reason switch
    {
        ExclusionReason.Hidden => $"hidden in {source}",
        ExclusionReason.NotInstalled => "not installed",
        ExclusionReason.NotFavorite => "not favorites",
        ExclusionReason.HideTag => "tagged to hide",
        ExclusionReason.NoCover => "without cover art",
        ExclusionReason.AdultContent => "by the nudity/sexual content filter",
        ExclusionReason.MatureRating => "rated Mature/18+",
        ExclusionReason.NotSelectedContent => "by the \"show only\" content filter",
        ExclusionReason.WrongShape => shape switch
        {
            CoverShape.Vertical => "with horizontal or square cover art",
            CoverShape.Square => "without square cover art",
            _ => "without horizontal cover art",
        },
        _ => reason.ToString(),
    };

    private void OnResetKeywordsClick(object sender, RoutedEventArgs e)
    {
        AdultKeywordsBox.Text = string.Join(Environment.NewLine, FilterSettings.DefaultAdultKeywords);
        MatureKeywordsBox.Text = string.Join(Environment.NewLine, FilterSettings.DefaultMatureKeywords);
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Library export (*.json)|*.json",
            InitialDirectory = AppPaths.DataFolder,
        };
        if (dialog.ShowDialog(this) == true)
        {
            LibraryPathBox.Text = dialog.FileName;
        }
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSourcePanels();
        ScheduleSummary(sender, e);
        UpdateMosaicRows(working.MosaicColumns); // Steam's landscape covers are wider than Playnite's
    }

    /// <summary>Show the location setting for the chosen source only.</summary>
    private void UpdateSourcePanels()
    {
        var steam = SourceCombo.SelectedValue is LibrarySource.Steam;
        SteamPanel.Visibility = steam ? Visibility.Visible : Visibility.Collapsed;
        PlaynitePanel.Visibility = steam ? Visibility.Collapsed : Visibility.Visible;
        // Every Steam game's play time comes from Steam, so there's nothing to leave out.
        SkipLabel.Visibility = SkipPanel.Visibility = PlaynitePanel.Visibility;
    }

    private void OnBrowseSteamClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder Steam is installed in",
            InitialDirectory = SteamLibrary.FindSteamFolder() ?? "",
        };
        if (dialog.ShowDialog(this) == true)
        {
            SteamPathBox.Text = dialog.FolderName;
        }
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        CommitKeywordLists();
        var snapshot = working.Clone().Sanitize();
        Hide();
        ScreensaverSession.Start(snapshot, windowed: false, onExit: () =>
        {
            Show();
            Activate();
        });
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        CommitKeywordLists();
        try
        {
            SettingsStore.Save(working);
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
            MessageBox.Show(this, "Couldn't save settings:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Close();
    }

    private void OnCheckNowClick(object sender, RoutedEventArgs e) => _ = CheckForUpdatesAsync(userAsked: true);

    /// <summary>Shows whether the daily update task exists. It's set up by the installer, so it's per PC, not in settings.json.</summary>
    private void ShowAutoUpdateState()
    {
        var enabled = Updates.AutoUpdateService.IsEnabled();
        AutoUpdateCheck.IsEnabled = enabled != null;
        AutoUpdateCheck.IsChecked = enabled == true;
        AutoUpdateNote.Text = enabled == null
            ? "Windows didn't say whether automatic updates are on. Run the installer again to change it."
            : "Changing this asks for administrator permission. The Playnite add-on updates through Playnite.";
    }

    private void OnAutoUpdateClick(object sender, RoutedEventArgs e)
    {
        var wanted = AutoUpdateCheck.IsChecked == true;
        Updates.AutoUpdateService.SetEnabledAsAdministrator(wanted);
        ShowAutoUpdateState(); // shows what actually happened, e.g. unchanged if they said no to the prompt
    }

    /// <summary>Asks GitHub for the latest release and shows the banner if it's newer. Quietly gives up if offline.</summary>
    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        UpdateStatus.Text = "Checking for updates…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            update = await UpdateCheck.CheckAsync(CurrentVersion, Http, timeout.Token);
            if (update == null)
            {
                UpdateStatus.Text = "You have the latest version.";
                return;
            }

            UpdateStatus.Text = $"Version {update.Version} is available.";
            UpdateBannerText.Text = $"Version {update.Version} is available (you have {CurrentVersion.ToString(3)}). ";
            UpdateNotesLink.NavigateUri = new Uri(update.ReleaseUrl);
            UpdateNowButton.Content = update.InstallerUrl != null ? "Update now" : "Download";
            UpdateBanner.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or UriFormatException)
        {
            if (userAsked)
            {
                Log.Error("Update check failed", ex);
            }

            UpdateStatus.Text = "Couldn't check for updates" + (ex is TaskCanceledException ? " (no answer from GitHub)." : ": " + ex.Message);
        }
    }

    /// <summary>
    /// Downloads the new installer and runs it. Settings are saved first, because the installer closes this window
    /// (it can't replace the .scr while it's running).
    /// </summary>
    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        if (update?.InstallerUrl == null)
        {
            Process.Start(new ProcessStartInfo(update?.ReleaseUrl ?? RepositoryUrl + "/releases/latest") { UseShellExecute = true });
            return;
        }

        UpdateNowButton.IsEnabled = false;
        var path = Path.Combine(Path.GetTempPath(), update.InstallerName ?? "PCGameCoverArtSetup.exe");
        try
        {
            using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(path);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    UpdateNowButton.Content = total > 0 ? $"Downloading… {done * 100 / total}%" : $"Downloading… {done / 1_048_576} MB";
                }
            }

            CommitKeywordLists();
            SettingsStore.Save(working);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); // asks for administrator permission
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // "No" on the administrator prompt: nothing changed, so let them try again later.
            UpdateNowButton.Content = "Update now";
            UpdateNowButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or Win32Exception or TaskCanceledException)
        {
            Log.Error("Update failed", ex);
            UpdateNowButton.Content = "Update now";
            UpdateNowButton.IsEnabled = true;
            MessageBox.Show(this, "Couldn't download or start the update:\n" + ex.Message +
                "\n\nYou can download it yourself from the project's Releases page.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnLicenseClick(object sender, RoutedEventArgs e) =>
        TextViewerWindow.ShowEmbedded(this, "MIT License", "LICENSE");

    private void OnNoticesClick(object sender, RoutedEventArgs e) =>
        TextViewerWindow.ShowEmbedded(this, "Third-party notices", "THIRD-PARTY-NOTICES.md");

    private void OnLinkClick(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.IsFile)
        {
            Directory.CreateDirectory(e.Uri.LocalPath);
        }

        var target = e.Uri.IsFile ? e.Uri.LocalPath : e.Uri.AbsoluteUri;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        e.Handled = true;
    }
}
