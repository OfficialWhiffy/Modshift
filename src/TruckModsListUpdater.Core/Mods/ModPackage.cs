using TruckModsListUpdater.Core.Archives;

namespace TruckModsListUpdater.Core.Mods;

public enum ModSource { Local, Workshop }

public enum IssueSeverity { Info, Warning, Error }

public enum ModIssueKind
{
    /// <summary>Package has no manifest.sii (older mods); shown with its file name.</summary>
    NoManifest,
    /// <summary>Package could not be opened or its manifest could not be decoded.</summary>
    Unreadable,
    /// <summary>A folder in mod\ without manifest.sii; the game loads it as a broken mod.</summary>
    BrokenFolder,
    /// <summary>Two packages share one id (e.g. x.scs and x.zip); only one is loaded.</summary>
    DuplicateId,
    /// <summary>The manifest's compatible_versions do not include the installed game version.</summary>
    IncompatibleVersion,
    /// <summary>A DLC listed in dlc_dependencies is not installed.</summary>
    MissingDlc,
    /// <summary>The profile enables a mod whose package is not on disk.</summary>
    MissingPackage,
}

public sealed record ModIssue(ModIssueKind Kind, IssueSeverity Severity, string Message);

/// <summary>A mod package found on disk (local mod folder or Steam Workshop).</summary>
public sealed record ModPackage
{
    /// <summary>Id used in profile.sii active_mods: file name without extension, or mod_workshop_package.{hex}.</summary>
    public required string PackageId { get; init; }
    public required ModSource Source { get; init; }
    public required PackageKind Kind { get; init; }
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public ulong? WorkshopId { get; init; }
    public ModManifest? Manifest { get; init; }
    public string? Description { get; init; }
    /// <summary>Cached copy of the package icon on local disk, if the package has one.</summary>
    public string? IconPath { get; init; }
    public IReadOnlyList<ModIssue> Issues { get; init; } = [];

    public string DisplayName => Manifest?.DisplayName ?? PackageId;
    public bool IsWorkshop => Source == ModSource.Workshop;
}
