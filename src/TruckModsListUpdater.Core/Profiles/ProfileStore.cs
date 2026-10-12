using System.Text;
using System.Text.RegularExpressions;
using TruckModsListUpdater.Core.Games;

namespace TruckModsListUpdater.Core.Profiles;

public enum ProfileKind { Local, SteamCloud }

/// <summary>A game profile folder (named after the hex-encoded profile name) holding profile.sii.</summary>
public sealed record ProfileRef(ProfileKind Kind, string Dir, string DisplayName, DateTime LastWriteUtc)
{
    public string FolderName => Path.GetFileName(Dir);
    public string SiiPath => Path.Combine(Dir, "profile.sii");
    public bool IsCloud => Kind == ProfileKind.SteamCloud;
}

public static partial class ProfileStore
{
    /// <summary>
    /// Lists local profiles (Documents\...\profiles) and Steam Cloud profiles, whose real
    /// profile.sii lives in Steam\userdata\{account}\{app}\remote\profiles (the Documents
    /// steam_profiles folder only holds controls and config).
    /// </summary>
    public static IReadOnlyList<ProfileRef> Enumerate(GameInstallation install)
    {
        var profiles = new List<ProfileRef>();
        profiles.AddRange(FromRoot(Path.Combine(install.DocumentsDir, "profiles"), ProfileKind.Local));
        // Only the most recent Steam account's cloud folder (first in the list) belongs to this user.
        if (install.SteamRemoteDirs.Count > 0)
            profiles.AddRange(FromRoot(Path.Combine(install.SteamRemoteDirs[0], "profiles"), ProfileKind.SteamCloud));
        return profiles.OrderByDescending(p => p.LastWriteUtc).ToList();
    }

    static IEnumerable<ProfileRef> FromRoot(string root, ProfileKind kind)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var sii = Path.Combine(dir, "profile.sii");
            if (!File.Exists(sii)) continue;
            yield return new ProfileRef(kind, dir, DecodeFolderName(Path.GetFileName(dir)), File.GetLastWriteTimeUtc(sii));
        }
    }

    /// <summary>Profile folders are the UTF-8 bytes of the profile name in hex ("576869666679" = "Whiffy").</summary>
    public static string DecodeFolderName(string folderName)
    {
        if (folderName.Length % 2 != 0 || !folderName.All(char.IsAsciiHexDigit)) return folderName;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(folderName));
        }
        catch (FormatException)
        {
            return folderName;
        }
    }

    /// <summary>
    /// The profile the game used last, from game.log.txt paths like "/home/profiles/{hex}" (local) or
    /// "/steam/profiles/{hex}" (cloud). Returns null when the log names no profile.
    /// </summary>
    public static ProfileRef? FindActive(GameInstallation install, IReadOnlyList<ProfileRef> profiles)
    {
        if (!File.Exists(install.LogPath)) return null;
        string log;
        try
        {
            using var stream = new FileStream(install.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            log = new StreamReader(stream).ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }

        var last = ProfilePathRegex().Matches(log).LastOrDefault();
        if (last == null) return null;
        var kind = last.Groups["kind"].Value == "steam" ? ProfileKind.SteamCloud : ProfileKind.Local;
        var hex = last.Groups["hex"].Value;
        return profiles.FirstOrDefault(p => p.Kind == kind && p.FolderName.Equals(hex, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"/(?<kind>home|steam)/profiles/(?<hex>[0-9A-Fa-f]+)/")]
    private static partial Regex ProfilePathRegex();
}
