using CoverArtSaver.Core;

namespace CoverArtSaver.Tests;

public class ImageHeaderTests
{
    private static ImageSize? Read(byte[] bytes) => ImageHeader.ReadSize(new MemoryStream(bytes));

    private static byte[] PngBytes(int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BitConverter.GetBytes(width).Reverse().ToArray().CopyTo(b, 16);
        BitConverter.GetBytes(height).Reverse().ToArray().CopyTo(b, 20);
        return b;
    }

    [Fact]
    public void Png() => Assert.Equal(new ImageSize(600, 900), Read(PngBytes(600, 900)));

    [Fact]
    public void Gif() => Assert.Equal(new ImageSize(460, 215),
        Read([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0xCC, 0x01, 0xD7, 0x00, 0, 0]));

    [Fact]
    public void Bmp()
    {
        var b = new byte[40];
        b[0] = (byte)'B';
        b[1] = (byte)'M';
        BitConverter.GetBytes(40).CopyTo(b, 14);   // BITMAPINFOHEADER
        BitConverter.GetBytes(300).CopyTo(b, 18);
        BitConverter.GetBytes(-450).CopyTo(b, 22); // top-down
        Assert.Equal(new ImageSize(300, 450), Read(b));
    }

    [Fact]
    public void JpegSkipsExifBeforeFrameHeader()
    {
        var exif = new byte[500];
        byte[] jpeg =
        [
            0xFF, 0xD8,                                  // start of image
            0xFF, 0xE1, 0x01, 0xF6, .. exif,             // APP1 (EXIF), length 502
            0xFF, 0xC4, 0x00, 0x04, 0x00, 0x00,          // DHT: in the SOF range but not a frame header
            0xFF, 0xFF, 0xC2, 0x00, 0x11, 0x08,          // fill byte, then progressive SOF2 with precision 8
            0x03, 0x84,                                  // height 900
            0x02, 0x58,                                  // width 600
            0x03, 0, 0, 0,
        ];
        Assert.Equal(new ImageSize(600, 900), Read(jpeg));
    }

    [Fact]
    public void WebPLossy()
    {
        var b = WebP("VP8 ");
        BitConverter.GetBytes((ushort)600).CopyTo(b, 26);
        BitConverter.GetBytes((ushort)900).CopyTo(b, 28);
        Assert.Equal(new ImageSize(600, 900), Read(b));
    }

    [Fact]
    public void WebPLossless()
    {
        var b = WebP("VP8L");
        b[20] = 0x2F;
        BitConverter.GetBytes((uint)((600 - 1) | ((900 - 1) << 14))).CopyTo(b, 21);
        Assert.Equal(new ImageSize(600, 900), Read(b));
    }

    [Fact]
    public void WebPExtended()
    {
        var b = WebP("VP8X");
        BitConverter.GetBytes(1920 - 1).AsSpan(0, 3).CopyTo(b.AsSpan(24));
        BitConverter.GetBytes(1080 - 1).AsSpan(0, 3).CopyTo(b.AsSpan(27));
        Assert.Equal(new ImageSize(1920, 1080), Read(b));
    }

    private static byte[] WebP(string chunk)
    {
        var b = new byte[40];
        "RIFF"u8.CopyTo(b);
        "WEBP"u8.CopyTo(b.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(b, 12);
        return b;
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })]  // unknown format
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 })]         // truncated JPEG
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08 })]   // image data before any frame header
    public void UnreadableImagesReturnNull(byte[] bytes) => Assert.Null(Read(bytes));

    [Fact]
    public void CacheRereadsChangedFiles()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cover = Path.Combine(dir, "cover.png");
            var cacheFile = Path.Combine(dir, "sizes.json");
            File.WriteAllBytes(cover, PngBytes(600, 900));

            var cache = CoverSizeCache.Load(cacheFile);
            cache.Measure([cover, null, "", Path.Combine(dir, "missing.png")]);
            Assert.Equal(600.0 / 900, cache.GetAspect(cover));
            Assert.Null(cache.GetAspect(Path.Combine(dir, "missing.png")));
            cache.Save();

            // Replace the image with a landscape one (different length, so it's detected even if the timestamp is equal).
            File.WriteAllBytes(cover, [.. PngBytes(1920, 1080), 0]);
            Assert.Equal(1920.0 / 1080, CoverSizeCache.Load(cacheFile).GetAspect(cover));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class CoverShapeFilterTests
{
    private const string Box = @"C:\covers\box.jpg";
    private const string Banner = @"C:\covers\banner.jpg";
    private const string Square = @"C:\covers\square.jpg";
    private const string Broken = @"C:\covers\broken.jpg";

    private static readonly Dictionary<string, double?> Aspects = new()
    {
        [Box] = 600.0 / 900,
        [Banner] = 630.0 / 500,
        [Square] = 512.0 / 500,
        [Broken] = null,
    };

    private static ExclusionReason Evaluate(string? cover, CoverShape shape) =>
        new GameFilter(new FilterSettings { CoverShape = shape, SkipGamesWithoutCover = false },
                fileExists: _ => true, coverAspect: p => Aspects[p])
            .Evaluate(new GameEntry { Id = "1", Name = "x", CoverPath = cover });

    [Theory]
    [InlineData(Box, CoverShape.All, true)]
    [InlineData(Banner, CoverShape.All, true)]
    [InlineData(Square, CoverShape.All, true)]
    [InlineData(Box, CoverShape.Vertical, true)]
    [InlineData(Banner, CoverShape.Vertical, false)]
    [InlineData(Square, CoverShape.Vertical, false)]
    [InlineData(Box, CoverShape.Square, false)]
    [InlineData(Banner, CoverShape.Square, false)]
    [InlineData(Square, CoverShape.Square, true)]
    [InlineData(Box, CoverShape.Horizontal, false)]
    [InlineData(Banner, CoverShape.Horizontal, true)]
    [InlineData(Square, CoverShape.Horizontal, false)]
    public void KeepsOnlyTheChosenShape(string cover, CoverShape shape, bool shown) =>
        Assert.Equal(shown ? ExclusionReason.None : ExclusionReason.WrongShape, Evaluate(cover, shape));

    [Theory]
    [InlineData(CoverShape.Vertical, true)]
    [InlineData(CoverShape.Square, false)]
    [InlineData(CoverShape.Horizontal, false)]
    public void GamesWithoutUsableArtCountAsVertical(CoverShape shape, bool shown)
    {
        // They're drawn as a vertical title card.
        var expected = shown ? ExclusionReason.None : ExclusionReason.WrongShape;
        Assert.Equal(expected, Evaluate(null, shape));
        Assert.Equal(expected, Evaluate(Broken, shape));
    }

    [Theory]
    [InlineData(0.667, CoverShape.Vertical)]
    [InlineData(0.899, CoverShape.Vertical)]
    [InlineData(0.9, CoverShape.Square)]
    [InlineData(1.0, CoverShape.Square)]
    [InlineData(1.11, CoverShape.Square)]
    [InlineData(1.12, CoverShape.Horizontal)]
    [InlineData(1.26, CoverShape.Horizontal)]
    [InlineData(2.14, CoverShape.Horizontal)]
    public void Classify(double aspect, CoverShape expected) => Assert.Equal(expected, CoverShapes.Classify(aspect));

    [Fact]
    public void AllByDefault() => Assert.Equal(CoverShape.All, new FilterSettings().CoverShape);

    [Fact]
    public void ShowTagDoesNotOverrideIt()
    {
        var filter = new GameFilter(new FilterSettings { CoverShape = CoverShape.Vertical }, fileExists: _ => true, coverAspect: p => Aspects[p]);
        var game = new GameEntry { Id = "1", Name = "x", CoverPath = Banner, Tags = ["Screensaver: Show"] };
        Assert.Equal(ExclusionReason.WrongShape, filter.Evaluate(game));
    }

    [Theory]
    [InlineData(true, CoverShape.Vertical)]
    [InlineData(false, CoverShape.All)]
    public void UpgradesTheOldVerticalOnlySwitch(bool oldValue, CoverShape expected)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, $$"""{ "Filter": { "VerticalCoversOnly": {{(oldValue ? "true" : "false")}} } }""");
            var loaded = SettingsStore.Load(path);
            Assert.Equal(expected, loaded.Filter.CoverShape);

            SettingsStore.Save(loaded, path);
            var saved = File.ReadAllText(path);
            Assert.DoesNotContain("VerticalCoversOnly", saved);                       // the old switch isn't written back
            Assert.Contains($"\"CoverShape\": \"{expected}\"", saved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MosaicTilesTakeTheChosenShape()
    {
        Assert.Equal(4, MosaicLayout.Fit(10, 1920, 1080, CoverShapes.TileAspect(CoverShape.Vertical)).Rows);
        Assert.Equal(6, MosaicLayout.Fit(10, 1920, 1080, CoverShapes.TileAspect(CoverShape.Square)).Rows);
        Assert.Equal(7, MosaicLayout.Fit(10, 1920, 1080, CoverShapes.TileAspect(CoverShape.Horizontal)).Rows);
    }
}
