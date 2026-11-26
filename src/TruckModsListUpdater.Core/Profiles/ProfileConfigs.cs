using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TruckModsListUpdater.Core.Games;

namespace TruckModsListUpdater.Core.Profiles;

/// <summary>The groups of per-profile settings files that can be copied between profiles.</summary>
public enum ConfigCategory { Gameplay, Controls, LocalSettings, GearboxLayouts }

/// <summary>The two folders a profile's settings can be in.</summary>
public enum ConfigRoot
{
    /// <summary>The folder that holds profile.sii (Documents\profiles\{hex}, or the Steam Cloud folder).</summary>
    Profile,

    /// <summary>Documents\steam_profiles\{hex}: the files of a Steam Cloud profile that stay on this PC.</summary>
    SteamProfiles,
}

/// <summary>
/// Which files belong to each <see cref="ConfigCategory"/> and where they are. A local profile keeps
/// everything next to profile.sii. A Steam Cloud profile keeps config.cfg in the cloud folder and its
/// controls, local settings and gearbox layouts in Documents\steam_profiles\{hex}.
/// profile.sii, profile.bak.sii and saves are never part of any category.
/// </summary>
public static partial class ProfileConfigs
{
    public static IReadOnlyList<ConfigCategory> All { get; } = Enum.GetValues<ConfigCategory>();

    public static string DisplayName(ConfigCategory category) => category switch
    {
        ConfigCategory.Gameplay => "Gameplay settings",
        ConfigCategory.Controls => "Controls",
        ConfigCategory.LocalSettings => "Audio and local settings",
        ConfigCategory.GearboxLayouts => "Gearbox layouts",
        _ => category.ToString(),
    };

    public static string Description(ConfigCategory category) => category switch
    {
        ConfigCategory.Gameplay => "config.cfg: driving aids, transmission, camera, cruise control, interface",
        ConfigCategory.Controls => "controls.sii and ffb_lut.sii: keyboard, wheel and controller bindings",
        ConfigCategory.LocalSettings => "config_local.cfg: volumes and other settings kept on this PC",
        ConfigCategory.GearboxLayouts => "gearbox_layout_*.sii: H-shifter layouts",
        _ => "",
    };

    public static bool Matches(ConfigCategory category, string fileName) => category switch
    {
        ConfigCategory.Gameplay => fileName.Equals("config.cfg", StringComparison.OrdinalIgnoreCase),
        ConfigCategory.Controls => fileName.Equals("controls.sii", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("ffb_lut.sii", StringComparison.OrdinalIgnoreCase),
        ConfigCategory.LocalSettings => fileName.Equals("config_local.cfg", StringComparison.OrdinalIgnoreCase),
        ConfigCategory.GearboxLayouts => GearboxLayoutRegex().IsMatch(fileName),
        _ => false,
    };

    public static ConfigCategory? CategoryOf(string fileName) =>
        All.Cast<ConfigCategory?>().FirstOrDefault(c => Matches(c!.Value, fileName));

    public static string RootDir(GameInstallation install, ProfileRef profile, ConfigRoot root) => root switch
    {
        ConfigRoot.Profile => profile.Dir,
        ConfigRoot.SteamProfiles => Path.Combine(install.DocumentsDir, "steam_profiles", profile.FolderName),
        _ => throw new ArgumentOutOfRangeException(nameof(root)),
    };

    /// <summary>The folders to look in, most likely first.</summary>
    public static IReadOnlyList<ConfigRoot> SearchRoots(ProfileRef profile, ConfigCategory category) =>
        !profile.IsCloud ? [ConfigRoot.Profile]
        : category == ConfigCategory.Gameplay ? [ConfigRoot.Profile, ConfigRoot.SteamProfiles]
        : [ConfigRoot.SteamProfiles, ConfigRoot.Profile];

    /// <summary>Where a file goes when the target profile does not have it yet.</summary>
    public static ConfigRoot DefaultRoot(ProfileRef profile, ConfigCategory category) => SearchRoots(profile, category)[0];

    /// <summary>The category's files a profile has, by file name; the first folder that has a name wins.</summary>
    public static IReadOnlyList<ConfigFile> Find(GameInstallation install, ProfileRef profile, ConfigCategory category)
    {
        var found = new Dictionary<string, ConfigFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SearchRoots(profile, category))
        {
            var dir = RootDir(install, profile, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(path);
                if (Matches(category, name) && !found.ContainsKey(name)) found[name] = new ConfigFile(category, root, name, path);
            }
        }
        return found.Values.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Hash of a file's current contents, or null when it does not exist.</summary>
    public static string? Sha256OfFile(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [GeneratedRegex(@"^gearbox_layout_.+\.sii$", RegexOptions.IgnoreCase)]
    private static partial Regex GearboxLayoutRegex();
}

/// <summary>One settings file of a profile.</summary>
public sealed record ConfigFile(ConfigCategory Category, ConfigRoot Root, string FileName, string Path);
