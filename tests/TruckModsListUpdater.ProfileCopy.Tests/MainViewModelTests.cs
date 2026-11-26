using Moq;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.ProfileCopy.Services;
using TruckModsListUpdater.ProfileCopy.ViewModels;

namespace TruckModsListUpdater.ProfileCopy.Tests;

public class MainViewModelTests
{
    readonly Mock<ICopyServices> _services = new();
    readonly Mock<IDialogs> _dialogs = new();
    readonly GameInstallation _install = new(GameDefinition.Ets2, @"C:\docs", null, [], [], null, new HashSet<string>());
    readonly ProfileRef _a = new(ProfileKind.Local, @"C:\docs\profiles\41", "A", DateTime.UtcNow);
    readonly ProfileRef _b = new(ProfileKind.SteamCloud, @"C:\remote\profiles\42", "B", DateTime.UtcNow.AddDays(-1));
    readonly List<IReadOnlyList<ConfigCategory>> _planned = [];
    ConfigFileAction _action = ConfigFileAction.Replace;

    MainViewModel CreateVm()
    {
        _services.Setup(s => s.LocateGames()).Returns([_install]);
        _services.Setup(s => s.Profiles(_install)).Returns([_a, _b]);
        _services.Setup(s => s.LastPlayedProfile(_install, It.IsAny<IReadOnlyList<ProfileRef>>())).Returns(_b);
        _services.Setup(s => s.Backups(GameDefinition.Ets2, It.IsAny<ProfileRef>())).Returns([]);
        _services.Setup(s => s.Plan(_install, It.IsAny<ProfileRef>(), It.IsAny<ProfileRef>(), It.IsAny<IReadOnlyList<ConfigCategory>>()))
            .Returns((GameInstallation i, ProfileRef s, ProfileRef t, IReadOnlyList<ConfigCategory> c) =>
            {
                _planned.Add(c);
                var files = c.Select(cat => new ConfigFileCopy(cat, cat + ".file", "src", "h1", ConfigRoot.Profile, "dst", "h2", _action)).ToList();
                return new ConfigCopyPlan(i, s, t, c, files, []);
            });
        _services.Setup(s => s.Copy(It.IsAny<ConfigCopyPlan>()))
            .Returns((ConfigCopyPlan p) => new ConfigCopyResult(null, p.Changes));
        return new MainViewModel(_services.Object, _dialogs.Object);
    }

    [Fact]
    public void StartsWithTheLastPlayedProfileAsSource() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();

        vm.Source!.Profile.Should().Be(_b);
        vm.Target.Should().BeNull();
        vm.CopyCommand.CanExecute(null).Should().BeFalse();
    });

    [Fact]
    public void ChoosingATarget_ShowsThePreview_AndEnablesCopy() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);

        vm.Files.Should().HaveCount(4);
        vm.CopyCommand.CanExecute(null).Should().BeTrue();
        vm.Summary.Should().Contain("4 files");
    });

    [Fact]
    public void UntickingACategory_LeavesItOutOfThePlan() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);

        vm.Categories.Single(c => c.Category == ConfigCategory.Controls).IsChecked = false;

        _planned.Last().Should().NotContain(ConfigCategory.Controls).And.HaveCount(3);
        vm.Files.Should().HaveCount(3);
    });

    [Fact]
    public void SameProfileOnBothSides_DisablesCopy() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Source;

        vm.CopyCommand.CanExecute(null).Should().BeFalse();
        vm.Summary.Should().Contain("two different profiles");
    });

    [Fact]
    public void NothingDifferent_DisablesCopy() => Sta.Run(async () =>
    {
        _action = ConfigFileAction.Unchanged;
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);

        vm.CopyCommand.CanExecute(null).Should().BeFalse();
        vm.Summary.Should().Contain("Nothing to copy");
    });

    [Fact]
    public void GameRunning_DisablesCopy() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);

        vm.OnGameRunningChanged(GameDefinition.Ets2, true);

        vm.CopyCommand.CanExecute(null).Should().BeFalse();
        vm.Banner.Should().Contain("running");
    });

    [Fact]
    public void Copy_AsksFirst_ThenCopies() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);

        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        await vm.CopyCommand.ExecuteAsync(null);
        _services.Verify(s => s.Copy(It.IsAny<ConfigCopyPlan>()), Times.Never);

        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        await vm.CopyCommand.ExecuteAsync(null);
        _services.Verify(s => s.Copy(It.Is<ConfigCopyPlan>(p => p.Source == _b && p.Target == _a)), Times.Once);
        vm.StatusText.Should().Contain("Copied 4 files");
    });

    [Fact]
    public void CopyFailure_ShowsTheMessage() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _services.Setup(s => s.Copy(It.IsAny<ConfigCopyPlan>())).Throws(new ConfigCopyException("Close the game first."));

        await vm.CopyCommand.ExecuteAsync(null);

        _dialogs.Verify(d => d.Error("Copy settings", "Close the game first."), Times.Once);
        vm.IsBusy.Should().BeFalse();
    });

    [Fact]
    public void Swap_ExchangesSourceAndTarget() => Sta.Run(async () =>
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        vm.Target = vm.Profiles.Single(p => p.Profile == _a);

        vm.SwapCommand.Execute(null);

        vm.Source!.Profile.Should().Be(_a);
        vm.Target!.Profile.Should().Be(_b);
        vm.TargetIsCloud.Should().BeTrue();
    });
}
