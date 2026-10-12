using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Tests.Fixtures;
using static TruckModsListUpdater.Core.Tests.Fixtures.PackageBuilder;

namespace TruckModsListUpdater.Core.Tests.Mods;

public class ModScannerTests
{
    static GameInstallation Install(TempDir tmp, string? gameVersion = "1.61.1.1", params string[] workshopDirs) =>
        new(GameDefinition.Ets2, tmp.Dir("docs"), null, workshopDirs, [], gameVersion, new HashSet<string>());

    [Fact]
    public async Task Scan_FindsLocalPackages_FolderMods_AndBrokenFolders()
    {
        using var tmp = new TempDir();
        var install = Install(tmp);
        Zip(tmp.File("docs/mod/zipmod.scs"), new Dictionary<string, byte[]> { ["manifest.sii"] = Utf8(Manifest("Zip Mod")) });
        HashFs(tmp.File("docs/mod/oldmod.scs"), new Dictionary<string, byte[]> { ["def/a.sii"] = Utf8("x") });
        tmp.File("docs/mod/foldermod/manifest.sii", Manifest("Folder Mod"));
        tmp.File("docs/mod/old/something.scs", "x");
        tmp.File("docs/mod/readme.txt", "ignored");

        var catalog = await new ModScanner(new ModMetadataCache(tmp.Dir("cache"))).ScanAsync(install);

        catalog.Packages.Select(p => p.PackageId).Should().BeEquivalentTo("zipmod", "oldmod", "foldermod", "old");
        catalog.Find("zipmod")!.DisplayName.Should().Be("Zip Mod");
        catalog.Find("oldmod")!.Issues.Should().ContainSingle(i => i.Kind == ModIssueKind.NoManifest);
        catalog.Find("oldmod")!.DisplayName.Should().Be("oldmod");
        catalog.Find("foldermod")!.DisplayName.Should().Be("Folder Mod");
        catalog.Find("old")!.Issues.Should().ContainSingle(i => i.Kind == ModIssueKind.BrokenFolder);
    }

    [Fact]
    public async Task Scan_FlagsDuplicateIds_AndUnreadablePackages()
    {
        using var tmp = new TempDir();
        var install = Install(tmp);
        Zip(tmp.File("docs/mod/dup.scs"), new Dictionary<string, byte[]> { ["manifest.sii"] = Utf8(Manifest("A")) });
        Zip(tmp.File("docs/mod/dup.zip"), new Dictionary<string, byte[]> { ["manifest.sii"] = Utf8(Manifest("B")) });
        tmp.File("docs/mod/broken.scs", "not an archive");

        var catalog = await new ModScanner(new ModMetadataCache(tmp.Dir("cache"))).ScanAsync(install);

        catalog.Packages.Where(p => p.PackageId == "dup").Should().HaveCount(2)
            .And.OnlyContain(p => p.Issues.Any(i => i.Kind == ModIssueKind.DuplicateId));
        catalog.Find("broken")!.Issues.Should().ContainSingle(i => i.Kind == ModIssueKind.Unreadable);
    }

    [Fact]
    public async Task Scan_Workshop_PicksPackageForGameVersion()
    {
        using var tmp = new TempDir();
        var workshop = tmp.Dir("workshop/content/227300");
        tmp.File("workshop/content/227300/3071067969/versions.sii", """
            SiiNunit
            {
            package_version_info : .v160 { package_name: "160_content" compatible_versions[]: "1.60.*" }
            package_version_info : .v161 { package_name: "161_content" compatible_versions[]: "1.61.*" }
            package_version_info : .latest { package_name: "latest" }
            }
            """);
        tmp.File("workshop/content/227300/3071067969/160_content/manifest.sii", Manifest("Old"));
        tmp.File("workshop/content/227300/3071067969/161_content/manifest.sii", Manifest("Current"));
        tmp.File("workshop/content/227300/3071067969/latest/manifest.sii", Manifest("Latest"));
        Zip(tmp.File("workshop/content/227300/42/universal.zip"), new Dictionary<string, byte[]> { ["manifest.sii"] = Utf8(Manifest("Universal")) });
        tmp.Dir("workshop/content/227300/7"); // not downloaded yet

        var install = Install(tmp, "1.61.1.1", workshop);
        var catalog = await new ModScanner(new ModMetadataCache(tmp.Dir("cache"))).ScanAsync(install);

        var item = catalog.Find("mod_workshop_package.00000000B70CC741")!;
        item.DisplayName.Should().Be("Current");
        item.WorkshopId.Should().Be(3071067969UL);
        item.Source.Should().Be(ModSource.Workshop);
        catalog.Find("mod_workshop_package.000000000000002A")!.DisplayName.Should().Be("Universal");
        catalog.Find("mod_workshop_package.0000000000000007")!.Issues.Should().ContainSingle(i => i.Kind == ModIssueKind.Unreadable);

        // Unknown future version falls back to the entry without compatible_versions.
        var future = await new ModScanner(new ModMetadataCache(tmp.Dir("cache2"))).ScanAsync(install with { GameVersion = "1.70.0.1" });
        future.Find("mod_workshop_package.00000000B70CC741")!.DisplayName.Should().Be("Latest");
    }

    [Fact]
    public async Task Rescan_UsesCache_UntilFileChanges()
    {
        using var tmp = new TempDir();
        var install = Install(tmp);
        var path = tmp.File("docs/mod/m.scs");
        Zip(path, new Dictionary<string, byte[]> { ["manifest.sii"] = Utf8(Manifest("First")), ["icon.jpg"] = [1, 2, 3] });

        var cacheDir = tmp.Dir("cache");
        var cache = new ModMetadataCache(cacheDir);
        (await new ModScanner(cache).ScanAsync(install)).Find("m")!.IconPath.Should().NotBeNull();
        cache.Save();

        var reloaded = new ModMetadataCache(cacheDir);
        reloaded.Load();
        var cached = (await new ModScanner(reloaded).ScanAsync(install)).Find("m")!;
        cached.DisplayName.Should().Be("First");
        File.ReadAllBytes(cached.IconPath!).Should().Equal(1, 2, 3);

        Zip(path, new Dictionary<string, byte[]> { ["manifest.sii"] = Utf8(Manifest("Second, longer name")) });
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        (await new ModScanner(reloaded).ScanAsync(install)).Find("m")!.DisplayName.Should().Be("Second, longer name");
    }

    [Theory]
    [InlineData("1.61.*", "1.61.1.1", true)]
    [InlineData("1.61.*", "1.60.2.0", false)]
    [InlineData("1.6*", "1.61.1.1", true)]
    [InlineData("1.61", "1.61.1.1", true)]
    [InlineData("1.6", "1.61.1.1", false)]
    public void VersionPattern_Matches(string pattern, string version, bool expected)
    {
        VersionPattern.Matches(pattern, version).Should().Be(expected);
    }

    [Fact]
    public void Compatibility_FlagsWrongVersionAndMissingDlc()
    {
        using var tmp = new TempDir();
        var install = new GameInstallation(GameDefinition.Ets2, tmp.Path, tmp.Path, [], [], "1.61.1.1",
            new HashSet<string>(["dlc_east"], StringComparer.OrdinalIgnoreCase));
        var package = new ModPackage
        {
            PackageId = "m", Source = ModSource.Local, Kind = Core.Archives.PackageKind.Zip, Path = "m.scs",
            Manifest = new ModManifest { CompatibleVersions = ["1.60.*"], DlcDependencies = ["dlc_east", "dlc_iberia"] },
        };

        var issues = CompatibilityChecker.Check(package, install);
        issues.Should().Contain(i => i.Kind == ModIssueKind.IncompatibleVersion);
        issues.Should().ContainSingle(i => i.Kind == ModIssueKind.MissingDlc).Which.Message.Should().Contain("dlc_iberia").And.NotContain("dlc_east");
    }
}
