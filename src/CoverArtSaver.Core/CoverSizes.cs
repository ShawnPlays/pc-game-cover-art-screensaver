using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CoverArtSaver.Core;

public readonly record struct ImageSize(int Width, int Height)
{
    /// <summary>Width ÷ height: below 1 is portrait (vertical), above 1 is landscape.</summary>
    public double Aspect => (double)Width / Height;
}

/// <summary>
/// Reads an image's pixel size from the first few bytes of the file, without decoding the picture.
/// Recognises the file by its contents rather than its extension (Playnite sometimes saves odd ones).
/// </summary>
public static class ImageHeader
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageSize? ReadSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096);
            return ReadSize(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns null for unrecognised or truncated images. The stream must be seekable.</summary>
    public static ImageSize? ReadSize(Stream stream)
    {
        try
        {
            Span<byte> buffer = stackalloc byte[32];
            ReadOnlySpan<byte> h = buffer[..ReadAtMost(stream, buffer)];

            if (h.Length >= 24 && h.StartsWith(PngSignature))
            {
                return Size(BinaryPrimitives.ReadInt32BigEndian(h[16..]), BinaryPrimitives.ReadInt32BigEndian(h[20..]));
            }

            if (h.Length >= 10 && h.StartsWith("GIF8"u8))
            {
                return Size(BinaryPrimitives.ReadUInt16LittleEndian(h[6..]), BinaryPrimitives.ReadUInt16LittleEndian(h[8..]));
            }

            if (h.Length >= 26 && h.StartsWith("BM"u8))
            {
                // Old OS/2 bitmaps use 16-bit sizes; everything since uses 32-bit, with negative height meaning top-down.
                return BinaryPrimitives.ReadUInt32LittleEndian(h[14..]) == 12
                    ? Size(BinaryPrimitives.ReadUInt16LittleEndian(h[18..]), BinaryPrimitives.ReadUInt16LittleEndian(h[20..]))
                    : Size(BinaryPrimitives.ReadInt32LittleEndian(h[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h[22..])));
            }

            if (h.Length >= 30 && h.StartsWith("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
            {
                return ReadWebP(h);
            }

            if (h.Length >= 2 && h[0] == 0xFF && h[1] == 0xD8)
            {
                stream.Position = 2;
                return ReadJpeg(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException)
        {
            // Corrupt header: treat it like an unknown format.
        }

        return null;
    }

    private static ImageSize? ReadWebP(ReadOnlySpan<byte> h)
    {
        var chunk = h[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Lossy: 14-bit sizes after the key-frame start code.
            return Size(BinaryPrimitives.ReadUInt16LittleEndian(h[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(h[28..]) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // Lossless: two 14-bit (size - 1) fields packed into 4 bytes after a 0x2F signature byte.
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(h[21..]);
            return Size((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Extended: 24-bit (size - 1) canvas fields.
            return Size(ReadUInt24(h[24..]) + 1, ReadUInt24(h[27..]) + 1);
        }

        return null;
    }

    /// <summary>Walks the JPEG's segments until the frame header, skipping EXIF/thumbnails without reading them.</summary>
    private static ImageSize? ReadJpeg(Stream stream)
    {
        Span<byte> segment = stackalloc byte[5];
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return null;
            }

            if (b != 0xFF)
            {
                continue;
            }

            int marker;
            do
            {
                marker = stream.ReadByte(); // any number of 0xFF fill bytes may precede a marker
            }
            while (marker == 0xFF);

            if (marker < 0 || marker == 0xD9 || marker == 0xDA)
            {
                return null; // end of file, or image data began before any frame header
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
            {
                continue; // markers without a length field
            }

            if (ReadAtMost(stream, segment[..2]) < 2)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(segment);
            if (length < 2)
            {
                return null;
            }

            // SOF0–SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                // Segment: precision (1 byte), height (2), width (2).
                return ReadAtMost(stream, segment) < 5
                    ? null
                    : Size(BinaryPrimitives.ReadUInt16BigEndian(segment[3..]), BinaryPrimitives.ReadUInt16BigEndian(segment[1..]));
            }

            stream.Seek(length - 2, SeekOrigin.Current);
        }
    }

    private static ImageSize? Size(int width, int height) =>
        width > 0 && height > 0 ? new ImageSize(width, height) : null;

    private static int ReadUInt24(ReadOnlySpan<byte> b) => b[0] | (b[1] << 8) | (b[2] << 16);

    private static int ReadAtMost(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}

/// <summary>
/// Remembers each cover's pixel size between runs, so the "vertical covers only" filter doesn't re-read
/// thousands of image headers every time the screensaver starts. An entry is re-read if the file's size or
/// date changes. Saved as %LOCALAPPDATA%\PCGameCoverArt\cover-sizes.json.
/// </summary>
public sealed class CoverSizeCache
{
    private readonly string? path;
    private readonly ConcurrentDictionary<string, Entry> entries;
    private volatile bool dirty;

    /// <summary>A zero size means "not a readable image".</summary>
    public sealed record Entry(int Width, int Height, long Length, long Modified);

    private CoverSizeCache(string? path, IDictionary<string, Entry> entries)
    {
        this.path = path;
        this.entries = new ConcurrentDictionary<string, Entry>(entries, StringComparer.OrdinalIgnoreCase);
    }

    /// <param name="path">Where to persist; null keeps the cache in memory only.</param>
    public static CoverSizeCache Load(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path));
                if (saved != null)
                {
                    return new CoverSizeCache(path, saved);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the cover size cache; rebuilding it.", ex);
        }

        return new CoverSizeCache(path, new Dictionary<string, Entry>());
    }

    public static CoverSizeCache LoadDefault() => Load(AppPaths.CoverSizesFile);

    /// <summary>Reads every cover that isn't cached yet, several at a time. Call once before filtering a big library.</summary>
    public void Measure(IEnumerable<string?> coverPaths)
    {
        var paths = coverPaths.OfType<string>().Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 8 }, p => GetAspect(p));
    }

    /// <summary>Width ÷ height of the image, or null if it's missing or not a recognised image.</summary>
    public double? GetAspect(string coverPath)
    {
        var info = new FileInfo(coverPath);
        if (!info.Exists)
        {
            return null;
        }

        var modified = info.LastWriteTimeUtc.Ticks;
        if (!entries.TryGetValue(coverPath, out var entry) || entry.Length != info.Length || entry.Modified != modified)
        {
            var size = ImageHeader.ReadSize(coverPath);
            entry = new Entry(size?.Width ?? 0, size?.Height ?? 0, info.Length, modified);
            entries[coverPath] = entry;
            dirty = true;
        }

        return entry.Width > 0 && entry.Height > 0 ? (double)entry.Width / entry.Height : null;
    }

    public void Save()
    {
        if (!dirty || path == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, Entry>(entries)));
            dirty = false;
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the cover size cache.", ex);
        }
    }
}
