namespace TruckModsListUpdater.Core.Tests.LocalMachine;

/// <summary>
/// Read-only checks against the real game install on a developer machine. They are skipped when
/// the game is not installed and are excluded in CI with --filter "Category!=LocalMachine".
/// </summary>
public sealed class LocalMachineFactAttribute : FactAttribute
{
    public static readonly string Ets2Documents =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Euro Truck Simulator 2");

    public LocalMachineFactAttribute()
    {
        if (!Directory.Exists(Path.Combine(Ets2Documents, "mod")))
            Skip = "Euro Truck Simulator 2 is not installed on this machine.";
    }
}
