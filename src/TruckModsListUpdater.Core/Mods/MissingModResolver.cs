using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>
/// Finds the installed version of a mod a profile refers to under an old name, e.g. a profile that
/// still lists "promods-map-v240" while "promods-eu-map-v284" (same manifest name) is installed.
/// This replaces the hard-coded ProMods version table of the original tool.
/// </summary>
public static class MissingModResolver
{
    public static ModPackage? FindInstalled(string packageId, string? displayName, ModCatalog catalog)
    {
        if (packageId.StartsWith(ActiveModEntry.WorkshopPrefix, StringComparison.Ordinal)) return null;

        var probe = new ModPackage
        {
            PackageId = packageId, Source = ModSource.Local, Kind = PackageKind.Unknown, Path = packageId,
        };
        var candidates = catalog.Packages.Where(p => !p.IsWorkshop && !p.Issues.Any(i => i.Kind == ModIssueKind.BrokenFolder)).ToList();

        var byName = candidates.Where(p => ModIdentity.IsSameMod(probe, p)).ToList();
        if (byName.Count == 0 && !string.IsNullOrWhiteSpace(displayName))
        {
            // The profile stores the mod's display name; ProMods keeps those stable across versions
            // even when the file name changes ("promods-map" became "promods-eu-map").
            var key = ModIdentity.Key(displayName);
            byName = candidates.Where(p => p.Manifest?.DisplayName is { } n && ModIdentity.Key(n) == key).ToList();
        }
        return Newest(byName);
    }

    /// <summary>Replacement suggestions for every missing entry of a mod list.</summary>
    public static IReadOnlyList<(ActiveModEntry Missing, ModPackage Replacement)> Suggest(IEnumerable<ActiveModEntry> mods, ModCatalog catalog)
    {
        var result = new List<(ActiveModEntry, ModPackage)>();
        var list = mods.ToList();
        foreach (var entry in list.Where(e => catalog.Find(e.PackageId) == null))
        {
            var replacement = FindInstalled(entry.PackageId, entry.DisplayName, catalog);
            // Skip if the replacement is already in the list (or already suggested).
            if (replacement == null || list.Any(e => e.PackageId.Equals(replacement.PackageId, StringComparison.OrdinalIgnoreCase))
                || result.Any(r => r.Item2 == replacement))
                continue;
            result.Add((entry, replacement));
        }
        return result;
    }

    static ModPackage? Newest(List<ModPackage> packages) =>
        packages.Count switch
        {
            0 => null,
            1 => packages[0],
            _ => packages.Aggregate((best, p) => ModIdentity.Compare(p, best) == VersionComparison.Newer ? p : best),
        };
}
