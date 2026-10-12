using TruckModsListUpdater.Core.Profiles;

namespace TruckModsListUpdater.App.ViewModels;

public sealed class ProfileItemViewModel(ProfileRef profile, bool isActive, int modCount)
{
    public ProfileRef Profile { get; } = profile;
    public bool IsActive { get; } = isActive;
    public int ModCount { get; } = modCount;
    public string DisplayName => Profile.DisplayName;
    public string KindText => Profile.IsCloud ? "Steam Cloud" : "Local";
    public string Summary => $"{KindText} · {ModCount} mods{(IsActive ? " · last played" : "")}";

    public override string ToString() => DisplayName;
}
