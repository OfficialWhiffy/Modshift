namespace TruckModsListUpdater.Core.Presets;

/// <summary>
/// Read-only presets embedded in the app (Presets\BuiltIn\*.json). Package ids carry the version
/// they were made with; <see cref="PresetApplier"/> maps them to newer installed versions.
/// </summary>
public static class BuiltInPresets
{
    const string ResourcePrefix = "TruckModsListUpdater.Core.Presets.BuiltIn.";

    static readonly Lazy<IReadOnlyList<ModPreset>> All = new(Load);

    public static IReadOnlyList<ModPreset> List(string game) =>
        All.Value.Where(p => p.Game == game).ToList();

    public static bool IsBuiltInName(string game, string name) =>
        All.Value.Any(p => p.Game == game && p.Name.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    static IReadOnlyList<ModPreset> Load()
    {
        var assembly = typeof(BuiltInPresets).Assembly;
        var presets = new List<ModPreset>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            presets.Add(PresetStore.Parse(reader.ReadToEnd()) with { IsBuiltIn = true });
        }
        return presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
