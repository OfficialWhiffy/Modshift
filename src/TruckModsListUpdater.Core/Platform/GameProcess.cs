using System.Diagnostics;
using TruckModsListUpdater.Core.Games;

namespace TruckModsListUpdater.Core.Platform;

public interface IGameProcessMonitor
{
    bool IsRunning(GameDefinition game);
}

/// <summary>
/// Polls for the game's process. The game rewrites profile.sii when it exits, so every write
/// must be refused while it runs, and profiles must be reloaded after it exits.
/// </summary>
public sealed class GameProcessMonitor : IGameProcessMonitor, IDisposable
{
    readonly Timer _timer;
    readonly Dictionary<GameDefinition, bool> _state = new();

    /// <summary>Raised on a thread-pool thread when a game starts (true) or exits (false).</summary>
    public event Action<GameDefinition, bool>? RunningChanged;

    public GameProcessMonitor(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(2);
        foreach (var g in GameDefinitions()) _state[g] = Check(g);
        _timer = new Timer(_ => Poll(), null, period, period);
    }

    static IEnumerable<GameDefinition> GameDefinitions() => GameDefinition.All;

    public bool IsRunning(GameDefinition game) => Check(game);

    static bool Check(GameDefinition game)
    {
        var processes = Process.GetProcessesByName(game.ExeName);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    void Poll()
    {
        foreach (var g in GameDefinitions())
        {
            bool running = Check(g);
            bool changed;
            lock (_state)
            {
                changed = _state[g] != running;
                _state[g] = running;
            }
            if (changed) RunningChanged?.Invoke(g, running);
        }
    }

    public void Dispose() => _timer.Dispose();
}

public static class GameLauncher
{
    /// <summary>Starts the game through Steam so overlays, cloud and DRM behave as usual.</summary>
    public static void Launch(GameDefinition game) =>
        Process.Start(new ProcessStartInfo($"steam://rungameid/{game.SteamAppId}") { UseShellExecute = true })?.Dispose();

    public static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }
}
