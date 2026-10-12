using TruckModsListUpdater.Core.Mods;

namespace TruckModsListUpdater.Core.Tests.Mods;

public class ModDescriptionTests
{
    static string Text(DescriptionLine line) => string.Concat(line.Spans.Select(s => s.Text));

    [Fact]
    public void Colour_StaysOnAcrossLinesUntilNormal()
    {
        var lines = ModDescription.Parse("[orange]Heading\nstill orange\n[normal]plain");

        lines.Select(Text).Should().Equal("Heading", "still orange", "plain");
        lines[0].Spans.Single().Color.Should().Be(DescriptionColor.Orange);
        lines[1].Spans.Single().Color.Should().Be(DescriptionColor.Orange);
        lines[2].Spans.Single().Color.Should().Be(DescriptionColor.Normal);
    }

    [Fact]
    public void Colour_ChangesMidLineAndIsCaseInsensitive()
    {
        var line = ModDescription.Parse("[RED]Important:[Normal] read this")[0];

        line.Spans.Select(s => (s.Text, s.Color)).Should().Equal(
            ("Important:", DescriptionColor.Red), (" read this", DescriptionColor.Normal));
    }

    [Fact]
    public void UnknownBracketText_StaysLiteral()
    {
        var line = ModDescription.Parse("[red]Name: [white]ETS2 [Trailer] Wind Turbine Blade")[0];

        Text(line).Should().Be("Name: ETS2 [Trailer] Wind Turbine Blade");
        line.Spans.Last().Color.Should().Be(DescriptionColor.White);
    }

    [Fact]
    public void BoldItalicUnderlineAndHexColour_AreParsed()
    {
        var line = ModDescription.Parse("[b]bold[/b] [i]it[/i] [u]un[/u] [COLOR=#ff0000]red[/COLOR] end")[0];

        Text(line).Should().Be("bold it un red end");
        line.Spans.Single(s => s.Text == "bold").Bold.Should().BeTrue();
        line.Spans.Single(s => s.Text == "it").Italic.Should().BeTrue();
        line.Spans.Single(s => s.Text == "un").Underline.Should().BeTrue();
        line.Spans.Single(s => s.Text == "red").Hex.Should().Be("#ff0000");
        line.Spans.Single(s => s.Text == " end").Hex.Should().BeNull();
        line.Spans.Single(s => s.Text == " end").Bold.Should().BeFalse();
    }

    [Fact]
    public void Links_BecomeSpansWithoutTrailingPunctuation()
    {
        var line = ModDescription.Parse("[blue]See https://promods.net/download, or http://a.b/c.")[0];

        line.Spans.Where(s => s.Link != null).Select(s => s.Link).Should().Equal("https://promods.net/download", "http://a.b/c");
        Text(line).Should().Be("See https://promods.net/download, or http://a.b/c.");
        line.Spans.Single(s => s.Link == "http://a.b/c").Color.Should().Be(DescriptionColor.Blue);
    }

    [Theory]
    [InlineData("- Albania", "•", 0)]
    [InlineData("* Feature", "•", 0)]
    [InlineData("✔ Full weather", "✔", 0)]
    [InlineData("\t- Nordic Horizons", "•", 1)]
    [InlineData("\t\t- Deep", "•", 2)]
    public void ListItems_ReadBulletAndIndent(string raw, string bullet, int indent)
    {
        var line = ModDescription.Parse(raw)[0];

        line.IsListItem.Should().BeTrue();
        line.Bullet.Should().Be(bullet);
        line.Indent.Should().Be(indent);
        Text(line).Should().NotStartWith("-").And.NotStartWith("*").And.NotStartWith("✔");
    }

    [Theory]
    [InlineData("-------")]
    [InlineData("*ACI SRL --- ITA Airways")]
    [InlineData("**Partially skinned")]
    [InlineData("1) Without repeats")]
    public void NotAList_WhenMarkerIsNotFollowedBySpace(string raw) =>
        ModDescription.Parse(raw)[0].IsListItem.Should().BeFalse();

    [Fact]
    public void ListItem_KeepsItsColour()
    {
        var line = ModDescription.Parse("- [red]Careful")[0];

        line.IsListItem.Should().BeTrue();
        line.Spans.Single().Color.Should().Be(DescriptionColor.Red);
    }

    [Fact]
    public void BlankLines_CollapseAndAreTrimmedAtTheEnds()
    {
        var lines = ModDescription.Parse("\n\n[orange]A\n\n\n\n[normal]B\n[normal]\n\n");

        lines.Select(l => l.IsBlank ? "" : Text(l)).Should().Equal("A", "", "B");
    }

    [Fact]
    public void WindowsLineEndings_AreHandled() =>
        ModDescription.Parse("one\r\ntwo\rthree").Select(Text).Should().Equal("one", "two", "three");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\t\n")]
    public void EmptyDescription_GivesNoLines(string? text) =>
        ModDescription.Parse(text).Should().BeEmpty();

    [Fact]
    public void ToPlainText_DropsMarkup() =>
        ModDescription.ToPlainText(ModDescription.Parse("[orange]Title[normal]\n- one\n- two"))
            .Should().Be($"Title{Environment.NewLine}one{Environment.NewLine}two");
}
