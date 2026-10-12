using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.App.ViewModels;

/// <summary>
/// One mod as shown in either list. An active mod always has an <see cref="Entry"/> (what is written
/// to profile.sii); <see cref="Package"/> is null when the profile refers to a mod that is not on disk.
/// </summary>
public sealed partial class ModItemViewModel : ObservableObject
{
    public ModItemViewModel(ModPackage? package, ActiveModEntry? entry, GameInstallation install, string? nameHint)
    {
        Package = package;
        PackageId = package?.PackageId ?? entry!.PackageId;
        DisplayName = package?.Manifest?.DisplayName
            ?? entry?.DisplayName
            ?? nameHint
            ?? (package?.WorkshopId is { } id ? $"Workshop item {id}" : PackageId);
        Entry = entry ?? ActiveModEntry.Create(PackageId, DisplayName);

        var issues = package == null
            ? new List<ModIssue> { new(ModIssueKind.MissingPackage, IssueSeverity.Error, "This mod is enabled in the profile but its file is not installed. The game will skip it.") }
            : CompatibilityChecker.Check(package, install).ToList();
        Issues = issues;
        Severity = issues.Count == 0 ? null : issues.Max(i => i.Severity);
        _descriptionLines = new(() => ModDescription.Parse(package?.Description));
    }

    /// <summary>Descriptions longer than this are folded to <see cref="CollapsedDescriptionLines"/> lines.</summary>
    public const int CollapseDescriptionAbove = 14;
    public const int CollapsedDescriptionLines = 12;

    readonly Lazy<IReadOnlyList<DescriptionLine>> _descriptionLines;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleDescriptionLines), nameof(DescriptionToggleLabel))]
    private bool _showFullDescription;

    public ModPackage? Package { get; }
    public ActiveModEntry Entry { get; }
    public string PackageId { get; }
    public string DisplayName { get; }
    public IReadOnlyList<ModIssue> Issues { get; }
    public IssueSeverity? Severity { get; }

    public bool IsMissing => Package == null;
    public bool IsWorkshop => Package?.IsWorkshop ?? Entry.IsWorkshop;
    public string SourceText => IsMissing ? "Not installed" : IsWorkshop ? "Workshop" : "Local";
    public string? Version => Package?.Manifest?.PackageVersion;
    public string? Author => Package?.Manifest?.Author;
    public string? Description => Package?.Description;

    public IReadOnlyList<DescriptionLine> DescriptionLines => _descriptionLines.Value;
    public bool HasDescription => DescriptionLines.Count > 0;
    public bool CanToggleDescription => DescriptionLines.Count > CollapseDescriptionAbove;

    public IReadOnlyList<DescriptionLine> VisibleDescriptionLines
    {
        get
        {
            if (!CanToggleDescription || ShowFullDescription) return DescriptionLines;
            var lines = DescriptionLines.Take(CollapsedDescriptionLines).ToList();
            while (lines.Count > 0 && lines[^1].IsBlank) lines.RemoveAt(lines.Count - 1);
            return lines;
        }
    }

    public string DescriptionToggleLabel =>
        ShowFullDescription ? "Show less" : $"Show more ({DescriptionLines.Count - CollapsedDescriptionLines} more lines)";

    [RelayCommand]
    void ToggleDescription() => ShowFullDescription = !ShowFullDescription;
    public string? IconPath => Package?.IconPath;
    public string? Path => Package?.Path;
    public string Categories => Package?.Manifest is { Categories.Count: > 0 } m ? string.Join(", ", m.Categories) : "";
    public string CompatibleVersions => Package?.Manifest is { CompatibleVersions.Count: > 0 } m ? string.Join(", ", m.CompatibleVersions) : "any";
    public string DlcDependencies => Package?.Manifest is { DlcDependencies.Count: > 0 } m ? string.Join(", ", m.DlcDependencies) : "none";
    public bool HasWarnings => Severity >= IssueSeverity.Warning;
    public string IssueSummary => string.Join(Environment.NewLine, Issues.Where(i => i.Severity >= IssueSeverity.Warning).Select(i => i.Message));
    public string? WorkshopUrl => Package?.WorkshopId is { } id ? $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}" : null;

    public string SizeText => Package is { Size: > 0 } p ? FormatSize(p.Size) : "";

    public string Subtitle => string.Join("  ·  ", new[] { VersionLabel, SourceText, SizeText }
        .Where(s => !string.IsNullOrEmpty(s)));

    string? VersionLabel => Version is null ? null : char.IsDigit(Version[0]) ? "v" + Version : Version;

    /// <summary>Text used for the search box.</summary>
    public string SearchText => $"{DisplayName} {PackageId} {Author} {Categories}";

    static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    public override string ToString() => DisplayName;
}
