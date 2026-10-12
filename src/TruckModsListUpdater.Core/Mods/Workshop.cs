using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>Game version patterns as used in manifests and versions.sii ("1.61.*", "1.5*").</summary>
public static class VersionPattern
{
    public static bool Matches(string pattern, string version)
    {
        pattern = pattern.Trim();
        if (pattern.Length == 0) return false;
        int star = pattern.IndexOf('*');
        if (star < 0)
            return version == pattern || version.StartsWith(pattern + ".", StringComparison.Ordinal);
        // Only trailing wildcards are used in practice; treat any '*' as "anything from here on".
        return version.StartsWith(pattern[..star], StringComparison.Ordinal);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string version) => patterns.Any(p => Matches(p, version));
}

/// <summary>Resolves which package inside a Steam Workshop item folder the game loads.</summary>
public static class WorkshopItem
{
    /// <summary>
    /// A workshop item may ship several packages; versions.sii maps game versions to one of them
    /// (the entry without compatible_versions is the fallback). Returns a file or folder path.
    /// </summary>
    public static string? ResolvePackage(string itemDir, string? gameVersion)
    {
        var versionsFile = Path.Combine(itemDir, "versions.sii");
        if (File.Exists(versionsFile))
        {
            string? chosen = null, fallback = null;
            try
            {
                foreach (var unit in SiiCodec.DecodeFile(versionsFile).ReadUnits().Where(u => u.Type == "package_version_info"))
                {
                    var name = unit.Get("package_name");
                    if (string.IsNullOrEmpty(name)) continue;
                    var compatible = unit.GetArray("compatible_versions");
                    if (compatible.Count == 0) fallback ??= name;
                    else if (gameVersion != null && VersionPattern.MatchesAny(compatible, gameVersion)) { chosen = name; break; }
                }
            }
            catch (SiiFormatException) { }
            if ((chosen ?? fallback) is { } package && FindPackage(itemDir, package) is { } path) return path;
        }

        if (FindPackage(itemDir, "universal") is { } universal) return universal;
        if (File.Exists(Path.Combine(itemDir, "manifest.sii"))) return itemDir;

        // Single-package items without versions.sii.
        var archives = Directory.EnumerateFiles(itemDir).Where(f => f.EndsWith(".scs", StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        return archives.Count == 1 ? archives[0] : null;
    }

    static string? FindPackage(string itemDir, string name)
    {
        foreach (var candidate in new[] { name, name + ".scs", name + ".zip" })
        {
            var p = Path.Combine(itemDir, candidate);
            if (File.Exists(p) || Directory.Exists(p)) return p;
        }
        return null;
    }
}
