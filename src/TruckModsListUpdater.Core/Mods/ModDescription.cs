using System.Text;
using System.Text.RegularExpressions;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>The colours the game's mod manager knows in description files.</summary>
public enum DescriptionColor { Normal, Orange, Blue, Red, Green, White, Yellow, Purple }

/// <summary>A run of text with one style. <see cref="Link"/> is set for a clickable web address.</summary>
public sealed record DescriptionSpan(string Text, DescriptionColor Color = DescriptionColor.Normal, bool Bold = false,
    bool Italic = false, bool Underline = false, string? Link = null, string? Hex = null);

/// <summary>One line of a description. <see cref="Indent"/> is the list nesting level (0 = none).</summary>
public sealed record DescriptionLine(IReadOnlyList<DescriptionSpan> Spans, bool IsListItem = false, int Indent = 0, string? Bullet = null)
{
    public bool IsBlank => Spans.Count == 0;
}

/// <summary>
/// Turns the raw text of a mod's description file into styled lines. Mods use the game's own
/// markup: colour tags ("[orange]") that stay on until "[normal]" or another colour, plus a few
/// BBCode-style tags. Anything else in square brackets is ordinary text ("ETS2 [Trailer] Blade").
/// </summary>
public static partial class ModDescription
{
    public static IReadOnlyList<DescriptionLine> Parse(string? text)
    {
        var lines = new List<DescriptionLine>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        var style = new Style();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = ParseLine(raw.TrimEnd(), style);
            // Collapse runs of blank lines to one.
            if (line.IsBlank && (lines.Count == 0 || lines[^1].IsBlank)) continue;
            lines.Add(line);
        }
        while (lines.Count > 0 && lines[^1].IsBlank) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>Plain text without any markup, e.g. for copying or searching.</summary>
    public static string ToPlainText(IReadOnlyList<DescriptionLine> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => string.Concat(l.Spans.Select(s => s.Text))));

    sealed class Style
    {
        public DescriptionColor Color;
        public string? Hex;
        public bool Bold, Italic, Underline;
    }

    static DescriptionLine ParseLine(string raw, Style style)
    {
        int indent = 0;
        while (indent < raw.Length && raw[indent] == '\t') indent++;
        var body = raw.TrimStart('\t', ' ');

        // A colour tag at the start of a list line ("- [red]foo") is still a list item, so look
        // at the marker first and let the tag parser handle the rest.
        bool isList = false;
        string? bullet = null;
        if (body.Length > 0 && ListMarkers.Contains(body[0]) && (body.Length == 1 || body[1] == ' '))
        {
            isList = true;
            bullet = body[0] == '✔' ? "✔" : "•";
            body = body[1..].TrimStart();
        }

        var spans = new List<DescriptionSpan>();
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0) return;
            AddWithLinks(spans, buffer.ToString(), style);
            buffer.Clear();
        }

        for (int i = 0; i < body.Length;)
        {
            if (body[i] == '[' && TryReadTag(body, i, out var tag, out var length) && Apply(tag, style, Flush))
            {
                i += length;
                continue;
            }
            buffer.Append(body[i++]);
        }
        Flush();

        // A line holding only whitespace after its tags is blank ("[normal]" on its own line).
        if (spans.All(s => string.IsNullOrWhiteSpace(s.Text))) return new DescriptionLine([]);
        return new DescriptionLine(spans, isList, isList ? indent : 0, bullet);
    }

    const string ListMarkers = "-*•✔";

    static bool TryReadTag(string s, int start, out string tag, out int length)
    {
        var m = TagRegex().Match(s, start);
        if (m.Success && m.Index == start) { tag = m.Groups[1].Value; length = m.Length; return true; }
        tag = ""; length = 0;
        return false;
    }

    /// <summary>Applies a recognised tag to the style; returns false so unknown tags stay as text.</summary>
    static bool Apply(string tag, Style style, Action flush)
    {
        var t = tag.Trim().ToLowerInvariant();
        DescriptionColor? color = t switch
        {
            "normal" => DescriptionColor.Normal,
            "orange" => DescriptionColor.Orange,
            "blue" => DescriptionColor.Blue,
            "red" => DescriptionColor.Red,
            "green" => DescriptionColor.Green,
            "white" => DescriptionColor.White,
            "yellow" => DescriptionColor.Yellow,
            "purple" => DescriptionColor.Purple,
            _ => null,
        };
        if (color != null)
        {
            flush();
            style.Color = color.Value;
            style.Hex = null;
            return true;
        }

        var hex = HexColorRegex().Match(t);
        if (hex.Success)
        {
            flush();
            style.Hex = "#" + hex.Groups[1].Value;
            return true;
        }
        if (t is "/color" or "/colour") { flush(); style.Hex = null; return true; }

        switch (t)
        {
            case "b": flush(); style.Bold = true; return true;
            case "/b": flush(); style.Bold = false; return true;
            case "i": flush(); style.Italic = true; return true;
            case "/i": flush(); style.Italic = false; return true;
            case "u": flush(); style.Underline = true; return true;
            case "/u": flush(); style.Underline = false; return true;
            default: return false;
        }
    }

    static void AddWithLinks(List<DescriptionSpan> spans, string text, Style style)
    {
        int last = 0;
        foreach (Match m in UrlRegex().Matches(text))
        {
            // Sentence punctuation right after a link is not part of it.
            var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '\'', '"');
            if (m.Index > last) spans.Add(Make(text[last..m.Index], style, null));
            spans.Add(Make(url, style, url));
            last = m.Index + url.Length;
        }
        if (last < text.Length) spans.Add(Make(text[last..], style, null));
    }

    static DescriptionSpan Make(string text, Style s, string? link) =>
        new(text, s.Color, s.Bold, s.Italic, s.Underline || link != null, link, s.Hex);

    [GeneratedRegex(@"\[([^\[\]\r\n]{1,30})\]")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^(?:color|colour)\s*=\s*#?([0-9a-f]{6}|[0-9a-f]{3})$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"https?://[^\s<>\[\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
