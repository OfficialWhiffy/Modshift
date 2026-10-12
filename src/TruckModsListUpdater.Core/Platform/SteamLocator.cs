using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TruckModsListUpdater.Core.Platform;

/// <summary>Where Steam, its libraries and the logged-in user's cloud folder are.</summary>
public sealed record SteamInstallation(string SteamDir, IReadOnlyList<string> Libraries, IReadOnlyList<string> UserDataDirs)
{
    const ulong SteamId64Base = 76561197960265728;

    /// <summary>Finds Steam via the registry. Returns null when Steam is not installed.</summary>
    [SupportedOSPlatform("windows")]
    public static SteamInstallation? Locate()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return path == null ? null : FromSteamDir(path);
    }

    /// <summary>Reads libraryfolders.vdf and loginusers.vdf below a known Steam directory.</summary>
    public static SteamInstallation? FromSteamDir(string steamDir)
    {
        steamDir = Path.GetFullPath(steamDir);
        if (!Directory.Exists(steamDir)) return null;

        var libraries = new List<string> { steamDir };
        var folders = VdfNode.TryLoad(Path.Combine(steamDir, "steamapps", "libraryfolders.vdf"))?.Child("libraryfolders");
        if (folders != null)
        {
            foreach (var lib in folders.Children.Values)
            {
                if (lib["path"] is { Length: > 0 } p) libraries.Add(Path.GetFullPath(p));
            }
        }
        libraries = libraries.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();

        return new SteamInstallation(steamDir, libraries, FindUserDataDirs(steamDir));
    }

    /// <summary>userdata\{accountId} folders, the most recently logged-in account first.</summary>
    static List<string> FindUserDataDirs(string steamDir)
    {
        var root = Path.Combine(steamDir, "userdata");
        if (!Directory.Exists(root)) return [];
        var dirs = Directory.GetDirectories(root).Where(d => uint.TryParse(Path.GetFileName(d), out _)).ToList();

        var users = VdfNode.TryLoad(Path.Combine(steamDir, "config", "loginusers.vdf"))?.Child("users");
        var mostRecent = users?.Children
            .FirstOrDefault(u => u.Value["MostRecent"] == "1" && ulong.TryParse(u.Key, out var id) && id > SteamId64Base).Key;
        if (mostRecent != null)
        {
            var accountId = (ulong.Parse(mostRecent) - SteamId64Base).ToString();
            dirs = dirs.OrderByDescending(d => Path.GetFileName(d) == accountId).ToList();
        }
        return dirs;
    }

    /// <summary>steamapps\common\{installdir} of an app, using its appmanifest in any library.</summary>
    public string? FindAppInstallDir(uint appId, string defaultFolderName)
    {
        foreach (var lib in Libraries)
        {
            var manifest = VdfNode.TryLoad(Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf"))?.Child("AppState");
            var folder = manifest?["installdir"] ?? defaultFolderName;
            var dir = Path.Combine(lib, "steamapps", "common", folder);
            if (manifest != null && Directory.Exists(dir)) return dir;
        }
        // No manifest (e.g. copied install): fall back to the default folder name.
        return Libraries.Select(l => Path.Combine(l, "steamapps", "common", defaultFolderName)).FirstOrDefault(Directory.Exists);
    }

    /// <summary>steamapps\workshop\content\{appId} in every library that has it.</summary>
    public IReadOnlyList<string> WorkshopDirs(uint appId) =>
        Libraries.Select(l => Path.Combine(l, "steamapps", "workshop", "content", appId.ToString()))
            .Where(Directory.Exists).ToList();

    /// <summary>userdata\{id}\{appId}\remote for each account that has cloud files for the app.</summary>
    public IReadOnlyList<string> RemoteDirs(uint appId) =>
        UserDataDirs.Select(u => Path.Combine(u, appId.ToString(), "remote")).Where(Directory.Exists).ToList();
}
