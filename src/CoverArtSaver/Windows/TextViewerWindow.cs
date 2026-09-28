using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CoverArtSaver.Windows;

/// <summary>A plain read-only text window, used to show the license texts embedded in the .scr.</summary>
internal sealed class TextViewerWindow : Window
{
    private TextViewerWindow(string title, string text)
    {
        Title = title;
        Width = 680;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(0),
        };
    }

    /// <param name="resourceName">LogicalName of an EmbeddedResource in CoverArtSaver.csproj.</param>
    public static void ShowEmbedded(Window owner, string title, string resourceName)
    {
        using var stream = typeof(TextViewerWindow).Assembly.GetManifestResourceStream(resourceName);
        var text = stream == null ? $"({resourceName} is missing from this build.)" : new StreamReader(stream).ReadToEnd();
        new TextViewerWindow(title, text) { Owner = owner }.ShowDialog();
    }
}
