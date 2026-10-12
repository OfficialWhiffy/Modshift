namespace TruckModsListUpdater.Core.Sii;

/// <summary>One SII unit ("type : id { ... }") with its attributes as strings.</summary>
public sealed class SiiUnit(string type, string id)
{
    public string Type { get; } = type;
    public string Id { get; } = id;
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> Arrays { get; } = new(StringComparer.Ordinal);

    public string? Get(string name) => Values.GetValueOrDefault(name);

    public IReadOnlyList<string> GetArray(string name) =>
        Arrays.TryGetValue(name, out var list) ? list : [];

    public bool GetBool(string name) => Get(name) is "true" or "1";
}
