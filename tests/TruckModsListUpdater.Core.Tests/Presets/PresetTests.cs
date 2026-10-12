using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Presets;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;

namespace TruckModsListUpdater.Core.Tests.Presets;

public class PresetTests
{
    static ModPackage P(string id, string? name = null, string? version = null, bool workshop = false) => new()
    {
        PackageId = id, Source = workshop ? ModSource.Workshop : ModSource.Local, Kind = PackageKind.Zip, Path = id + ".scs",
        Manifest = name == null ? null : new ModManifest { DisplayName = name, PackageVersion = version, Author = "Team" },
    };

    static readonly ModCatalog Catalog = new([
        P("promods-eu-map-v284", "ProMods Map Package", "2.84"),
        P("promods-eu-def-v284", "ProMods Definition Package", "2.84"),
        P("53RXF1"),
        P("mod_workshop_package.00000000B70CC741", "Asphalt", workshop: true),
    ]);

    [Fact]
    public void FromFileOrder_StoresHighestPriorityFirst()
    {
        var preset = ModPreset.FromFileOrder("P", "ets2", [ActiveModEntry.Create("low", "Low"), ActiveModEntry.Create("high", "High")]);
        preset.Mods.Select(m => m.PackageId).Should().Equal("high", "low");
    }

    [Fact]
    public void Store_SaveListRenameDelete()
    {
        using var tmp = new TempDir();
        var store = new PresetStore(tmp.Path);
        var preset = ModPreset.FromFileOrder("ProMods / TruckersMP", "ets2", [ActiveModEntry.Create("a", "A")]);
        store.Save(preset);
        store.Save(ModPreset.FromFileOrder("ATS list", "ats", []));

        store.List("ets2").Should().ContainSingle().Which.Name.Should().Be("ProMods / TruckersMP");
        store.Rename(store.List("ets2")[0], "Convoy");
        store.List("ets2").Select(p => p.Name).Should().Equal("Convoy");
        store.Delete("Convoy");
        store.List("ets2").Should().BeEmpty();
    }

    [Fact]
    public void ExportImport_RoundTrips()
    {
        using var tmp = new TempDir();
        var preset = ModPreset.FromFileOrder("Share me", "ets2", [ActiveModEntry.Create("x", "X \"quoted\"")]);
        var path = Path.Combine(tmp.Path, "share.json");
        PresetStore.Export(preset, path);

        var back = PresetStore.Import(path);
        back.Name.Should().Be("Share me");
        back.Mods.Should().Equal(preset.Mods);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"name\":\"x\",\"game\":\"ets2\"}")]
    [InlineData("{\"schema\":99,\"name\":\"x\",\"game\":\"ets2\",\"mods\":[]}")]
    [InlineData("{\"name\":\"x\",\"game\":\"ets2\",\"mods\":[{\"packageId\":\"a|b\",\"displayName\":\"x\"}]}")]
    public void Parse_RejectsInvalidFiles(string json)
    {
        var act = () => PresetStore.Parse(json);
        act.Should().Throw<PresetFormatException>();
    }

    [Fact]
    public void Resolve_ExactSubstitutedAndMissing()
    {
        var preset = new ModPreset
        {
            Name = "Old ProMods", Game = "ets2",
            Mods =
            [
                new PresetEntry("mod_workshop_package.00000000B70CC741", "Asphalt"),
                new PresetEntry("promods-map-v240", "ProMods Map Package"),
                new PresetEntry("53RXF1", "53RXF1"),
                new PresetEntry("gone-forever", "Gone"),
            ],
        };

        var result = PresetApplier.Resolve(preset, Catalog);

        LoadOrderMapper.ToDisplayOrder(result.FileOrder).Select(e => e.PackageId)
            .Should().Equal("mod_workshop_package.00000000B70CC741", "promods-eu-map-v284", "53RXF1");
        result.Substituted.Should().ContainSingle().Which.Package.PackageId.Should().Be("promods-eu-map-v284");
        result.Missing.Select(m => m.PackageId).Should().Equal("gone-forever");
    }

    [Fact]
    public void MissingModResolver_SuggestsInstalledVersions()
    {
        var mods = new[]
        {
            ActiveModEntry.Create("promods-def-st-v240", "ProMods Definition Package"),
            ActiveModEntry.Create("promods-map-v240", "ProMods Map Package"),
            ActiveModEntry.Create("mod_workshop_package.0000000000000001", "Workshop gone"),
            ActiveModEntry.Create("53RXF1", "53RXF1"),
        };

        var suggestions = MissingModResolver.Suggest(mods, Catalog);

        suggestions.Select(s => (s.Missing.PackageId, s.Replacement.PackageId)).Should().Equal(
            ("promods-def-st-v240", "promods-eu-def-v284"),
            ("promods-map-v240", "promods-eu-map-v284"));
    }

    [Fact]
    public void BuiltIns_LoadForEts2Only()
    {
        var presets = BuiltInPresets.List("ets2");

        presets.Select(p => (p.Name, p.Mods.Count)).Should().Equal(("ProMods", 25));
        presets.Should().OnlyContain(p => p.IsBuiltIn);
        BuiltInPresets.List("ats").Should().BeEmpty();
        BuiltInPresets.IsBuiltInName("ets2", " promods ").Should().BeTrue();
        BuiltInPresets.IsBuiltInName("ats", "ProMods").Should().BeFalse();
    }

    [Fact]
    public void BuiltIns_ResolveNewerProModsVersion()
    {
        var proMods = BuiltInPresets.List("ets2").Single(p => p.Name == "ProMods");
        var catalog = new ModCatalog([P("promods-eu-map-v290", "ProMods Map Package", "2.90")]);

        var result = PresetApplier.Resolve(proMods, catalog);

        result.Substituted.Should().ContainSingle().Which.Package.PackageId.Should().Be("promods-eu-map-v290");
    }

    [Fact]
    public void Serialize_OmitsIsBuiltIn()
    {
        var json = PresetStore.Serialize(BuiltInPresets.List("ets2")[0]);

        json.Should().NotContain("isBuiltIn");
        PresetStore.Parse(json).IsBuiltIn.Should().BeFalse();
    }
}
