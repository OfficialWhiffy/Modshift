using TruckModsListUpdater.Core.Games;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>Issues that depend on the installed game (version, DLCs), on top of the scan's own issues.</summary>
public static class CompatibilityChecker
{
    public static IReadOnlyList<ModIssue> Check(ModPackage package, GameInstallation install)
    {
        var issues = new List<ModIssue>(package.Issues);
        var manifest = package.Manifest;
        if (manifest == null) return issues;

        if (install.GameVersion is { } version && manifest.CompatibleVersions.Count > 0
            && !VersionPattern.MatchesAny(manifest.CompatibleVersions, version))
        {
            issues.Add(new ModIssue(ModIssueKind.IncompatibleVersion, IssueSeverity.Warning,
                $"Made for game version {string.Join(", ", manifest.CompatibleVersions)}; you have {version}."));
        }

        // Without an install folder we cannot tell which DLCs are owned; stay quiet rather than guess.
        if (install.InstallDir != null)
        {
            var missing = manifest.DlcDependencies.Where(d => !install.InstalledDlc.Contains(d)).ToList();
            if (missing.Count > 0)
                issues.Add(new ModIssue(ModIssueKind.MissingDlc, IssueSeverity.Warning, $"Needs DLC that is not installed: {string.Join(", ", missing)}."));
        }
        return issues;
    }
}
