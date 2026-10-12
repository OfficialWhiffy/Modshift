using System.Text.RegularExpressions;

namespace TruckModsListUpdater.Core.Sii;

/// <summary>
/// One entry of profile.sii's active_mods array: "package id|display name".
/// <see cref="RawValue"/> keeps the exact quoted text from the file so untouched entries are
/// written back byte-for-byte.
/// </summary>
public sealed record ActiveModEntry(string PackageId, string DisplayName, string RawValue)
{
    public const string WorkshopPrefix = "mod_workshop_package.";

    public static ActiveModEntry Create(string packageId, string displayName) =>
        new(packageId, displayName, SiiString.Quote(packageId + "|" + displayName));

    public static ActiveModEntry FromValue(string value)
    {
        int bar = value.IndexOf('|');
        return bar < 0
            ? new ActiveModEntry(value, value, SiiString.Quote(value))
            : new ActiveModEntry(value[..bar], value[(bar + 1)..], SiiString.Quote(value));
    }

    public bool IsWorkshop => PackageId.StartsWith(WorkshopPrefix, StringComparison.Ordinal);

    /// <summary>Workshop item id (decimal, as used for the workshop folder), or null for local mods.</summary>
    public ulong? WorkshopId =>
        IsWorkshop && ulong.TryParse(PackageId.AsSpan(WorkshopPrefix.Length), System.Globalization.NumberStyles.HexNumber, null, out var id)
            ? id : null;

    public static string WorkshopPackageId(ulong workshopId) => $"{WorkshopPrefix}{workshopId:X16}";
}

/// <summary>
/// Reads and rewrites the active_mods array inside the user_profile unit of a text profile.sii,
/// leaving every other byte of the file untouched.
/// </summary>
/// <remarks>
/// File order is load order: index 0 is loaded first and has the LOWEST priority (bottom of the
/// in-game list). Use <see cref="LoadOrderMapper"/> to convert to the UI order.
/// </remarks>
public static partial class ActiveModsBlock
{
    /// <summary>
    /// Returns the entries in file order. Index numbers and the count line are ignored, so
    /// hand-edited files with duplicate indices or a wrong count still read correctly.
    /// </summary>
    public static List<ActiveModEntry> Parse(string text)
    {
        var lines = text.Split('\n');
        var (start, unitEnd) = FindUserProfile(lines);
        var entries = new List<ActiveModEntry>();
        for (int i = start + 1; i < unitEnd; i++)
        {
            var m = EntryRegex().Match(lines[i].TrimEnd('\r'));
            if (!m.Success) continue;
            var rest = m.Groups["value"].Value;
            int end = SiiString.ReadQuoted(rest, 0, out var value);
            entries.Add(FromRaw(value, rest[..end]));
        }
        return entries;
    }

    static ActiveModEntry FromRaw(string value, string raw)
    {
        var entry = ActiveModEntry.FromValue(value);
        return entry with { RawValue = raw };
    }

    /// <summary>Renders the block as lines (without newline characters).</summary>
    public static List<string> Render(IReadOnlyList<ActiveModEntry> entries, string indent = " ")
    {
        var lines = new List<string>(entries.Count + 1) { $"{indent}active_mods: {entries.Count}" };
        for (int i = 0; i < entries.Count; i++) lines.Add($"{indent}active_mods[{i}]: {entries[i].RawValue}");
        return lines;
    }

    /// <summary>
    /// Replaces the active_mods lines of the user_profile unit with a clean block (correct count,
    /// sequential indices) at the position of the first old line. Everything else is unchanged.
    /// </summary>
    public static string Replace(string text, IReadOnlyList<ActiveModEntry> entries)
    {
        bool crlf = text.Contains("\r\n", StringComparison.Ordinal);
        var lines = text.Split('\n').ToList();
        var (start, end) = FindUserProfile(lines);

        int first = -1;
        string indent = " ";
        for (int i = end - 1; i > start; i--)
        {
            var line = lines[i].TrimEnd('\r');
            if (!CountRegex().IsMatch(line) && !EntryRegex().IsMatch(line)) continue;
            first = i;
            indent = line[..(line.Length - line.TrimStart().Length)];
            lines.RemoveAt(i);
        }
        if (first < 0)
            throw new SiiFormatException("profile.sii has no active_mods attribute in its user_profile unit.");

        var block = Render(entries, indent).Select(l => crlf ? l + "\r" : l);
        lines.InsertRange(first, block);
        return string.Join('\n', lines);
    }

    /// <summary>The text with the active_mods lines of the user_profile unit removed; used to prove nothing else changed.</summary>
    public static string WithoutBlock(string text)
    {
        var lines = text.Split('\n').ToList();
        var (start, end) = FindUserProfile(lines);
        for (int i = end - 1; i > start; i--)
        {
            var line = lines[i].TrimEnd('\r');
            if (CountRegex().IsMatch(line) || EntryRegex().IsMatch(line)) lines.RemoveAt(i);
        }
        return string.Join('\n', lines);
    }

    /// <summary>Line range (header line, closing brace line) of the user_profile unit.</summary>
    static (int Start, int End) FindUserProfile(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (!UnitHeaderRegex().IsMatch(lines[i])) continue;
            for (int j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].Trim() == "}") return (i, j);
            }
            return (i, lines.Count);
        }
        throw new SiiFormatException("profile.sii has no user_profile unit.");
    }

    [GeneratedRegex(@"^\s*user_profile\s*:\s*\S+\s*\{\s*$")]
    private static partial Regex UnitHeaderRegex();

    [GeneratedRegex(@"^\s*active_mods\s*:\s*\d+\s*$")]
    private static partial Regex CountRegex();

    [GeneratedRegex(@"^\s*active_mods\[\d*\]\s*:\s*(?<value>"".*)$")]
    private static partial Regex EntryRegex();
}

/// <summary>
/// The only place that converts between file order (index 0 = lowest priority) and the UI
/// order (top = highest priority, as in the in-game Mod Manager).
/// </summary>
public static class LoadOrderMapper
{
    public static List<T> ToDisplayOrder<T>(IEnumerable<T> fileOrder) => fileOrder.Reverse().ToList();

    public static List<T> ToFileOrder<T>(IEnumerable<T> displayOrder) => displayOrder.Reverse().ToList();
}
