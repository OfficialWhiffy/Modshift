using System.Text.RegularExpressions;

namespace TruckModsListUpdater.Core.Mods;

public enum VersionComparison { Older, Same, Newer, Unknown }

/// <summary>
/// Recognises different versions of the same mod: "promods-eu-map-v283" and "promods-eu-map-v284",
/// "50RXF1" and "53RXF1", "ROEX50PMME281" and "ROEX53PMME284".
/// </summary>
public static partial class ModIdentity
{
    /// <summary>
    /// File-name identity: lower case with version-like digit runs removed (runs of two or more digits,
    /// digits after a 'v', and dotted versions). Single digits are kept so part numbers stay distinct
    /// ("model1" vs "model2", "RXF1" vs "RXF2").
    /// </summary>
    public static string Key(string packageId)
    {
        var s = packageId.ToLowerInvariant();
        s = DottedVersionRegex().Replace(s, "");
        s = VersionRunRegex().Replace(s, "");
        s = SeparatorRunRegex().Replace(s, "-").Trim('-', '_', ' ', '.');
        return s;
    }

    /// <summary>True when two packages look like versions of the same mod (but are not the same id).</summary>
    public static bool IsSameMod(ModPackage a, ModPackage b)
    {
        if (string.Equals(a.PackageId, b.PackageId, StringComparison.OrdinalIgnoreCase)) return false;
        if (a.IsWorkshop || b.IsWorkshop) return false; // Steam updates workshop items itself
        var ka = Key(a.PackageId);
        if (ka.Length >= 3 && ka == Key(b.PackageId)) return true;
        // Same manifest name and author, e.g. "ProMods Map Package" by "ProMods Team".
        return a.Manifest?.DisplayName is { } na && b.Manifest?.DisplayName is { } nb
            && a.Manifest.Author is { } aa && b.Manifest?.Author is { } ab
            && string.Equals(Key(na), Key(nb), StringComparison.Ordinal)
            && string.Equals(aa, ab, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Compares <paramref name="candidate"/> with <paramref name="existing"/>: manifest version, then file-name digits, then date.</summary>
    public static VersionComparison Compare(ModPackage candidate, ModPackage existing)
    {
        var byManifest = CompareVersionStrings(candidate.Manifest?.PackageVersion, existing.Manifest?.PackageVersion);
        if (byManifest is not VersionComparison.Unknown and not VersionComparison.Same) return byManifest;

        var byName = CompareVersionStrings(NameVersion(candidate.PackageId), NameVersion(existing.PackageId));
        if (byName != VersionComparison.Unknown) return byName;
        if (byManifest == VersionComparison.Same) return VersionComparison.Same;

        if (candidate.LastWriteUtc == default || existing.LastWriteUtc == default) return VersionComparison.Unknown;
        return candidate.LastWriteUtc > existing.LastWriteUtc ? VersionComparison.Newer
            : candidate.LastWriteUtc < existing.LastWriteUtc ? VersionComparison.Older
            : VersionComparison.Same;
    }

    /// <summary>
    /// The version-like numbers in a file name (the same runs <see cref="Key"/> removes), joined:
    /// "ROEX53PMME284" → "53.284", "mod-v2" → "2". Part numbers such as the 1 in "RXF1" are ignored.
    /// </summary>
    static string? NameVersion(string packageId)
    {
        var runs = NameVersionRegex().Matches(packageId.ToLowerInvariant()).Select(m => m.Value.TrimStart('v')).ToList();
        return runs.Count == 0 ? null : string.Join('.', runs);
    }

    static VersionComparison CompareVersionStrings(string? a, string? b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        if (pa == null || pb == null) return VersionComparison.Unknown;
        for (int i = 0; i < Math.Max(pa.Count, pb.Count); i++)
        {
            long x = i < pa.Count ? pa[i] : 0, y = i < pb.Count ? pb[i] : 0;
            if (x != y) return x > y ? VersionComparison.Newer : VersionComparison.Older;
        }
        return VersionComparison.Same;
    }

    static List<long>? Parts(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = NumberRunRegex().Matches(version).SelectMany(m => m.Value.Split('.'))
            .Select(p => long.TryParse(p, out var n) ? n : (long?)null).ToList();
        return parts.Count == 0 || parts.Any(p => p == null) ? null : parts.Select(p => p!.Value).ToList();
    }

    [GeneratedRegex(@"v?\d+(?:\.\d+)+")]
    private static partial Regex DottedVersionRegex();

    [GeneratedRegex(@"v\d+|\d{2,}")]
    private static partial Regex VersionRunRegex();

    [GeneratedRegex(@"[-_ .]{2,}")]
    private static partial Regex SeparatorRunRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)*")]
    private static partial Regex NumberRunRegex();

    [GeneratedRegex(@"v?\d+(?:\.\d+)+|v\d+|\d{2,}")]
    private static partial Regex NameVersionRegex();
}
