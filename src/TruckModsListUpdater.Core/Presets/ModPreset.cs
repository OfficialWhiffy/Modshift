using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Presets;

public sealed record PresetEntry(string PackageId, string DisplayName, string? Version = null);

/// <summary>
/// A named mod list that can be applied to any profile and shared as a .json file.
/// <see cref="Mods"/> is in UI order: the first entry has the HIGHEST priority (top of the in-game list).
/// </summary>
public sealed record ModPreset
{
    public const int CurrentSchema = 1;

    public int Schema { get; init; } = CurrentSchema;
    public required string Name { get; init; }
    public required string Game { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public required IReadOnlyList<PresetEntry> Mods { get; init; }

    /// <summary>Shipped with the app (see <see cref="BuiltInPresets"/>); never stored in preset files.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    /// <summary>Creates a preset from a mod list in file order (index 0 = lowest priority).</summary>
    public static ModPreset FromFileOrder(string name, string game, IEnumerable<ActiveModEntry> fileOrder, ModCatalog? catalog = null) => new()
    {
        Name = name,
        Game = game,
        Mods = LoadOrderMapper.ToDisplayOrder(fileOrder)
            .Select(e => new PresetEntry(e.PackageId, e.DisplayName, catalog?.Find(e.PackageId)?.Manifest?.PackageVersion))
            .ToList(),
    };
}

public sealed class PresetFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Stores presets as one JSON file each in %APPDATA%\Modshift\presets.</summary>
public sealed class PresetStore(string dir)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public IReadOnlyList<ModPreset> List(string game)
    {
        if (!Directory.Exists(dir)) return [];
        var presets = new List<ModPreset>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var p = Parse(File.ReadAllText(file));
                if (p.Game == game) presets.Add(p);
            }
            catch (Exception e) when (e is PresetFormatException or IOException) { }
        }
        return presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(ModPreset preset)
    {
        Directory.CreateDirectory(dir);
        var path = PathFor(preset.Name);
        File.WriteAllText(path + ".tmp", Serialize(preset));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    public void Delete(string name)
    {
        if (File.Exists(PathFor(name))) File.Delete(PathFor(name));
    }

    public void Rename(ModPreset preset, string newName)
    {
        if (Exists(newName)) throw new PresetFormatException($"A preset called \"{newName}\" already exists.");
        Save(preset with { Name = newName });
        Delete(preset.Name);
    }

    public static void Export(ModPreset preset, string path) => File.WriteAllText(path, Serialize(preset));

    /// <summary>Reads a shared preset file; the caller decides whether to save it.</summary>
    public static ModPreset Import(string path) => Parse(File.ReadAllText(path));

    public static string Serialize(ModPreset preset) => JsonSerializer.Serialize(preset, Json);

    public static ModPreset Parse(string json)
    {
        ModPreset? preset;
        try
        {
            preset = JsonSerializer.Deserialize<ModPreset>(json, Json);
        }
        catch (JsonException e)
        {
            throw new PresetFormatException("This is not a Modshift preset file: " + e.Message, e);
        }
        if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.Mods == null)
            throw new PresetFormatException("The preset file is incomplete (name or mods missing).");
        if (preset.Schema > ModPreset.CurrentSchema)
            throw new PresetFormatException("This preset was made by a newer version of Modshift.");
        if (preset.Mods.Any(m => string.IsNullOrWhiteSpace(m.PackageId) || m.PackageId.Contains('|') || m.PackageId.Contains('"')))
            throw new PresetFormatException("The preset contains an invalid mod id.");
        return preset;
    }

    string PathFor(string name) => Path.Combine(dir, Slug(name) + ".json");

    /// <summary>File-name-safe version of a preset name (unique per name, case-insensitively).</summary>
    static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "preset" : slug.Length > 60 ? slug[..60] : slug;
    }
}

public sealed record PresetApplyResult(
    IReadOnlyList<ActiveModEntry> FileOrder,
    IReadOnlyList<(PresetEntry Entry, ModPackage Package)> Substituted,
    IReadOnlyList<PresetEntry> Missing);

public static class PresetApplier
{
    /// <summary>
    /// Resolves a preset against the installed mods: exact package id first, otherwise another
    /// version of the same mod (newest). Mods that are not installed are reported, not added.
    /// </summary>
    public static PresetApplyResult Resolve(ModPreset preset, ModCatalog catalog)
    {
        var display = new List<ActiveModEntry>();
        var substituted = new List<(PresetEntry, ModPackage)>();
        var missing = new List<PresetEntry>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in preset.Mods)
        {
            var package = catalog.Find(entry.PackageId) ?? MissingModResolver.FindInstalled(entry.PackageId, entry.DisplayName, catalog);
            if (package == null) { missing.Add(entry); continue; }
            if (!used.Add(package.PackageId)) continue;
            if (!package.PackageId.Equals(entry.PackageId, StringComparison.OrdinalIgnoreCase)) substituted.Add((entry, package));
            var name = package.PackageId.Equals(entry.PackageId, StringComparison.OrdinalIgnoreCase) ? entry.DisplayName : package.DisplayName;
            display.Add(ActiveModEntry.Create(package.PackageId, name));
        }
        return new PresetApplyResult(LoadOrderMapper.ToFileOrder(display), substituted, missing);
    }
}
