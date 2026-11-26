using System.Text;
using Moq;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Tests.Fixtures;

namespace TruckModsListUpdater.Core.Tests.Profiles;

public sealed class ProfileConfigCopierTests : IDisposable
{
    readonly TempDir _tmp = new();
    readonly Mock<IGameProcessMonitor> _monitor = new();
    readonly GameInstallation _install;
    readonly ConfigBackups _backups;

    public ProfileConfigCopierTests()
    {
        var docs = _tmp.Dir("docs");
        var remote = _tmp.Dir("steam/remote");
        _install = new GameInstallation(GameDefinition.Ets2, docs, null, [], [remote], null, new HashSet<string>());
        _backups = new ConfigBackups(_tmp.Dir("backups"));
    }

    public void Dispose() => _tmp.Dispose();

    static string Hex(string name) => Convert.ToHexString(Encoding.UTF8.GetBytes(name));

    ProfileConfigCopier Copier() => new(_monitor.Object, _backups);

    /// <summary>A local profile: everything in Documents\profiles\{hex}, plus profile.sii, its fallback and a save.</summary>
    ProfileRef Local(string name, Dictionary<string, string>? files = null)
    {
        var dir = $"docs/profiles/{Hex(name)}";
        _tmp.File($"{dir}/profile.sii", $"SiiNunit {{ {name} }}");
        _tmp.File($"{dir}/profile.bak.sii", $"bak {name}");
        _tmp.File($"{dir}/save/1/game.sii", $"save {name}");
        foreach (var (file, content) in files ?? []) _tmp.File($"{dir}/{file}", content);
        return new ProfileRef(ProfileKind.Local, Path.GetFullPath(Path.Combine(_tmp.Path, dir)), name, DateTime.UtcNow);
    }

    /// <summary>A Steam Cloud profile: config.cfg in the cloud folder, the rest in Documents\steam_profiles\{hex}.</summary>
    ProfileRef Cloud(string name, string? configCfg = null, Dictionary<string, string>? steamProfileFiles = null)
    {
        var dir = $"steam/remote/profiles/{Hex(name)}";
        _tmp.File($"{dir}/profile.sii", $"SiiNunit {{ {name} }}");
        if (configCfg != null) _tmp.File($"{dir}/config.cfg", configCfg);
        foreach (var (file, content) in steamProfileFiles ?? []) _tmp.File($"docs/steam_profiles/{Hex(name)}/{file}", content);
        return new ProfileRef(ProfileKind.SteamCloud, Path.GetFullPath(Path.Combine(_tmp.Path, dir)), name, DateTime.UtcNow);
    }

    string SteamProfilesDir(ProfileRef p) => Path.Combine(_install.DocumentsDir, "steam_profiles", p.FolderName);

    Dictionary<string, byte[]> Snapshot() =>
        Directory.EnumerateFiles(_tmp.Path, "*", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(Path.Combine(_tmp.Path, "backups")))
            .ToDictionary(p => p, File.ReadAllBytes);

    void AssertNoTempFiles() =>
        Directory.EnumerateFiles(_tmp.Path, "*" + ProfileConfigCopier.TempSuffix, SearchOption.AllDirectories).Should().BeEmpty();

    static readonly Dictionary<string, string> FullConfig = new()
    {
        ["config.cfg"] = "uset g_lang \"en\"\r\n",
        ["controls.sii"] = "SiiNunit { controls A }\r\n",
        ["ffb_lut.sii"] = "SiiNunit { ffb A }\r\n",
        ["config_local.cfg"] = "uset s_master_volume \"0.5\"\r\n",
        ["gearbox_layout_scania_12.sii"] = "scania A",
        ["gearbox_layout_zf_16.sii"] = "zf A",
    };

    [Fact]
    public void LocalToLocal_CopiesTheChosenCategories_AndLeavesEverythingElse()
    {
        var source = Local("A", FullConfig);
        var target = Local("B", new() { ["config.cfg"] = "uset g_lang \"de\"\r\n", ["controls.sii"] = "B controls", ["gearbox_layout_old.sii"] = "old" });
        var before = Snapshot();

        var plan = ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Gameplay, ConfigCategory.GearboxLayouts]);
        plan.Files.Select(f => (f.FileName, f.Action)).Should().BeEquivalentTo(new[]
        {
            ("config.cfg", ConfigFileAction.Replace),
            ("gearbox_layout_scania_12.sii", ConfigFileAction.Add),
            ("gearbox_layout_zf_16.sii", ConfigFileAction.Add),
        });

        var result = Copier().Execute(plan);

        result.Written.Should().HaveCount(3);
        File.ReadAllText(Path.Combine(target.Dir, "config.cfg")).Should().Be(FullConfig["config.cfg"]);
        File.ReadAllText(Path.Combine(target.Dir, "gearbox_layout_zf_16.sii")).Should().Be("zf A");
        // Not chosen, not in the source, or never touched:
        File.ReadAllText(Path.Combine(target.Dir, "controls.sii")).Should().Be("B controls");
        File.ReadAllText(Path.Combine(target.Dir, "gearbox_layout_old.sii")).Should().Be("old");
        foreach (var name in new[] { "profile.sii", "profile.bak.sii", Path.Combine("save", "1", "game.sii") })
            File.ReadAllBytes(Path.Combine(target.Dir, name)).Should().Equal(before[Path.Combine(target.Dir, name)]);
        // The source is never changed.
        foreach (var (path, bytes) in before.Where(kv => kv.Key.StartsWith(source.Dir)))
            File.ReadAllBytes(path).Should().Equal(bytes);
        AssertNoTempFiles();
    }

    [Fact]
    public void CloudProfiles_UseTheCloudFolderForConfigCfg_AndSteamProfilesForTheRest()
    {
        var source = Cloud("A", "cfg A", new() { ["controls.sii"] = "controls A", ["config_local.cfg"] = "local A" });
        var target = Cloud("B", "cfg B");

        var plan = ProfileConfigCopier.Plan(_install, source, target, ProfileConfigs.All);
        plan.MissingInSource.Should().Equal(ConfigCategory.GearboxLayouts);
        Copier().Execute(plan);

        File.ReadAllText(Path.Combine(target.Dir, "config.cfg")).Should().Be("cfg A");
        File.ReadAllText(Path.Combine(SteamProfilesDir(target), "controls.sii")).Should().Be("controls A");
        File.ReadAllText(Path.Combine(SteamProfilesDir(target), "config_local.cfg")).Should().Be("local A");
        File.Exists(Path.Combine(target.Dir, "controls.sii")).Should().BeFalse();
    }

    [Fact]
    public void LocalToCloud_PutsFilesWhereTheCloudProfileKeepsThem()
    {
        var source = Local("A", FullConfig);
        var target = Cloud("B");

        Copier().Execute(ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Gameplay, ConfigCategory.Controls]));

        File.Exists(Path.Combine(target.Dir, "config.cfg")).Should().BeTrue();
        File.Exists(Path.Combine(SteamProfilesDir(target), "controls.sii")).Should().BeTrue();
        File.Exists(Path.Combine(SteamProfilesDir(target), "ffb_lut.sii")).Should().BeTrue();
    }

    [Fact]
    public void IdenticalFiles_AreNotWrittenOrBackedUp()
    {
        var source = Local("A", new() { ["config.cfg"] = "same" });
        var target = Local("B", new() { ["config.cfg"] = "same" });

        var plan = ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Gameplay]);
        plan.Files.Single().Action.Should().Be(ConfigFileAction.Unchanged);
        var result = Copier().Execute(plan);

        result.Backup.Should().BeNull();
        _backups.List(GameDefinition.Ets2, target).Should().BeEmpty();
    }

    [Fact]
    public void SameProfile_IsRefused()
    {
        var a = Local("A", FullConfig);
        var act = () => ProfileConfigCopier.Plan(_install, a, a with { DisplayName = "again" }, ProfileConfigs.All);
        act.Should().Throw<ConfigCopyException>();
    }

    [Fact]
    public void GameRunning_RefusesAndChangesNothing()
    {
        var source = Local("A", FullConfig);
        var target = Local("B", new() { ["config.cfg"] = "B" });
        var plan = ProfileConfigCopier.Plan(_install, source, target, ProfileConfigs.All);
        var before = Snapshot();
        _monitor.Setup(m => m.IsRunning(GameDefinition.Ets2)).Returns(true);

        var act = () => Copier().Execute(plan);

        act.Should().Throw<ConfigCopyException>().WithMessage("*running*");
        Snapshot().Should().BeEquivalentTo(before);
        _backups.List(GameDefinition.Ets2, target).Should().BeEmpty();
    }

    [Fact]
    public void GameStartingDuringTheCopy_RefusesBeforeSwappingAnyFile()
    {
        var source = Local("A", FullConfig);
        var target = Local("B", new() { ["config.cfg"] = "B" });
        var plan = ProfileConfigCopier.Plan(_install, source, target, ProfileConfigs.All);
        var before = Snapshot();
        _monitor.SetupSequence(m => m.IsRunning(GameDefinition.Ets2)).Returns(false).Returns(true);

        var act = () => Copier().Execute(plan);

        act.Should().Throw<ConfigCopyException>();
        Snapshot().Should().BeEquivalentTo(before);
        AssertNoTempFiles();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FileChangedAfterThePreview_IsRefused(bool changeSource)
    {
        var source = Local("A", new() { ["config.cfg"] = "A" });
        var target = Local("B", new() { ["config.cfg"] = "B" });
        var plan = ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Gameplay]);
        File.WriteAllText(Path.Combine(changeSource ? source.Dir : target.Dir, "config.cfg"), "changed by Steam");

        var act = () => Copier().Execute(plan);

        act.Should().Throw<ConfigCopyException>().WithMessage("*changed*");
        File.ReadAllText(Path.Combine(target.Dir, "config.cfg")).Should().Be(changeSource ? "B" : "changed by Steam");
        AssertNoTempFiles();
    }

    [Fact]
    public void LockedTargetFile_PutsBackTheFilesAlreadyChanged()
    {
        var source = Local("A", new() { ["config.cfg"] = "A cfg", ["controls.sii"] = "A controls" });
        var target = Local("B", new() { ["config.cfg"] = "B cfg", ["controls.sii"] = "B controls" });
        var plan = ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Gameplay, ConfigCategory.Controls]);

        // controls.sii is written second; holding it open without delete sharing makes its swap fail.
        using (new FileStream(Path.Combine(target.Dir, "controls.sii"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var act = () => Copier().Execute(plan);
            act.Should().Throw<ConfigCopyException>().WithMessage("*put back*");
        }

        File.ReadAllText(Path.Combine(target.Dir, "config.cfg")).Should().Be("B cfg");
        File.ReadAllText(Path.Combine(target.Dir, "controls.sii")).Should().Be("B controls");
        AssertNoTempFiles();
    }

    [Fact]
    public void Restore_PutsBackReplacedFiles_AndRemovesAddedOnes()
    {
        var source = Local("A", FullConfig);
        var target = Local("B", new() { ["config.cfg"] = "B cfg" });
        var before = Snapshot();

        var copy = Copier().Execute(ProfileConfigCopier.Plan(_install, source, target, ProfileConfigs.All));
        var backup = _backups.List(GameDefinition.Ets2, target).Single();
        backup.Description.Should().Contain("from A");

        var restore = Copier().Restore(_install, target, backup);

        restore.Restored.Should().HaveCount(copy.Written.Count);
        restore.Skipped.Should().BeEmpty();
        Snapshot().Should().BeEquivalentTo(before);
        // The restore made its own backup, which undoes the restore.
        _backups.List(GameDefinition.Ets2, target).Should().HaveCount(2);
        Copier().Restore(_install, target, restore.Backup!);
        File.ReadAllText(Path.Combine(target.Dir, "config.cfg")).Should().Be(FullConfig["config.cfg"]);
        File.ReadAllText(Path.Combine(target.Dir, "controls.sii")).Should().Be(FullConfig["controls.sii"]);
    }

    [Fact]
    public void Restore_KeepsAnAddedFileThatChangedSince()
    {
        var source = Local("A", new() { ["controls.sii"] = "A controls" });
        var target = Local("B");
        Copier().Execute(ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Controls]));
        File.WriteAllText(Path.Combine(target.Dir, "controls.sii"), "rebound in game");

        var restore = Copier().Restore(_install, target, _backups.List(GameDefinition.Ets2, target).Single());

        restore.Skipped.Should().Equal("controls.sii");
        File.ReadAllText(Path.Combine(target.Dir, "controls.sii")).Should().Be("rebound in game");
    }

    [Fact]
    public void Restore_RefusesABackupOfAnotherProfile()
    {
        var source = Local("A", new() { ["config.cfg"] = "A" });
        var target = Local("B", new() { ["config.cfg"] = "B" });
        Copier().Execute(ProfileConfigCopier.Plan(_install, source, target, [ConfigCategory.Gameplay]));
        var backup = _backups.List(GameDefinition.Ets2, target).Single();

        var act = () => Copier().Restore(_install, source, backup);
        act.Should().Throw<ConfigCopyException>();
        File.ReadAllText(Path.Combine(source.Dir, "config.cfg")).Should().Be("A");
    }

    [Theory]
    [InlineData("config.cfg", ConfigCategory.Gameplay)]
    [InlineData("CONTROLS.SII", ConfigCategory.Controls)]
    [InlineData("ffb_lut.sii", ConfigCategory.Controls)]
    [InlineData("config_local.cfg", ConfigCategory.LocalSettings)]
    [InlineData("gearbox_layout_volvo_12_2.sii", ConfigCategory.GearboxLayouts)]
    [InlineData("profile.sii", null)]
    [InlineData("profile.bak.sii", null)]
    [InlineData("gearbox_layout_.sii", null)]
    public void CategoryOf(string fileName, ConfigCategory? expected)
    {
        ProfileConfigs.CategoryOf(fileName).Should().Be(expected);
    }

    [Fact]
    public void Backups_ArePrunedToTheNewest()
    {
        var backups = new ConfigBackups(_tmp.Dir("pruned"), keepLatest: 3);
        var target = Local("B");
        var start = new DateTime(2026, 1, 1, 12, 0, 0);
        for (int i = 0; i < 5; i++)
            backups.Create(GameDefinition.Ets2, target, $"#{i}", [new(ConfigRoot.Profile, "config.cfg", [1], "x")], start.AddMinutes(i));

        backups.List(GameDefinition.Ets2, target).Select(b => b.Description).Should().Equal("#4", "#3", "#2");
    }
}
