using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
using System.Windows.Threading;
using CoverArtSaver.Core;
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
    private string? loadedLibraryPath;
    private CoverSizeCache? coverSizes;
    private bool measuringCovers;

    public SettingsWindow(SaverSettings settings)
    {
        InitializeComponent();

        working = settings.Clone(); // edit a copy so Cancel really cancels
        DataContext = working;

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

        Loaded += (_, _) =>
        {
            UpdateSummary();
            UpdateLayoutSections();
            UpdateMosaicRows(working.MosaicColumns);
        };
    }

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e) => UpdateLayoutSections();

    private void OnShapeChanged(object sender, SelectionChangedEventArgs e)
    {
        ScheduleSummary(sender, e);
        UpdateMosaicRows(working.MosaicColumns); // mosaic tiles take the shape of the chosen covers
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
            CoverShapes.TileAspect(working.Filter.CoverShape));
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
        var path = working.LibraryFile;

        if (!File.Exists(path))
        {
            library = null;
            loadedLibraryPath = null;
            LibraryStatus.Text = $"⚠ No export found at {path}.\nInstall the \"PC Game Cover Art Exporter\" add-on in Playnite and restart Playnite.";
            FilterSummary.Text = "No games to preview yet.";
            return;
        }

        try
        {
            if (library == null || loadedLibraryPath != path)
            {
                library = LibraryData.Load(path);
                loadedLibraryPath = path;
            }
        }
        catch (Exception ex)
        {
            library = null;
            LibraryStatus.Text = "⚠ Couldn't read the export: " + ex.Message;
            FilterSummary.Text = "";
            return;
        }

        var age = DateTime.UtcNow - library.ExportedAt;
        LibraryStatus.Text = $"✔ {library.Games.Count} games, exported {Describe(age)} ago.";

        if (working.Filter.CoverShape != CoverShape.All && coverSizes == null)
        {
            MeasureCoversThenUpdate(library.Games);
            FilterSummary.Text = "Checking the shape of your cover art…";
            return;
        }

        var result = new GameFilter(working.Filter, coverAspect: coverSizes == null ? null : coverSizes.GetAspect).Apply(library.Games);
        var reasons = result.ExcludedCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Value} {DescribeReason(kv.Key, working.Filter.CoverShape)}");
        FilterSummary.Text =
            $"{result.Included.Count} of {library.Games.Count} games will appear in the screensaver." +
            (result.TotalExcluded > 0 ? "\nHidden: " + string.Join(", ", reasons) + "." : "");
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

    private static string Describe(TimeSpan age) =>
        age.TotalMinutes < 1 ? "just now"
        : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min"
        : age.TotalDays < 1 ? $"{(int)age.TotalHours} h"
        : $"{(int)age.TotalDays} days";

    private static string DescribeReason(ExclusionReason reason, CoverShape shape) => reason switch
    {
        ExclusionReason.Hidden => "hidden in Playnite",
        ExclusionReason.NotInstalled => "not installed",
        ExclusionReason.NotFavorite => "not favorites",
        ExclusionReason.HideTag => "tagged to hide",
        ExclusionReason.NoCover => "without cover art",
        ExclusionReason.AdultContent => "by the nudity/sexual content filter",
        ExclusionReason.MatureRating => "rated Mature/18+",
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
