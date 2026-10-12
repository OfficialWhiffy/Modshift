namespace TruckModsListUpdater.Core.Games;

/// <summary>Static facts about one SCS truck game (ETS2 or ATS).</summary>
public sealed record GameDefinition(
    string Id,
    string DisplayName,
    uint SteamAppId,
    string DocumentsFolderName,
    string InstallFolderName,
    string ExeName)
{
    public static readonly GameDefinition Ets2 = new(
        "ets2", "Euro Truck Simulator 2", 227300, "Euro Truck Simulator 2", "Euro Truck Simulator 2", "eurotrucks2");

    public static readonly GameDefinition Ats = new(
        "ats", "American Truck Simulator", 270880, "American Truck Simulator", "American Truck Simulator", "amtrucks");

    public static IReadOnlyList<GameDefinition> All { get; } = [Ets2, Ats];

    /// <summary>Executable path relative to the install folder.</summary>
    public string ExeRelativePath => Path.Combine("bin", "win_x64", ExeName + ".exe");
}
