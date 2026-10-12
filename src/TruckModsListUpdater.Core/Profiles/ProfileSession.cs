using System.Security.Cryptography;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Profiles;

/// <summary>Identifies the exact bytes a profile was loaded from, to detect changes made by the game or Steam.</summary>
public sealed record FileFingerprint(long Length, DateTime LastWriteUtc, string Sha256)
{
    public static FileFingerprint Of(string path, byte[] bytes) =>
        new(bytes.LongLength, File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(bytes)));

    public static FileFingerprint Of(string path) => Of(path, File.ReadAllBytes(path));
}

/// <summary>A loaded profile: its decoded document and active mods (file order: index 0 = lowest priority).</summary>
public sealed class ProfileSession
{
    public required ProfileRef Profile { get; init; }
    public required SiiDocument Document { get; init; }
    public required FileFingerprint Fingerprint { get; init; }
    public required IReadOnlyList<ActiveModEntry> ActiveMods { get; init; }
    public string? ProfileName { get; init; }

    /// <summary>Text profiles can be edited; binary ones (rare, g_save_format 1) are read-only.</summary>
    public bool IsWritable => Document.IsWritable;

    public static ProfileSession Load(ProfileRef profile)
    {
        var bytes = File.ReadAllBytes(profile.SiiPath);
        var doc = SiiCodec.Decode(bytes);

        IReadOnlyList<ActiveModEntry> mods;
        string? name;
        if (doc.Content == SiiContent.Text)
        {
            mods = ActiveModsBlock.Parse(doc.Text!);
            name = SiiText.Parse(doc.Text!).FirstOrDefault(u => u.Type == "user_profile")?.Get("profile_name");
        }
        else
        {
            var unit = doc.ReadUnits().FirstOrDefault(u => u.Type == "user_profile");
            mods = unit?.GetArray("active_mods").Select(ActiveModEntry.FromValue).ToList() ?? [];
            name = unit?.Get("profile_name");
        }

        return new ProfileSession
        {
            Profile = profile,
            Document = doc,
            Fingerprint = FileFingerprint.Of(profile.SiiPath, bytes),
            ActiveMods = mods,
            ProfileName = name,
        };
    }
}
