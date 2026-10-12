using System.Reflection;
using System.Text.RegularExpressions;

namespace TruckModsListUpdater.Core;

/// <summary>
/// A semantic version (MAJOR.MINOR.PATCH[-pre-release][+build]) as stamped into the app by its git tag.
/// Ordering follows semver.org: a pre-release sorts before its release, build metadata is ignored.
/// </summary>
public sealed partial record AppVersion(int Major, int Minor, int Patch, string? PreRelease = null, string? Build = null)
    : IComparable<AppVersion>
{
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>"2.1.0" or "2.1.1-alpha.0.7": the version without build metadata, as shown in the UI.</summary>
    public string Display => $"{Major}.{Minor}.{Patch}" + (PreRelease is null ? "" : "-" + PreRelease);

    /// <summary>The display version plus a short commit hash when the build carries one, for logs and About text.</summary>
    public string Detailed => Build is { Length: > 0 } b ? $"{Display}+{(b.Length > 7 ? b[..7] : b)}" : Display;

    public override string ToString() => Display;

    /// <summary>Parses "2.1.0", "v2.1.0-beta.1" or "2.1.0+abc1234".</summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default!;
        var m = text is null ? null : SemVerRegex().Match(text.Trim());
        if (m is not { Success: true }) return false;
        if (!int.TryParse(m.Groups["major"].ValueSpan, out var major) ||
            !int.TryParse(m.Groups["minor"].ValueSpan, out var minor) ||
            !int.TryParse(m.Groups["patch"].ValueSpan, out var patch)) return false;
        version = new AppVersion(major, minor, patch,
            m.Groups["pre"].Success ? m.Groups["pre"].Value : null,
            m.Groups["build"].Success ? m.Groups["build"].Value : null);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Not a semantic version: '{text}'");

    /// <summary>Reads the version MinVer stamped into the assembly, falling back to its plain assembly version.</summary>
    public static AppVersion FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParse(informational, out var v)) return v;
        var plain = assembly.GetName().Version;
        return plain is null ? new AppVersion(0, 0, 0) : new AppVersion(plain.Major, plain.Minor, Math.Max(plain.Build, 0));
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator <(AppVersion? a, AppVersion? b) => a is null ? b is not null : a.CompareTo(b) < 0;
    public static bool operator >(AppVersion? a, AppVersion? b) => a is not null && a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion? a, AppVersion? b) => !(a > b);
    public static bool operator >=(AppVersion? a, AppVersion? b) => !(a < b);

    // Equality ignores build metadata, to stay consistent with CompareTo.
    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0 &&
        string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    static int ComparePreRelease(string? a, string? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;   // a release outranks any pre-release of the same version
        if (b is null) return -1;

        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var l = left[i];
            var r = right[i];
            var lNumeric = long.TryParse(l, out var ln);
            var rNumeric = long.TryParse(r, out var rn);
            int c = (lNumeric, rNumeric) switch
            {
                (true, true) => ln.CompareTo(rn),
                (true, false) => -1,   // numeric identifiers sort before alphanumeric ones
                (false, true) => 1,
                _ => string.CompareOrdinal(l, r),
            };
            if (c != 0) return c;
        }
        return left.Length.CompareTo(right.Length);
    }

    [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex SemVerRegex();
}
