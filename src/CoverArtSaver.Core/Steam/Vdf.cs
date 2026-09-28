using System.Text;

namespace CoverArtSaver.Core.Steam;

/// <summary>
/// A node of Valve's KeyValues ("VDF") format: either a string value or a set of named children.
/// Keys are case-insensitive, as they are in Steam. Missing keys return an empty node, so lookups
/// like <c>node["apps"]["70"]["LastPlayed"].Value</c> never throw.
/// </summary>
public sealed class VdfNode
{
    public static readonly VdfNode Empty = new();

    private readonly Dictionary<string, VdfNode>? children;

    public string? Value { get; }

    public VdfNode(string value) => Value = value;

    public VdfNode() => children = new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase);

    public VdfNode this[string key] => children != null && children.TryGetValue(key, out var child) ? child : Empty;

    /// <summary>File order isn't preserved; nothing we read depends on it.</summary>
    public IEnumerable<KeyValuePair<string, VdfNode>> Children =>
        children ?? Enumerable.Empty<KeyValuePair<string, VdfNode>>();

    public long? AsLong() => long.TryParse(Value, out var n) ? n : null;

    internal void Add(string key, VdfNode value)
    {
        // Later duplicates win, which is how Steam treats them.
        children![key] = value;
    }
}

/// <summary>Reads text VDF files such as libraryfolders.vdf, appmanifest_*.acf and localconfig.vdf.</summary>
public static class Vdf
{
    public static VdfNode Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Returns a root node holding the file's top-level key(s). Malformed input yields what was read so far.</summary>
    public static VdfNode Parse(string text)
    {
        var root = new VdfNode();
        var position = 0;
        ReadChildren(text, ref position, root);
        return root;
    }

    private static void ReadChildren(string text, ref int position, VdfNode parent)
    {
        while (true)
        {
            var key = NextToken(text, ref position, out var keyKind);
            if (keyKind == TokenKind.End || keyKind == TokenKind.Close)
            {
                return; // end of file, or end of this block
            }

            if (keyKind == TokenKind.Open)
            {
                continue; // stray brace: skip it
            }

            var value = NextToken(text, ref position, out var valueKind);
            if (valueKind == TokenKind.String)
            {
                parent.Add(key!, new VdfNode(value!));
            }
            else if (valueKind == TokenKind.Open)
            {
                var child = new VdfNode();
                ReadChildren(text, ref position, child);
                parent.Add(key!, child);
            }
            else
            {
                return;
            }
        }
    }

    private enum TokenKind { String, Open, Close, End }

    private static string? NextToken(string text, ref int i, out TokenKind kind)
    {
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++; // comment to end of line
                }
            }
            else if (c == '[')
            {
                while (i < text.Length && text[i] != ']')
                {
                    i++; // platform conditional like [$WIN32]: ignore
                }

                i++;
            }
            else
            {
                break;
            }
        }

        if (i >= text.Length)
        {
            kind = TokenKind.End;
            return null;
        }

        switch (text[i])
        {
            case '{':
                i++;
                kind = TokenKind.Open;
                return null;
            case '}':
                i++;
                kind = TokenKind.Close;
                return null;
            case '"':
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
                    }
                    else
                    {
                        sb.Append(text[i]);
                    }

                    i++;
                }

                i++; // closing quote
                kind = TokenKind.String;
                return sb.ToString();
            default:
                // Unquoted token: runs to whitespace or a brace.
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                {
                    i++;
                }

                kind = TokenKind.String;
                return text[start..i];
        }
    }
}
