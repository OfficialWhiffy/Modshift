using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.App.Views;
using TruckModsListUpdater.Core.Mods;

namespace TruckModsListUpdater.App.Tests;

public class DescriptionTextTests
{
    [Fact]
    public void Spans_BecomeStyledInlines() => Sta.Run(() =>
    {
        var block = new TextBlock();
        var spans = ModDescription.Parse("[b]Bold[/b] and [COLOR=#ff0000]red[/COLOR] see https://promods.net")[0].Spans;

        DescriptionText.SetSpans(block, spans);

        block.Inlines.Should().HaveCount(spans.Count);
        block.Inlines.OfType<Run>().Single(r => r.Text == "Bold").FontWeight.Should().Be(System.Windows.FontWeights.SemiBold);
        ((SolidColorBrush)block.Inlines.OfType<Run>().Single(r => r.Text == "red").Foreground).Color
            .Should().Be(Color.FromRgb(0xFF, 0, 0));
        var link = block.Inlines.OfType<Hyperlink>().Single();
        link.NavigateUri.Should().Be(new Uri("https://promods.net"));
        return Task.CompletedTask;
    });

    [Fact]
    public void BlankLine_GetsASmallSpacer() => Sta.Run(() =>
    {
        var block = new TextBlock();

        DescriptionText.SetSpans(block, []);

        block.Inlines.Should().ContainSingle().Which.Should().BeOfType<Run>().Which.FontSize.Should().Be(6);
        return Task.CompletedTask;
    });

    // Descriptions are untrusted text, so anything but http(s) must be refused (a valid address would launch the browser).
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void ExternalLinks_RejectsNonWebAddresses(string? url) => ExternalLinks.TryOpen(url).Should().BeFalse();
}
