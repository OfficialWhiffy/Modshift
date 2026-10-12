using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Profiles;

/// <summary>A mod that was replaced by another package (e.g. a new version with a new file name).</summary>
public sealed record ModReplacement(string OldPackageId, string NewPackageId, string NewDisplayName);

/// <summary>
/// After installing a new version of a mod under a new file name, points every profile that used
/// the old version at the new one, keeping its position in the load order. This generalises the
/// original ProMods version upgrade.
/// </summary>
public static class ProfileReferenceUpdater
{
    public static IReadOnlyList<ActiveModEntry> Apply(IReadOnlyList<ActiveModEntry> mods, IReadOnlyCollection<ModReplacement> replacements)
    {
        var result = new List<ActiveModEntry>(mods.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mods)
        {
            var r = replacements.FirstOrDefault(r => r.OldPackageId.Equals(entry.PackageId, StringComparison.OrdinalIgnoreCase));
            var updated = r == null ? entry : ActiveModEntry.Create(r.NewPackageId, r.NewDisplayName);
            // If the profile already had the new version too, keep only one entry.
            if (seen.Add(updated.PackageId)) result.Add(updated);
        }
        return result;
    }

    /// <summary>Rewrites every writable profile that uses one of the old ids. Returns the updated profiles.</summary>
    public static IReadOnlyList<(ProfileRef Profile, ProfileWriteResult Result)> UpdateProfiles(
        GameDefinition game, IEnumerable<ProfileRef> profiles, IReadOnlyCollection<ModReplacement> replacements, ProfileWriter writer)
    {
        var updated = new List<(ProfileRef, ProfileWriteResult)>();
        if (replacements.Count == 0) return updated;
        foreach (var profile in profiles)
        {
            var session = ProfileSession.Load(profile);
            if (!session.IsWritable) continue;
            if (!session.ActiveMods.Any(m => replacements.Any(r => r.OldPackageId.Equals(m.PackageId, StringComparison.OrdinalIgnoreCase))))
                continue;
            updated.Add((profile, writer.WriteActiveMods(game, session, Apply(session.ActiveMods, replacements))));
        }
        return updated;
    }
}
