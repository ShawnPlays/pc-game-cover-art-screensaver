using System.Globalization;
using System.Text;

namespace CoverArtSaver.Core.Steam;

/// <summary>
/// Reads Steam's appcache\appinfo.vdf: a binary cache of every app the Steam client knows about. For each app it
/// holds a binary KeyValues tree whose "common" section has the name, type (game, dlc, tool...), genres, store tags,
/// content descriptors and release dates. Only the apps asked for are decoded; the rest are skipped by size.
/// Understands format versions 27 and 28 (keys stored inline) and 29 (keys in a shared string table).
/// </summary>
public static class SteamAppInfo
{
    private const uint Version27 = 0x07564427;
    private const uint Version28 = 0x07564428;
    private const uint Version29 = 0x07564429;

    // Binary KeyValues value types.
    private const byte TypeMap = 0, TypeString = 1, TypeInt32 = 2, TypeFloat = 3, TypePointer = 4,
        TypeWideString = 5, TypeColor = 6, TypeUInt64 = 7, TypeEnd = 8, TypeInt64 = 10, TypeAltEnd = 11;

    /// <summary>Reads the file into memory first, so Steam updating it meanwhile can't give us a torn read.</summary>
    public static Dictionary<uint, VdfNode> ReadCommon(string path, IReadOnlySet<uint> wanted)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var memory = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        file.CopyTo(memory);
        memory.Position = 0;
        return ReadCommon(memory, wanted);
    }

    /// <returns>Each wanted app's "common" section, keyed by app id. Apps missing from the file are left out.</returns>
    public static Dictionary<uint, VdfNode> ReadCommon(Stream stream, IReadOnlySet<uint> wanted)
    {
        var reader = new BinaryReader(stream, Encoding.UTF8);
        var magic = reader.ReadUInt32();
        if (magic is not (Version27 or Version28 or Version29))
        {
            throw new InvalidDataException($"Unrecognised appinfo.vdf format (0x{magic:X8}). Steam may have changed it.");
        }

        reader.ReadUInt32(); // universe
        string[]? strings = null;
        if (magic == Version29)
        {
            var tableOffset = reader.ReadInt64();
            var entriesStart = stream.Position;
            stream.Position = tableOffset;
            strings = new string[reader.ReadUInt32()];
            for (var i = 0; i < strings.Length; i++)
            {
                strings[i] = ReadCString(reader);
            }

            stream.Position = entriesStart;
        }

        // Before each app's KeyValues: state, last updated, PICS token, text SHA-1, change number (+ binary SHA-1 from v28).
        var entryHeader = magic == Version27 ? 40 : 60;
        var result = new Dictionary<uint, VdfNode>();
        while (stream.Position + 8 <= stream.Length)
        {
            var appId = reader.ReadUInt32();
            if (appId == 0)
            {
                break; // end marker
            }

            var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (wanted.Contains(appId) && size > entryHeader)
            {
                stream.Position += entryHeader;
                var tree = ReadMap(reader, strings);
                result[appId] = tree["appinfo"]["common"];
            }

            stream.Position = next;
        }

        return result;
    }

    private static VdfNode ReadMap(BinaryReader reader, string[]? strings)
    {
        var node = new VdfNode();
        while (true)
        {
            var type = reader.ReadByte();
            if (type is TypeEnd or TypeAltEnd)
            {
                return node;
            }

            var key = strings != null ? strings[reader.ReadInt32()] : ReadCString(reader);
            node.Add(key, type switch
            {
                TypeMap => ReadMap(reader, strings),
                TypeString => new VdfNode(ReadCString(reader)),
                TypeInt32 or TypePointer or TypeColor => new VdfNode(reader.ReadInt32().ToString(CultureInfo.InvariantCulture)),
                TypeFloat => new VdfNode(reader.ReadSingle().ToString(CultureInfo.InvariantCulture)),
                TypeUInt64 => new VdfNode(reader.ReadUInt64().ToString(CultureInfo.InvariantCulture)),
                TypeInt64 => new VdfNode(reader.ReadInt64().ToString(CultureInfo.InvariantCulture)),
                TypeWideString => new VdfNode(ReadWideString(reader)),
                _ => throw new InvalidDataException($"Unknown KeyValues type {type} in appinfo.vdf."),
            });
        }
    }

    private static string ReadCString(BinaryReader reader)
    {
        var bytes = new List<byte>(32);
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            bytes.Add(b);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string ReadWideString(BinaryReader reader)
    {
        var sb = new StringBuilder();
        char c;
        while ((c = (char)reader.ReadUInt16()) != '\0')
        {
            sb.Append(c);
        }

        return sb.ToString();
    }
}
