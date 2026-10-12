using System.Text;
using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Mods;

/// <summary>The mod_package unit of a mod's manifest.sii.</summary>
public sealed record ModManifest
{
    public string? DisplayName { get; init; }
    public string? PackageVersion { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string? IconFile { get; init; }
    public string? DescriptionFile { get; init; }
    public IReadOnlyList<string> CompatibleVersions { get; init; } = [];
    public IReadOnlyList<string> DlcDependencies { get; init; } = [];
    public bool MultiplayerOptional { get; init; }

    public static ModManifest FromUnit(SiiUnit unit) => new()
    {
        DisplayName = NullIfEmpty(unit.Get("display_name")),
        PackageVersion = NullIfEmpty(unit.Get("package_version")),
        Author = NullIfEmpty(unit.Get("author")),
        Categories = unit.GetArray("category"),
        IconFile = NullIfEmpty(unit.Get("icon")),
        DescriptionFile = NullIfEmpty(unit.Get("description_file")),
        CompatibleVersions = unit.GetArray("compatible_versions"),
        DlcDependencies = unit.GetArray("dlc_dependencies"),
        MultiplayerOptional = unit.GetBool("mp_mod_optional"),
    };

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>What could be read from a package: manifest (if any), description text and icon bytes.</summary>
public sealed record PackageMetadata(ModManifest? Manifest, string? Description, byte[]? Icon, string? IconExtension);

public static class ManifestReader
{
    /// <summary>
    /// Reads manifest.sii (plain, ScsC or 3nK encoded), its description and icon. Packages without a
    /// manifest (older mods) still get their root icon.jpg if present.
    /// </summary>
    public static PackageMetadata Read(IPackageReader reader)
    {
        ModManifest? manifest = null;
        if (reader.TryReadFile("manifest.sii", out var data))
        {
            var unit = SiiCodec.Decode(data).ReadUnits().FirstOrDefault(u => u.Type == "mod_package");
            if (unit != null) manifest = ModManifest.FromUnit(unit);
        }

        string? description = null;
        if (manifest?.DescriptionFile is { } descFile && reader.TryReadFile(descFile, out var descBytes))
            description = NormalizeDescription(Encoding.UTF8.GetString(SiiCodec.StripBoms(descBytes)));

        var iconFile = manifest?.IconFile ?? "icon.jpg";
        byte[]? icon = reader.TryReadFile(iconFile, out var iconBytes) && iconBytes.Length > 0 ? iconBytes : null;
        var ext = icon == null ? null : Path.GetExtension(iconFile).ToLowerInvariant() is { Length: > 0 } e ? e : ".jpg";

        return new PackageMetadata(manifest, description, icon, ext);
    }

    /// <summary>
    /// Keeps the game's colour/format tags ("[orange]", "[COLOR=#ff0000]") in the text;
    /// <see cref="ModDescription"/> turns them into styled lines when the description is shown.
    /// </summary>
    static string NormalizeDescription(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
}
