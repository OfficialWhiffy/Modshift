using TruckModsListUpdater.App.ViewModels;
using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;

namespace TruckModsListUpdater.App.Tests;

public class ModItemDescriptionTests
{
    static readonly GameInstallation Install = new(GameDefinition.Ets2, @"C:\docs", null, [], [], "1.61.1.1", new HashSet<string>());

    static ModItemViewModel Item(string? description) => new(
        new ModPackage
        {
            PackageId = "x", Source = ModSource.Local, Kind = PackageKind.Zip, Path = @"C:\docs\mod\x.scs",
            Manifest = new ModManifest { DisplayName = "X" }, Description = description,
        },
        null, Install, null);

    static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => $"- line {i}"));

    [Fact]
    public void ShortDescription_IsShownInFullWithoutToggle()
    {
        var item = Item("[orange]Title\n" + Lines(5));

        item.HasDescription.Should().BeTrue();
        item.CanToggleDescription.Should().BeFalse();
        item.VisibleDescriptionLines.Should().HaveCount(6);
    }

    [Fact]
    public void NoDescription_HasNothingToShow()
    {
        var item = Item(null);

        item.HasDescription.Should().BeFalse();
        item.VisibleDescriptionLines.Should().BeEmpty();
    }

    [Fact]
    public void LongDescription_IsFoldedUntilExpanded()
    {
        var item = Item(Lines(40));

        item.CanToggleDescription.Should().BeTrue();
        item.VisibleDescriptionLines.Should().HaveCount(ModItemViewModel.CollapsedDescriptionLines);
        item.DescriptionToggleLabel.Should().Be("Show more (28 more lines)");

        item.ToggleDescriptionCommand.Execute(null);

        item.VisibleDescriptionLines.Should().HaveCount(40);
        item.DescriptionToggleLabel.Should().Be("Show less");
    }

    [Fact]
    public void ToggleDescription_RaisesChangeNotifications()
    {
        var item = Item(Lines(40));
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.ToggleDescriptionCommand.Execute(null);

        changed.Should().Contain([nameof(ModItemViewModel.VisibleDescriptionLines), nameof(ModItemViewModel.DescriptionToggleLabel)]);
    }

    [Fact]
    public void ExactlyAtTheLimit_IsNotFolded() =>
        Item(Lines(ModItemViewModel.CollapseDescriptionAbove)).CanToggleDescription.Should().BeFalse();
}
