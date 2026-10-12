using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Tests.Sii;

public class ActiveModsBlockTests
{
    static string Profile(string modLines, string nl = "\n") => string.Join(nl,
        "SiiNunit",
        "{",
        "user_profile : _nameless.2a3b.4c5d {",
        " face: 0",
        " brand: \"scania\"",
        modLines,
        " customization: 7",
        " profile_name: \"Bozo (mp)\"",
        "}",
        "",
        "}",
        "");

    const string ThreeMods =
        " active_mods: 3\n" +
        " active_mods[0]: \"promods-eu-assets-v284|ProMods Assets Package\"\n" +
        " active_mods[1]: \"mod_workshop_package.00000000B70CC741|Asphalt_improved\"\n" +
        " active_mods[2]: \"53RXF1|53RXF1\"";

    [Fact]
    public void Parse_ReadsEntriesInFileOrder()
    {
        var mods = ActiveModsBlock.Parse(Profile(ThreeMods));
        mods.Select(m => m.PackageId).Should().Equal("promods-eu-assets-v284", "mod_workshop_package.00000000B70CC741", "53RXF1");
        mods[1].DisplayName.Should().Be("Asphalt_improved");
        mods[1].IsWorkshop.Should().BeTrue();
        mods[1].WorkshopId.Should().Be(3071067969UL);
    }

    [Fact]
    public void Parse_ToleratesDuplicateIndicesAndWrongCount()
    {
        // As found in hand-edited copies: count says 2, index 1 appears twice.
        const string broken =
            " active_mods: 2\n" +
            " active_mods[0]: \"a|A\"\n" +
            "active_mods[1]: \"b|B\"\n" +
            " active_mods[1]: \"c|C\"";
        ActiveModsBlock.Parse(Profile(broken)).Select(m => m.PackageId).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Parse_EmptyList()
    {
        ActiveModsBlock.Parse(Profile(" active_mods: 0")).Should().BeEmpty();
    }

    [Fact]
    public void Replace_WritesCleanBlock_AndKeepsEverythingElse()
    {
        var text = Profile(ThreeMods);
        var mods = ActiveModsBlock.Parse(text);
        var reordered = new[] { mods[2], mods[0], ActiveModEntry.Create("new_mod", "New \"Mod\"") };

        var result = ActiveModsBlock.Replace(text, reordered);

        result.Should().Be(Profile(
            " active_mods: 3\n" +
            " active_mods[0]: \"53RXF1|53RXF1\"\n" +
            " active_mods[1]: \"promods-eu-assets-v284|ProMods Assets Package\"\n" +
            " active_mods[2]: \"new_mod|New \\\"Mod\\\"\""));
        ActiveModsBlock.Parse(result).Select(m => m.DisplayName).Should().Equal("53RXF1", "ProMods Assets Package", "New \"Mod\"");
    }

    [Fact]
    public void Replace_FixesMalformedArray()
    {
        const string broken = " active_mods: 5\n active_mods[3]: \"a|A\"\n active_mods[3]: \"b|B\"";
        var text = Profile(broken);
        var result = ActiveModsBlock.Replace(text, ActiveModsBlock.Parse(text));
        result.Should().Be(Profile(" active_mods: 2\n active_mods[0]: \"a|A\"\n active_mods[1]: \"b|B\""));
    }

    [Fact]
    public void Replace_ToEmpty_LeavesCountLine()
    {
        var result = ActiveModsBlock.Replace(Profile(ThreeMods), []);
        result.Should().Be(Profile(" active_mods: 0"));
    }

    [Fact]
    public void Replace_PreservesCrLf()
    {
        var crlfMods = ThreeMods.Replace("\n", "\r\n");
        var text = Profile(crlfMods, "\r\n");
        var result = ActiveModsBlock.Replace(text, ActiveModsBlock.Parse(text));
        result.Should().Be(text);
    }

    [Fact]
    public void Replace_UntouchedEntries_KeepTheirExactBytes()
    {
        // The game writes non-ASCII as \x escapes; re-quoting must not change them.
        var text = Profile(" active_mods: 1\n active_mods[0]: \"m|M\\xc3\\xbcller\"");
        ActiveModsBlock.Replace(text, ActiveModsBlock.Parse(text)).Should().Be(text);
        ActiveModsBlock.Parse(text)[0].DisplayName.Should().Be("Müller");
    }

    [Fact]
    public void Replace_WithoutUserProfile_Throws()
    {
        var act = () => ActiveModsBlock.Replace("SiiNunit\n{\n}\n", []);
        act.Should().Throw<SiiFormatException>();
    }

    [Fact]
    public void WorkshopPackageId_Is16UppercaseHexDigits()
    {
        ActiveModEntry.WorkshopPackageId(3071067969).Should().Be("mod_workshop_package.00000000B70CC741");
    }

    [Fact]
    public void LoadOrderMapper_ReversesBothWays()
    {
        var file = new[] { "lowest", "middle", "highest" };
        var display = LoadOrderMapper.ToDisplayOrder(file);
        display.Should().Equal("highest", "middle", "lowest");
        LoadOrderMapper.ToFileOrder(display).Should().Equal(file);
    }
}
