using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;
using static TruckModsListUpdater.Core.Tests.Fixtures.PackageBuilder;

namespace TruckModsListUpdater.Core.Tests.Mods;

public class ModIdentityTests
{
    [Theory]
    [InlineData("promods-eu-map-v284", "promods-eu-map")]
    [InlineData("promods-eu-model1-v284", "promods-eu-model1")]
    [InlineData("53RXF1", "rxf1")]
    [InlineData("ROEX53PMME284", "roexpmme")]
    [InlineData("cnx-pm-eu-v284-v104", "cnx-pm-eu")]
    [InlineData("161MaghrebFix", "maghrebfix")]
    [InlineData("realistic_mod_1.2.3", "realistic_mod")]
    public void Key_StripsVersions_KeepsPartNumbers(string id, string expected)
    {
        ModIdentity.Key(id).Should().Be(expected);
    }

    static ModPackage P(string id, string? version = null, string? name = null, string? author = null) => new()
    {
        PackageId = id, Source = ModSource.Local, Kind = PackageKind.Zip, Path = id + ".scs",
        Manifest = version == null && name == null ? null : new ModManifest { PackageVersion = version, DisplayName = name, Author = author },
    };

    [Fact]
    public void IsSameMod()
    {
        ModIdentity.IsSameMod(P("promods-eu-map-v283"), P("promods-eu-map-v284")).Should().BeTrue();
        ModIdentity.IsSameMod(P("53RXF1"), P("53RXF2")).Should().BeFalse();
        ModIdentity.IsSameMod(P("promods-eu-map-v284"), P("promods-eu-map-v284")).Should().BeFalse();
        ModIdentity.IsSameMod(P("a", name: "ProMods Map Package", author: "ProMods Team"), P("b", name: "ProMods Map Package", author: "ProMods Team")).Should().BeTrue();
        ModIdentity.IsSameMod(P("a", name: "Same", author: "One"), P("b", name: "Same", author: "Other")).Should().BeFalse();
    }

    [Theory]
    [InlineData("promods-eu-map-v284", null, "promods-eu-map-v283", null, VersionComparison.Newer)]
    [InlineData("50RXF1", null, "53RXF1", null, VersionComparison.Older)]
    [InlineData("x", "2.10", "y", "2.9", VersionComparison.Newer)]
    [InlineData("mod-v2", "1.0", "mod-v3", "1.0", VersionComparison.Older)]
    public void Compare(string a, string? va, string b, string? vb, VersionComparison expected)
    {
        ModIdentity.Compare(P(a, va), P(b, vb)).Should().Be(expected);
    }
}

public class InstallAndArchiveTests : IDisposable
{
    readonly TempDir _tmp = new();
    readonly GameInstallation _install;
    readonly ModArchive _archive;

    public InstallAndArchiveTests()
    {
        _install = new GameInstallation(GameDefinition.Ets2, _tmp.Dir("docs"), null, [], [], "1.61.1.1", new HashSet<string>());
        Directory.CreateDirectory(_install.ModDir);
        _archive = new ModArchive(_install.ArchiveDir);
    }

    public void Dispose() => _tmp.Dispose();

    Task<ModCatalog> Scan() => new ModScanner(new ModMetadataCache(_tmp.Dir("cache-" + Guid.NewGuid().ToString("N")[..6]))).ScanAsync(_install);

    static Dictionary<string, byte[]> Mod(string name, string version = "1.0") => new() { ["manifest.sii"] = Utf8(Manifest(name, version)), ["icon.jpg"] = [1, 2, 3] };

    [Fact]
    public async Task InstallsLooseScs_AndZippedDownloadWithSeveralMods()
    {
        Zip(_tmp.File("downloads/single.scs"), Mod("Single"));
        var inner1 = _tmp.File("build/part1.scs");
        var inner2 = _tmp.File("build/part2.scs");
        HashFs(inner1, Mod("Part 1"));
        Zip(inner2, Mod("Part 2"));
        Zip(_tmp.File("downloads/pack.zip"), new Dictionary<string, byte[]>
        {
            ["Pack v1/part1.scs"] = File.ReadAllBytes(inner1),
            ["Pack v1/part2.scs"] = File.ReadAllBytes(inner2),
            ["Pack v1/readme.txt"] = Utf8("read me"),
        });

        var installer = new ModInstaller(_install, _archive);
        var plan = await installer.AnalyzeAsync([_tmp.File("downloads/single.scs"), _tmp.File("downloads/pack.zip")], await Scan());

        plan.Problems.Should().BeEmpty();
        plan.Candidates.Select(c => c.TargetName).Should().BeEquivalentTo("single.scs", "part1.scs", "part2.scs");
        plan.Candidates.Should().OnlyContain(c => c.Action == InstallAction.Install && c.Conflict == InstallConflict.None);
        plan.Candidates.Single(c => c.TargetName == "part1.scs").Package.DisplayName.Should().Be("Part 1");
        plan.Candidates.Single(c => c.TargetName == "part1.scs").SourceDescription.Should().Be("pack.zip › part1.scs");

        installer.Execute(plan);

        Directory.GetFiles(_install.ModDir).Select(Path.GetFileName).Should().BeEquivalentTo("single.scs", "part1.scs", "part2.scs");
        Directory.Exists(plan.StagingDir).Should().BeFalse();
    }

    [Fact]
    public async Task NewerVersion_ReplacesAndArchivesOld()
    {
        Zip(Path.Combine(_install.ModDir, "coolmod-v10.scs"), Mod("Cool Mod", "1.0"));
        Zip(_tmp.File("downloads/coolmod-v11.scs"), Mod("Cool Mod", "1.1"));

        var installer = new ModInstaller(_install, _archive);
        var plan = await installer.AnalyzeAsync([_tmp.File("downloads/coolmod-v11.scs")], await Scan());

        var c = plan.Candidates.Single();
        c.Conflict.Should().Be(InstallConflict.NewerVersionOf);
        c.Action.Should().Be(InstallAction.Replace);
        c.Existing!.PackageId.Should().Be("coolmod-v10");

        var result = installer.Execute(plan).Single();

        Directory.GetFiles(_install.ModDir).Select(Path.GetFileName).Should().Equal("coolmod-v11.scs");
        result.Replaced!.Name.Should().Be("coolmod-v10.scs");
        File.Exists(result.Replaced.ArchivedPath).Should().BeTrue();
        _archive.List().Should().ContainSingle();
    }

    [Fact]
    public async Task IdenticalFile_IsSkipped_OlderVersionIsSkipped()
    {
        Zip(Path.Combine(_install.ModDir, "same.scs"), Mod("Same"));
        File.Copy(Path.Combine(_install.ModDir, "same.scs"), _tmp.File("downloads/same.scs"));
        Zip(Path.Combine(_install.ModDir, "tool-v20.scs"), Mod("Tool", "2.0"));
        Zip(_tmp.File("downloads/tool-v19.scs"), Mod("Tool", "1.9"));

        var plan = await new ModInstaller(_install, _archive).AnalyzeAsync(
            [_tmp.File("downloads/same.scs"), _tmp.File("downloads/tool-v19.scs")], await Scan());

        plan.Candidates.Single(c => c.TargetName == "same.scs").Conflict.Should().Be(InstallConflict.AlreadyInstalled);
        plan.Candidates.Single(c => c.TargetName == "tool-v19.scs").Conflict.Should().Be(InstallConflict.OlderVersionOf);
        plan.Candidates.Should().OnlyContain(c => c.Action == InstallAction.Skip);
    }

    [Fact]
    public async Task UnsupportedAndNonModFiles_AreReportedAsProblems()
    {
        _tmp.File("downloads/mod.rar", "rar");
        _tmp.File("downloads/notes.txt", "hello");
        Zip(_tmp.File("downloads/pictures.zip"), new Dictionary<string, byte[]> { ["a.png"] = [1] });

        var plan = await new ModInstaller(_install, _archive).AnalyzeAsync(
            [_tmp.File("downloads/mod.rar"), _tmp.File("downloads/notes.txt"), _tmp.File("downloads/pictures.zip")], await Scan());

        plan.Candidates.Should().BeEmpty();
        plan.Problems.Should().HaveCount(3);
        plan.Problems[0].Should().Contain("Extract it first");
    }

    [Fact]
    public async Task ZippedModFolder_IsInstalledAsFolderMod()
    {
        Zip(_tmp.File("downloads/foldermod.zip"), new Dictionary<string, byte[]>
        {
            ["MyFolderMod/manifest.sii"] = Utf8(Manifest("Folder Mod")),
            ["MyFolderMod/def/x.sii"] = Utf8("SiiNunit { }"),
        });
        var installer = new ModInstaller(_install, _archive);
        var plan = await installer.AnalyzeAsync([_tmp.File("downloads/foldermod.zip")], await Scan());

        plan.Candidates.Single().TargetName.Should().Be("MyFolderMod");
        installer.Execute(plan);
        File.Exists(Path.Combine(_install.ModDir, "MyFolderMod", "def", "x.sii")).Should().BeTrue();
    }

    [Fact]
    public async Task Archive_AndRestore_BrokenFolder()
    {
        _tmp.File("docs/mod/old/a.scs", "x");
        var catalog = await Scan();
        var suggestions = ModArchive.SuggestCleanup(catalog, []);
        var broken = suggestions.Single(s => s.Reason == CleanupReason.BrokenFolder);
        broken.Recommended.Should().BeTrue();

        var item = _archive.Archive(broken.Package.Path, "Broken folder");
        Directory.Exists(Path.Combine(_install.ModDir, "old")).Should().BeFalse();
        Directory.Exists(item.ArchivedPath).Should().BeTrue();
        _archive.List().Should().ContainSingle().Which.Reason.Should().Be("Broken folder");

        _archive.Restore(item);
        Directory.Exists(Path.Combine(_install.ModDir, "old")).Should().BeTrue();
        _archive.List().Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_RefusesToOverwrite()
    {
        var path = Path.Combine(_install.ModDir, "m.scs");
        Zip(path, Mod("M"));
        var item = _archive.Archive(path, "test");
        Zip(path, Mod("M again"));

        var act = () => _archive.Restore(item);
        act.Should().Throw<ModArchiveException>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SuggestCleanup_OldVersionsAndUnused()
    {
        Zip(Path.Combine(_install.ModDir, "promods-eu-map-v283.scs"), Mod("ProMods Map Package", "2.83"));
        Zip(Path.Combine(_install.ModDir, "promods-eu-map-v284.scs"), Mod("ProMods Map Package", "2.84"));
        Zip(Path.Combine(_install.ModDir, "spare.scs"), Mod("Spare"));
        var used = new List<ActiveModEntry> { ActiveModEntry.Create("promods-eu-map-v284", "Map") };

        var suggestions = ModArchive.SuggestCleanup(await Scan(), [used]);

        suggestions.Single(s => s.Package.PackageId == "promods-eu-map-v283").Should().Match<CleanupSuggestion>(s => s.Reason == CleanupReason.OlderVersion && s.Recommended);
        suggestions.Single(s => s.Package.PackageId == "spare").Should().Match<CleanupSuggestion>(s => s.Reason == CleanupReason.Unused && !s.Recommended);
        suggestions.Should().NotContain(s => s.Package.PackageId == "promods-eu-map-v284");
    }

    [Fact]
    public void ReferenceUpdater_KeepsPosition_AndDeduplicates()
    {
        var mods = new[] { ActiveModEntry.Create("a", "A"), ActiveModEntry.Create("old", "Old"), ActiveModEntry.Create("new", "New"), ActiveModEntry.Create("z", "Z") };
        var result = ProfileReferenceUpdater.Apply(mods, [new ModReplacement("old", "new", "New v2")]);
        result.Select(m => m.PackageId).Should().Equal("a", "new", "z");
        result[1].DisplayName.Should().Be("New v2");
    }
}
