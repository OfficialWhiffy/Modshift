using System.Text;
using GongSolutions.Wpf.DragDrop;
using Moq;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.App.ViewModels;
using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.App.Tests;

public class MainViewModelTests
{
    static readonly GameInstallation Install = new(GameDefinition.Ets2, @"C:\docs", null, [], [], "1.61.1.1", new HashSet<string>());
    static readonly ProfileRef LocalProfile = new(ProfileKind.Local, @"C:\docs\profiles\4C", "Local", DateTime.UtcNow);
    static readonly ProfileRef CloudProfile = new(ProfileKind.SteamCloud, @"C:\remote\profiles\43", "Cloud", DateTime.UtcNow);

    readonly Mock<IGameServices> _services = new();
    readonly Mock<IUserDialogs> _dialogs = new();
    readonly AppSettings _settings = new();
    readonly Dictionary<ProfileRef, ProfileSession> _sessions = new();
    List<ActiveModEntry>? _written;

    static ModPackage Package(string id, string name) => new()
    {
        PackageId = id, Source = ModSource.Local, Kind = PackageKind.Zip, Path = $@"C:\docs\mod\{id}.scs",
        Manifest = new ModManifest { DisplayName = name },
    };

    static ProfileSession Session(ProfileRef profile, params string[] fileOrderIds)
    {
        var entries = fileOrderIds.Select(id => ActiveModEntry.Create(id, id.ToUpperInvariant())).ToList();
        var text = "SiiNunit\n{\nuser_profile : _nameless.1 {\n active_mods: 0\n}\n}\n";
        text = ActiveModsBlock.Replace(text, entries);
        return new ProfileSession
        {
            Profile = profile,
            Document = new SiiDocument(SiiContainer.Plain, SiiContent.Text, Encoding.UTF8.GetBytes(text), text),
            Fingerprint = new FileFingerprint(text.Length, DateTime.UtcNow, "x"),
            ActiveMods = entries,
        };
    }

    MainViewModel CreateVm(params ProfileRef[] profiles)
    {
        profiles = profiles.Length == 0 ? [LocalProfile] : profiles;
        _sessions.TryAdd(LocalProfile, Session(LocalProfile, "low", "mid", "high"));
        _sessions.TryAdd(CloudProfile, Session(CloudProfile, "low"));

        var catalog = new ModCatalog([Package("low", "Low"), Package("mid", "Mid"), Package("high", "High"), Package("extra", "Extra")]);
        _services.Setup(s => s.LocateGames()).Returns([Install]);
        _services.Setup(s => s.ScanAsync(Install, It.IsAny<IProgress<string>>())).ReturnsAsync(catalog);
        _services.Setup(s => s.Profiles(Install)).Returns(profiles);
        _services.Setup(s => s.LoadProfile(It.IsAny<ProfileRef>())).Returns((ProfileRef p) => _sessions[p]);
        _services.Setup(s => s.WriteActiveMods(GameDefinition.Ets2, It.IsAny<ProfileSession>(), It.IsAny<IReadOnlyList<ActiveModEntry>>()))
            .Returns((GameDefinition _, ProfileSession s, IReadOnlyList<ActiveModEntry> mods) =>
            {
                _written = mods.ToList();
                var updated = Session(s.Profile, mods.Select(m => m.PackageId).ToArray());
                _sessions[s.Profile] = updated;
                return new ProfileWriteResult(new ProfileBackup(@"C:\backup.zip", DateTime.Now), updated);
            });
        return new MainViewModel(_services.Object, _dialogs.Object, _settings, () => { });
    }

    static List<string> Ids(IEnumerable<ModItemViewModel> mods) => mods.Select(m => m.PackageId).ToList();

    [Fact]
    public void Initialize_ShowsActiveModsHighestPriorityFirst() => Sta.Run(Initialize_ShowsActiveModsHighestPriorityFirstAsync);

    async Task Initialize_ShowsActiveModsHighestPriorityFirstAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.SelectedProfile!.Profile.Should().Be(LocalProfile);
        Ids(vm.ActiveMods).Should().Equal("high", "mid", "low");
        Ids(vm.AvailableMods).Should().Equal("extra");
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void EnableAndApply_WritesFileOrder_AndClearsChanges() => Sta.Run(EnableAndApply_WritesFileOrder_AndClearsChangesAsync);

    async Task EnableAndApply_WritesFileOrder_AndClearsChangesAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.EnableCommand.Execute(vm.AvailableMods.Single());

        Ids(vm.ActiveMods).Should().Equal("extra", "high", "mid", "low");
        vm.HasChanges.Should().BeTrue();
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();

        vm.ApplyCommand.Execute(null);

        _written!.Select(m => m.PackageId).Should().Equal("low", "mid", "high", "extra");
        _written![3].RawValue.Should().Be("\"extra|Extra\"");
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void MoveAndRevert() => Sta.Run(MoveAndRevertAsync);

    async Task MoveAndRevertAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.SelectedActiveMod = vm.ActiveMods.Single(m => m.PackageId == "low");
        vm.MoveToTopCommand.Execute(null);
        Ids(vm.ActiveMods).Should().Equal("low", "high", "mid");
        vm.MoveDownCommand.Execute(null);
        Ids(vm.ActiveMods).Should().Equal("high", "low", "mid");
        vm.HasChanges.Should().BeTrue();

        vm.RevertCommand.Execute(null);
        Ids(vm.ActiveMods).Should().Equal("high", "mid", "low");
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void MovingBackToOriginalOrder_IsNotAChange() => Sta.Run(MovingBackToOriginalOrder_IsNotAChangeAsync);

    async Task MovingBackToOriginalOrder_IsNotAChangeAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.SelectedActiveMod = vm.ActiveMods[0];
        vm.MoveDownCommand.Execute(null);
        vm.MoveUpCommand.Execute(null);
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Disable_MovesModToAvailable() => Sta.Run(Disable_MovesModToAvailableAsync);

    async Task Disable_MovesModToAvailableAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.DisableCommand.Execute(vm.ActiveMods.Single(m => m.PackageId == "mid"));

        Ids(vm.ActiveMods).Should().Equal("high", "low");
        vm.AvailableMods.Select(m => m.PackageId).Should().BeEquivalentTo("extra", "mid");
    }

    [Fact]
    public void MissingPackage_IsShownWithError_AndDisappearsWhenDisabled() => Sta.Run(MissingPackage_IsShownWithError_AndDisappearsWhenDisabledAsync);

    async Task MissingPackage_IsShownWithError_AndDisappearsWhenDisabledAsync()
    {
        _sessions[LocalProfile] = Session(LocalProfile, "low", "gone");
        var vm = CreateVm();
        await vm.InitializeAsync();

        var missing = vm.ActiveMods.Single(m => m.PackageId == "gone");
        missing.IsMissing.Should().BeTrue();
        missing.Issues.Should().ContainSingle(i => i.Kind == ModIssueKind.MissingPackage);

        vm.DisableCommand.Execute(missing);
        vm.AvailableMods.Should().NotContain(m => m.PackageId == "gone");
    }

    [Fact]
    public void ApplyIsDisabled_WhileGameRuns() => Sta.Run(ApplyIsDisabled_WhileGameRunsAsync);

    async Task ApplyIsDisabled_WhileGameRunsAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.EnableCommand.Execute(vm.AvailableMods.Single());

        vm.OnGameRunningChanged(GameDefinition.Ets2, true);

        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        vm.Banner.Should().Contain("running");
    }

    [Fact]
    public void GameExit_ReloadsProfile_WhenNoUnsavedChanges() => Sta.Run(GameExit_ReloadsProfile_WhenNoUnsavedChangesAsync);

    async Task GameExit_ReloadsProfile_WhenNoUnsavedChangesAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.OnGameRunningChanged(GameDefinition.Ets2, true);

        _sessions[LocalProfile] = Session(LocalProfile, "extra");
        vm.OnGameRunningChanged(GameDefinition.Ets2, false);

        Ids(vm.ActiveMods).Should().Equal("extra");
    }

    [Fact]
    public void CloudProfile_AsksOnce_AndDecliningDoesNotWrite() => Sta.Run(CloudProfile_AsksOnce_AndDecliningDoesNotWriteAsync);

    async Task CloudProfile_AsksOnce_AndDecliningDoesNotWriteAsync()
    {
        _settings.LastProfileDir = CloudProfile.Dir;
        var vm = CreateVm(LocalProfile, CloudProfile);
        await vm.InitializeAsync();
        vm.SelectedProfile!.Profile.Should().Be(CloudProfile);
        vm.EnableCommand.Execute(vm.AvailableMods.First());

        _dialogs.Setup(d => d.Confirm("Steam Cloud profile", It.IsAny<string>())).Returns(false);
        vm.ApplyCommand.Execute(null);
        _written.Should().BeNull();

        _dialogs.Setup(d => d.Confirm("Steam Cloud profile", It.IsAny<string>())).Returns(true);
        vm.ApplyCommand.Execute(null);
        _written.Should().NotBeNull();
        _settings.CloudWarningAcknowledged.Should().BeTrue();
    }

    [Fact]
    public void WriteFailure_IsReported_AndChangesAreKept() => Sta.Run(WriteFailure_IsReported_AndChangesAreKeptAsync);

    async Task WriteFailure_IsReported_AndChangesAreKeptAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        _services.Setup(s => s.WriteActiveMods(It.IsAny<GameDefinition>(), It.IsAny<ProfileSession>(), It.IsAny<IReadOnlyList<ActiveModEntry>>()))
            .Throws(new GameRunningException(GameDefinition.Ets2));
        vm.EnableCommand.Execute(vm.AvailableMods.Single());

        vm.ApplyCommand.Execute(null);

        _dialogs.Verify(d => d.Error("Could not save", It.Is<string>(m => m.Contains("running"))));
        vm.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Search_FiltersAvailableList() => Sta.Run(Search_FiltersAvailableListAsync);

    async Task Search_FiltersAvailableListAsync()
    {
        _sessions[LocalProfile] = Session(LocalProfile);
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.SearchText = "mi";
        vm.AvailableView.Cast<ModItemViewModel>().Select(m => m.PackageId).Should().Equal("mid");
    }

    [Fact]
    public void Install_NewModGoesOnTop_ReplacedVersionKeepsItsPlace() => Sta.Run(Install_NewModGoesOnTop_ReplacedVersionKeepsItsPlaceAsync);

    async Task Install_NewModGoesOnTop_ReplacedVersionKeepsItsPlaceAsync()
    {
        var vm = CreateVm(LocalProfile, CloudProfile);
        await vm.InitializeAsync();

        var fresh = new InstallCandidate { Package = Package("fresh", "Fresh"), SourceDescription = "fresh.scs", TargetName = "fresh.scs", Action = InstallAction.Install };
        var midV2 = new InstallCandidate
        {
            Package = Package("mid-v2", "Mid 2"), SourceDescription = "mid-v2.scs", TargetName = "mid-v2.scs",
            Existing = Package("mid", "Mid"), Conflict = InstallConflict.NewerVersionOf, Action = InstallAction.Replace,
        };
        var plan = new InstallPlan(@"C:\staging", [fresh, midV2], []);
        _services.Setup(s => s.AnalyzeInstallAsync(Install, It.IsAny<IReadOnlyList<string>>(), It.IsAny<ModCatalog>())).ReturnsAsync(plan);
        _services.Setup(s => s.ExecuteInstallAsync(Install, plan, It.IsAny<IProgress<string>>()))
            .ReturnsAsync([new InstalledMod(fresh, @"C:\docs\mod\fresh.scs", null), new InstalledMod(midV2, @"C:\docs\mod\mid-v2.scs", null)]);
        _services.Setup(s => s.UpdateReferences(GameDefinition.Ets2, It.IsAny<IEnumerable<ProfileRef>>(), It.IsAny<IReadOnlyCollection<ModReplacement>>()))
            .Returns([CloudProfile]);
        _dialogs.Setup(d => d.ShowInstall(It.IsAny<InstallViewModel>())).Returns(true);
        var afterInstall = new ModCatalog([Package("low", "Low"), Package("mid-v2", "Mid 2"), Package("high", "High"), Package("extra", "Extra"), Package("fresh", "Fresh")]);
        _services.Setup(s => s.ScanAsync(Install, It.IsAny<IProgress<string>>())).ReturnsAsync(afterInstall);

        await vm.InstallFilesAsync([@"C:\downloads\fresh.scs", @"C:\downloads\mid-v2.scs"]);

        Ids(vm.ActiveMods).Should().Equal("fresh", "high", "mid-v2", "low");
        vm.HasChanges.Should().BeTrue();
        vm.ActiveMods.Should().OnlyContain(m => !m.IsMissing);
        _services.Verify(s => s.UpdateReferences(GameDefinition.Ets2,
            It.Is<IEnumerable<ProfileRef>>(p => p.SequenceEqual(new[] { CloudProfile })),
            It.Is<IReadOnlyCollection<ModReplacement>>(r => r.Single().OldPackageId == "mid" && r.Single().NewPackageId == "mid-v2")));
    }

    [Fact]
    public void Install_Cancelled_CleansStaging() => Sta.Run(Install_Cancelled_CleansStagingAsync);

    async Task Install_Cancelled_CleansStagingAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        var plan = new InstallPlan(@"C:\staging",
            [new InstallCandidate { Package = Package("x", "X"), SourceDescription = "x.scs", TargetName = "x.scs" }], []);
        _services.Setup(s => s.AnalyzeInstallAsync(Install, It.IsAny<IReadOnlyList<string>>(), It.IsAny<ModCatalog>())).ReturnsAsync(plan);
        _dialogs.Setup(d => d.ShowInstall(It.IsAny<InstallViewModel>())).Returns(false);

        await vm.InstallFilesAsync([@"C:\x.scs"]);

        _services.Verify(s => s.CancelInstall(Install, plan));
        _services.Verify(s => s.ExecuteInstallAsync(It.IsAny<GameInstallation>(), It.IsAny<InstallPlan>(), It.IsAny<IProgress<string>>()), Times.Never);
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void ApplyPreset_ReplacesList_AsUnsavedChange() => Sta.Run(ApplyPreset_ReplacesList_AsUnsavedChangeAsync);

    async Task ApplyPreset_ReplacesList_AsUnsavedChangeAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.ApplyPreset(new Core.Presets.ModPreset
        {
            Name = "Mine", Game = "ets2",
            Mods = [new("extra", "Extra"), new("low", "Low"), new("not-installed", "Nope")],
        });

        Ids(vm.ActiveMods).Should().Equal("extra", "low");
        vm.HasChanges.Should().BeTrue();
        _dialogs.Verify(d => d.Info("Preset \"Mine\"", It.Is<string>(s => s.Contains("not-installed"))));
        _written.Should().BeNull();
    }

    [Fact]
    public void FixMissingMods_ReplacesInPlace() => Sta.Run(FixMissingMods_ReplacesInPlaceAsync);

    async Task FixMissingMods_ReplacesInPlaceAsync()
    {
        _sessions[LocalProfile] = new ProfileSession
        {
            Profile = LocalProfile,
            Document = Session(LocalProfile).Document,
            Fingerprint = new FileFingerprint(0, DateTime.UtcNow, "x"),
            ActiveMods = [ActiveModEntry.Create("low", "Low"), ActiveModEntry.Create("extra-v1", "Extra"), ActiveModEntry.Create("high", "High")],
        };
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.FixMissingModsCommand.CanExecute(null).Should().BeTrue();
        _dialogs.Setup(d => d.Confirm("Fix missing mods", It.IsAny<string>())).Returns(true);

        vm.FixMissingModsCommand.Execute(null);

        Ids(vm.ActiveMods).Should().Equal("high", "extra", "low");
        vm.ActiveMods.Should().OnlyContain(m => !m.IsMissing);
        vm.FixMissingModsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Drop_ReordersWithinActiveList() => Sta.Run(Drop_ReordersWithinActiveListAsync);

    async Task Drop_ReordersWithinActiveListAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        var low = vm.ActiveMods.Single(m => m.PackageId == "low");

        var drop = new Mock<IDropInfo>();
        drop.SetupGet(d => d.Data).Returns(low);
        drop.SetupGet(d => d.TargetCollection).Returns(vm.ActiveMods);
        drop.SetupGet(d => d.InsertIndex).Returns(0);
        vm.Drop(drop.Object);

        Ids(vm.ActiveMods).Should().Equal("low", "high", "mid");
    }

    [Fact]
    public void Drop_FromAvailable_EnablesAtPosition() => Sta.Run(Drop_FromAvailable_EnablesAtPositionAsync);

    async Task Drop_FromAvailable_EnablesAtPositionAsync()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        var drop = new Mock<IDropInfo>();
        drop.SetupGet(d => d.Data).Returns(vm.AvailableMods.Single());
        drop.SetupGet(d => d.TargetCollection).Returns(vm.ActiveMods);
        drop.SetupGet(d => d.InsertIndex).Returns(2);
        vm.Drop(drop.Object);

        Ids(vm.ActiveMods).Should().Equal("high", "mid", "extra", "low");
    }
}
