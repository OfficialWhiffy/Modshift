using System.Text;

namespace TruckModsListUpdater.Core.Platform;

/// <summary>
/// Minimal reader for Valve's text KeyValues format (libraryfolders.vdf, loginusers.vdf,
/// appmanifest_*.acf). Keys are case-insensitive, as Steam treats them.
/// </summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string key] => Values.GetValueOrDefault(key);
    public VdfNode? Child(string key) => Children.GetValueOrDefault(key);

    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        return ReadBody(tokens);
    }

    public static VdfNode? TryLoad(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static VdfNode ReadBody(IEnumerator<string?> tokens)
    {
        var node = new VdfNode();
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key == null) break; // "}"
            if (!tokens.MoveNext()) break;
            if (tokens.Current == "{") node.Children[key] = ReadBody(tokens);
            else if (tokens.Current != null) node.Values[key] = tokens.Current;
            else break;
        }
        return node;
    }

    /// <summary>Yields strings, "{" for an opening brace and null for a closing one.</summary>
    static IEnumerable<string?> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '{') { i++; yield return "{"; continue; }
            if (c == '}') { i++; yield return null; continue; }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
                    }
                    else sb.Append(text[i]);
                    i++;
                }
                i++;
                yield return sb.ToString();
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"')) i++;
            yield return text[start..i];
        }
    }
}
