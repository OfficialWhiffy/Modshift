using CommunityToolkit.Mvvm.ComponentModel;
using TruckModsListUpdater.Core.Mods;

namespace TruckModsListUpdater.App.ViewModels;

/// <summary>The install preview: what was found in the dropped files and what will happen to each mod.</summary>
public sealed partial class InstallViewModel : ObservableObject
{
    public InstallViewModel(InstallPlan plan, string? profileName)
    {
        Plan = plan;
        ProfileName = profileName;
        Candidates = plan.Candidates.Select(c => new InstallCandidateViewModel(c)).ToList();
        foreach (var c in Candidates) c.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        _enableInProfile = profileName != null;
    }

    public InstallPlan Plan { get; }
    public string? ProfileName { get; }
    public IReadOnlyList<InstallCandidateViewModel> Candidates { get; }
    public IReadOnlyList<string> Problems => Plan.Problems;
    public bool HasProblems => Problems.Count > 0;
    public bool HasReplacements => Candidates.Any(c => c.Candidate.Existing != null && !c.Candidate.Existing.PackageId.Equals(c.Candidate.Package.PackageId, StringComparison.OrdinalIgnoreCase));
    public bool CanEnable => ProfileName != null;

    [ObservableProperty] private bool _enableInProfile;
    [ObservableProperty] private bool _updateProfiles = true;

    public string EnableText => $"Enable the new mods in \"{ProfileName}\" (you still click Apply)";

    public string Summary
    {
        get
        {
            int install = Candidates.Count(c => c.Action == InstallAction.Install);
            int replace = Candidates.Count(c => c.Action == InstallAction.Replace);
            int skip = Candidates.Count(c => c.Action == InstallAction.Skip);
            return $"{install} to install, {replace} to replace (old files go to the archive), {skip} skipped";
        }
    }

    public bool HasWork => Candidates.Any(c => c.Action != InstallAction.Skip);
}

public sealed partial class InstallCandidateViewModel : ObservableObject
{
    public InstallCandidateViewModel(InstallCandidate candidate)
    {
        Candidate = candidate;
        _action = candidate.Action;
        Actions = candidate.Existing == null ? [InstallAction.Install, InstallAction.Skip] : [InstallAction.Replace, InstallAction.Install, InstallAction.Skip];
        if (candidate.Conflict == InstallConflict.SameFileName) Actions = [InstallAction.Replace, InstallAction.Skip];
    }

    public InstallCandidate Candidate { get; }
    public IReadOnlyList<InstallAction> Actions { get; }
    public string DisplayName => Candidate.Package.DisplayName;
    public string? Version => Candidate.Package.Manifest?.PackageVersion;
    public string? IconPath => Candidate.Package.IconPath;
    public string Source => Candidate.SourceDescription;
    public string ConflictText => Candidate.ConflictText;
    public bool IsConflict => Candidate.Conflict != InstallConflict.None;

    [ObservableProperty] private InstallAction _action;

    partial void OnActionChanged(InstallAction value) => Candidate.Action = value;
}
