using System.Text;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Profiles;

public class ProfileWriteException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class GameRunningException(GameDefinition game)
    : ProfileWriteException($"{game.DisplayName} is running. Close the game first: it overwrites profile.sii when it exits.");

public sealed class ProfileChangedException()
    : ProfileWriteException("profile.sii was changed by something else (the game or Steam) after it was loaded. Reload the profile and try again.");

public sealed record ProfileWriteResult(ProfileBackup Backup, ProfileSession Session);

/// <summary>
/// The only code that writes profile.sii. Every write:
/// 1. refuses while the game runs, and for profiles that were not decoded as text;
/// 2. refuses when the file changed since it was loaded;
/// 3. zips the profile folder to the app's backup folder;
/// 4. writes the new text (plain SiiNunit, UTF-8 without BOM) to a temp file next to it,
///    reads it back and checks that only the active_mods block changed;
/// 5. checks the game again and swaps the temp file in.
/// The game reads plain-text profiles and re-encrypts them itself on its next save.
/// profile.bak.sii is left alone so the game keeps its own fallback.
/// </summary>
public sealed class ProfileWriter(IGameProcessMonitor monitor, ProfileBackups backups)
{
    public const string TempSuffix = ".tmm.tmp";

    public ProfileWriteResult WriteActiveMods(GameDefinition game, ProfileSession session, IReadOnlyList<ActiveModEntry> fileOrder)
    {
        if (!session.IsWritable || session.Document.Text is not { } originalText)
            throw new ProfileWriteException("This profile is stored in binary format and cannot be edited. Set g_save_format to 0 or 2 and save the profile in game once.");
        if (monitor.IsRunning(game)) throw new GameRunningException(game);

        var path = session.Profile.SiiPath;
        var current = FileFingerprint.Of(path);
        if (current.Sha256 != session.Fingerprint.Sha256) throw new ProfileChangedException();

        var backup = backups.Create(game, session.Profile);

        var newText = ActiveModsBlock.Replace(originalText, fileOrder);
        var newBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(newText);
        var tmp = path + TempSuffix;
        try
        {
            File.WriteAllBytes(tmp, newBytes);
            Verify(File.ReadAllBytes(tmp), originalText, fileOrder);

            if (monitor.IsRunning(game)) throw new GameRunningException(game);
            if (FileFingerprint.Of(path).Sha256 != current.Sha256) throw new ProfileChangedException();
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }

        return new ProfileWriteResult(backup, ProfileSession.Load(session.Profile));
    }

    /// <summary>Restores profile.sii from a backup, with the same safety checks (and a backup of the current state first).</summary>
    public ProfileWriteResult Restore(GameDefinition game, ProfileRef profile, ProfileBackup backup)
    {
        if (monitor.IsRunning(game)) throw new GameRunningException(game);
        var bytes = ProfileBackups.ReadProfileSii(backup);
        SiiCodec.Decode(bytes); // refuse to restore something that is not a profile

        var safety = backups.Create(game, profile);
        var tmp = profile.SiiPath + TempSuffix;
        try
        {
            File.WriteAllBytes(tmp, bytes);
            if (monitor.IsRunning(game)) throw new GameRunningException(game);
            File.Move(tmp, profile.SiiPath, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
        return new ProfileWriteResult(safety, ProfileSession.Load(profile));
    }

    static void Verify(byte[] written, string originalText, IReadOnlyList<ActiveModEntry> expected)
    {
        try
        {
            var doc = SiiCodec.Decode(written);
            var text = doc.Text ?? throw new ProfileWriteException("Verification failed: written profile is not text.");
            var entries = ActiveModsBlock.Parse(text);
            if (!entries.Select(e => e.RawValue).SequenceEqual(expected.Select(e => e.RawValue)))
                throw new ProfileWriteException("Verification failed: the written mod list differs from the intended one.");
            if (ActiveModsBlock.WithoutBlock(text) != ActiveModsBlock.WithoutBlock(originalText))
                throw new ProfileWriteException("Verification failed: something other than the mod list would change.");
            if (SiiText.Parse(text).All(u => u.Type != "user_profile"))
                throw new ProfileWriteException("Verification failed: the user_profile unit is missing.");
        }
        catch (SiiFormatException e)
        {
            throw new ProfileWriteException("Verification failed: " + e.Message, e);
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Removes temp files left by an interrupted write.</summary>
    public static void CleanUpTempFiles(ProfileRef profile) => TryDelete(profile.SiiPath + TempSuffix);
}
