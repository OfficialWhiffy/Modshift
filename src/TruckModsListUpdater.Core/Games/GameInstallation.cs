using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using TruckModsListUpdater.Core.Platform;

namespace TruckModsListUpdater.Core.Games;

/// <summary>Everything the manager needs to know about one installed game on this PC.</summary>
public sealed record GameInstallation(
    GameDefinition Game,
    string DocumentsDir,
    string? InstallDir,
    IReadOnlyList<string> WorkshopDirs,
    IReadOnlyList<string> SteamRemoteDirs,
    string? GameVersion,
    IReadOnlySet<string> InstalledDlc)
{
    public string ModDir => Path.Combine(DocumentsDir, "mod");
    public string ArchiveDir => Path.Combine(DocumentsDir, "mod_archive");
    public string LogPath => Path.Combine(DocumentsDir, "game.log.txt");
    public string? ExePath => InstallDir == null ? null : Path.Combine(InstallDir, Game.ExeRelativePath);
}

public static partial class GameLocator
{
    /// <summary>
    /// Locates a game. The documents folder is required (it holds mods and local profiles);
    /// Steam, install dir and workshop are optional.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static GameInstallation? Locate(GameDefinition game, string? documentsOverride = null) =>
        Locate(game, SteamInstallation.Locate(), DocumentsCandidates(game, documentsOverride));

    public static GameInstallation? Locate(GameDefinition game, SteamInstallation? steam, IEnumerable<string> documentsCandidates)
    {
        var docs = documentsCandidates.FirstOrDefault(IsGameDocumentsDir);
        if (docs == null) return null;

        var installDir = steam?.FindAppInstallDir(game.SteamAppId, game.InstallFolderName);
        var dlc = installDir == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : Directory.EnumerateFiles(installDir, "dlc_*.scs").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var install = new GameInstallation(
            game,
            docs,
            installDir,
            steam?.WorkshopDirs(game.SteamAppId) ?? [],
            steam?.RemoteDirs(game.SteamAppId) ?? [],
            null,
            dlc);
        return install with { GameVersion = ReadGameVersion(install) };
    }

    static IEnumerable<string> DocumentsCandidates(GameDefinition game, string? documentsOverride)
    {
        if (!string.IsNullOrWhiteSpace(documentsOverride)) yield return documentsOverride;
        // MyDocuments resolves the Documents known folder, including OneDrive redirection.
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), game.DocumentsFolderName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", game.DocumentsFolderName);
    }

    /// <summary>A real game documents folder has config.cfg or a profiles folder, not just a leftover mod folder.</summary>
    public static bool IsGameDocumentsDir(string dir) =>
        File.Exists(Path.Combine(dir, "config.cfg")) || Directory.Exists(Path.Combine(dir, "profiles"));

    /// <summary>Game version from the last log ("Loaded pack set version 1.61.1.1"), else the exe's file version.</summary>
    public static string? ReadGameVersion(GameInstallation install)
    {
        try
        {
            if (File.Exists(install.LogPath))
            {
                using var stream = new FileStream(install.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                for (int i = 0; i < 200 && reader.ReadLine() is { } line; i++)
                {
                    var m = PackSetVersionRegex().Match(line);
                    if (m.Success) return m.Groups[1].Value;
                }
            }
        }
        catch (IOException) { }

        if (install.ExePath is { } exe && File.Exists(exe))
        {
            var v = FileVersionInfo.GetVersionInfo(exe);
            if (v.FileMajorPart > 0) return $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}";
        }
        return null;
    }

    [GeneratedRegex(@"Loaded pack set version (\d+\.\d+(?:\.\d+)*)")]
    private static partial Regex PackSetVersionRegex();
}
