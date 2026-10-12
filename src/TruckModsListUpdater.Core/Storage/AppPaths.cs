namespace TruckModsListUpdater.Core.Storage;

/// <summary>Where the app keeps its own files. Nothing is ever written into the game's profile folders.</summary>
public sealed record AppPaths(string RoamingDir, string LocalDir)
{
    public const string AppFolderName = "Modshift";

    /// <summary>The folder name used before the app was renamed (Truck Mod Manager).</summary>
    public const string LegacyAppFolderName = "TruckModManager";

    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

    /// <summary>
    /// Moves the settings, presets, backups and logs of the old app name into the new folders.
    /// Does nothing when the new folder already exists or the old one does not; never throws.
    /// </summary>
    public static void MigrateLegacyFolders(string roamingRoot, string localRoot)
    {
        foreach (var root in new[] { roamingRoot, localRoot })
        {
            var legacy = Path.Combine(root, LegacyAppFolderName);
            var current = Path.Combine(root, AppFolderName);
            try
            {
                if (Directory.Exists(legacy) && !Directory.Exists(current)) Directory.Move(legacy, current);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The app starts with empty folders; the old data stays where it was.
            }
        }
    }

    public string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public string PresetsDir => Path.Combine(RoamingDir, "presets");
    public string CacheDir => Path.Combine(LocalDir, "cache");
    public string BackupsDir => Path.Combine(LocalDir, "backups");
    public string LogsDir => Path.Combine(LocalDir, "logs");
}
