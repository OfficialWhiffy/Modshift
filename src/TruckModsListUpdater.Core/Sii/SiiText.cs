using System.Text;

namespace TruckModsListUpdater.Core.Sii;

/// <summary>
/// Tolerant reader for text SII (manifest.sii, versions.sii, profile.sii, mods_info.sii).
/// Handles comments, quoted strings with escapes, tuples, arrays written as name[], name[N]
/// or with a count line, and units written on one line. Unknown syntax is skipped.
/// </summary>
public static class SiiText
{
    public static List<SiiUnit> Parse(string text)
    {
        var units = new List<SiiUnit>();
        var tokens = Tokenize(text).ToList();
        int i = 0;

        while (i < tokens.Count)
        {
            // Unit header: type ':' id '{'
            if (i + 3 < tokens.Count && tokens[i + 1].Is(':') && tokens[i + 3].Is('{') && !tokens[i].Quoted)
            {
                var unit = new SiiUnit(tokens[i].Text, tokens[i + 2].Text);
                i += 4;
                i = ReadBody(tokens, i, unit);
                units.Add(unit);
                continue;
            }
            i++; // "SiiNunit", "{", "}", "@include ..." and anything unexpected
        }
        return units;
    }

    static int ReadBody(List<Token> tokens, int i, SiiUnit unit)
    {
        while (i < tokens.Count)
        {
            if (tokens[i].Is('}')) return i + 1;
            if (i + 2 < tokens.Count && tokens[i + 1].Is(':') && !tokens[i].IsPunct && !tokens[i + 2].IsPunct)
            {
                Assign(unit, tokens[i].Text, tokens[i + 2].Text);
                i += 3;
                continue;
            }
            i++;
        }
        return i;
    }

    static void Assign(SiiUnit unit, string key, string value)
    {
        int bracket = key.IndexOf('[');
        if (bracket < 0)
        {
            // "name: N" is the array count line when an array of that name follows; keep it as a value too.
            unit.Values[key] = value;
            return;
        }
        string name = key[..bracket];
        if (!unit.Arrays.TryGetValue(name, out var list)) unit.Arrays[name] = list = new List<string>();
        list.Add(value);
    }

    readonly record struct Token(string Text, bool Quoted, bool IsPunct)
    {
        public bool Is(char c) => IsPunct && Text[0] == c;
    }

    static IEnumerable<Token> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#' || (c == '/' && Peek(text, i + 1) == '/'))
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && Peek(text, i + 1) == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                continue;
            }
            if (c is '{' or '}' or ':')
            {
                i++;
                yield return new Token(c.ToString(), false, true);
                continue;
            }
            if (c == '"')
            {
                i = SiiString.ReadQuoted(text, i, out var value);
                yield return new Token(value, true, false);
                continue;
            }
            if (c == '(')
            {
                int end = text.IndexOf(')', i);
                end = end < 0 ? text.Length : end + 1;
                yield return new Token(text[i..end], false, false);
                i = end;
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or ':' or '"')) i++;
            yield return new Token(text[start..i], false, false);
        }
    }

    static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';
}

/// <summary>SII string quoting: \" \\ \n \t and \xNN (UTF-8 bytes).</summary>
public static class SiiString
{
    /// <summary>Reads a quoted string starting at the opening quote; returns the index after the closing quote.</summary>
    public static int ReadQuoted(string text, int start, out string value)
    {
        var bytes = new List<byte>();
        Span<byte> buf = stackalloc byte[4];
        int i = start + 1;
        while (i < text.Length && text[i] != '"')
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                char e = text[i + 1];
                if (e == 'x' && i + 3 < text.Length && IsHex(text[i + 2]) && IsHex(text[i + 3]))
                {
                    bytes.Add(Convert.ToByte(text.Substring(i + 2, 2), 16));
                    i += 4;
                    continue;
                }
                bytes.Add((byte)(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e }));
                i += 2;
                continue;
            }
            // Copy the char (or surrogate pair) as UTF-8.
            int len = char.IsHighSurrogate(c) && i + 1 < text.Length ? 2 : 1;
            int n = Encoding.UTF8.GetBytes(text.AsSpan(i, len), buf);
            for (int k = 0; k < n; k++) bytes.Add(buf[k]);
            i += len;
        }
        value = Encoding.UTF8.GetString(bytes.ToArray());
        return Math.Min(i + 1, text.Length);
    }

    /// <summary>Quotes a value the way the game writes it: non-ASCII as \xNN UTF-8 escapes.</summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            if (b == '"') sb.Append("\\\"");
            else if (b == '\\') sb.Append("\\\\");
            else if (b == '\n') sb.Append("\\n");
            else if (b == '\t') sb.Append("\\t");
            else if (b < 0x20 || b >= 0x7F) sb.Append("\\x").Append(b.ToString("x2"));
            else sb.Append((char)b);
        }
        return sb.Append('"').ToString();
    }

    static bool IsHex(char c) => char.IsAsciiHexDigit(c);
}
