using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>A decoded cover and its pre-baked reflection.</summary>
internal sealed record CoverTexture(BitmapSource Front, BitmapSource? Reflection)
{
    /// <summary>Width ÷ height of the cover art.</summary>
    public double Aspect => Front.PixelHeight == 0 ? 0.7 : (double)Front.PixelWidth / Front.PixelHeight;
}

/// <summary>
/// Loads cover images in the background so the animation never stutters, and keeps only the
/// covers near the centre in memory (a big library can have thousands of covers).
/// </summary>
internal sealed class CoverTextureCache
{
    /// <summary>Fraction of the cover height mirrored below it.</summary>
    public const double ReflectionFraction = 0.45;

    private readonly int textureHeight;
    private readonly bool makeReflections;
    private readonly Dispatcher dispatcher;
    private readonly Dictionary<string, CoverTexture> loaded = [];
    private readonly HashSet<string> pending = [];
    private readonly SemaphoreSlim decodeSlots = new(2); // decode at most 2 images at once

    /// <summary>Raised on the UI thread when a game's texture becomes available.</summary>
    public event Action<string, CoverTexture>? TextureReady;

    public CoverTextureCache(int textureHeight, bool makeReflections, Dispatcher dispatcher)
    {
        this.textureHeight = textureHeight;
        this.makeReflections = makeReflections;
        this.dispatcher = dispatcher;
    }

    /// <summary>Returns the texture if it's ready; otherwise starts loading it and returns null.</summary>
    public CoverTexture? Request(GameEntry game)
    {
        if (loaded.TryGetValue(game.Id, out var texture))
        {
            return texture;
        }

        if (pending.Add(game.Id))
        {
            _ = LoadAsync(game);
        }

        return null;
    }

    /// <summary>Drops textures for games that aren't near the centre any more.</summary>
    public void Trim(ICollection<string> keepIds, int capacity)
    {
        if (loaded.Count <= capacity)
        {
            return;
        }

        foreach (var id in loaded.Keys.Where(id => !keepIds.Contains(id)).ToList())
        {
            loaded.Remove(id);
        }
    }

    private async Task LoadAsync(GameEntry game)
    {
        CoverTexture? texture = null;
        await decodeSlots.WaitAsync();
        try
        {
            // WPF bitmaps that are Frozen can be created on a worker thread and handed to the UI thread.
            texture = await Task.Run(() => Decode(game.CoverPath));
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't load cover for '{game.Name}' ({game.CoverPath})", ex);
        }
        finally
        {
            decodeSlots.Release();
        }

        await dispatcher.InvokeAsync(() =>
        {
            // No usable image file: draw a simple title card instead (must happen on the UI thread).
            texture ??= new CoverTexture(TitleCard.Render(game.Name), null);
            if (makeReflections && texture.Reflection == null)
            {
                texture = texture with { Reflection = MakeReflection(texture.Front) };
            }

            pending.Remove(game.Id);
            loaded[game.Id] = texture;
            TextureReady?.Invoke(game.Id, texture);
        });
    }

    private CoverTexture? Decode(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;              // read everything now so the file isn't kept open
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelHeight = textureHeight;                   // decode straight to the size we need: big memory saver
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return new CoverTexture(image, makeReflections ? MakeReflection(image) : null);
    }

    /// <summary>
    /// Builds the "wet floor" reflection by flipping the bottom part of the cover and darkening it
    /// towards black. Doing this once per image with raw pixels is far cheaper than using opacity
    /// masks in the 3D scene, and keeping it opaque avoids transparency-sorting glitches.
    /// </summary>
    internal static BitmapSource MakeReflection(BitmapSource source, double startBrightness = 0.35)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var reflectionHeight = Math.Max(1, (int)(height * ReflectionFraction));

        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        var output = new byte[stride * reflectionHeight];
        for (var y = 0; y < reflectionHeight; y++)
        {
            var sourceRow = height - 1 - y;                      // flip vertically
            var fade = 1.0 - (double)y / reflectionHeight;
            var brightness = startBrightness * fade * fade;      // quadratic fall-off looks natural
            for (var x = 0; x < stride; x += 4)
            {
                var s = sourceRow * stride + x;
                var d = y * stride + x;
                output[d] = (byte)(pixels[s] * brightness);
                output[d + 1] = (byte)(pixels[s + 1] * brightness);
                output[d + 2] = (byte)(pixels[s + 2] * brightness);
                output[d + 3] = 255;
            }
        }

        var reflection = BitmapSource.Create(width, reflectionHeight, 96, 96, PixelFormats.Bgra32, null, output, stride);
        reflection.Freeze();
        return reflection;
    }
}

/// <summary>Generates a plain cover for games that have no artwork.</summary>
internal static class TitleCard
{
    public static BitmapSource Render(string title, int width = 400, int height = 600)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(0x3a, 0x3f, 0x4b), Color.FromRgb(0x14, 0x16, 0x1c), 90);
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));

            var text = new FormattedText(
                string.IsNullOrWhiteSpace(title) ? "Untitled" : title,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                44,
                Brushes.White,
                1.0)
            {
                MaxTextWidth = width - 60,
                MaxTextHeight = height - 60,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            dc.DrawText(text, new Point(30, (height - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
