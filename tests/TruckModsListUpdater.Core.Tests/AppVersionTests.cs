namespace TruckModsListUpdater.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("2.1.0", 2, 1, 0, null, null)]
    [InlineData("v2.1.0", 2, 1, 0, null, null)]
    [InlineData("2.0.0-beta.1", 2, 0, 0, "beta.1", null)]
    [InlineData("2.0.0-alpha.0.10+23cd2c9b642510ab02794fd10fd08b9c2f4e564f", 2, 0, 0, "alpha.0.10", "23cd2c9b642510ab02794fd10fd08b9c2f4e564f")]
    public void Parse_ReadsAllParts(string text, int major, int minor, int patch, string? pre, string? build)
    {
        AppVersion.Parse(text).Should().Be(new AppVersion(major, minor, patch, pre, build));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2.1")]
    [InlineData("v.1.6.0")]
    [InlineData("2.1.0.4")]
    [InlineData("two.one.zero")]
    [InlineData("2.1.0-")]
    public void TryParse_RejectsNonSemVer(string text)
    {
        AppVersion.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Display_DropsBuildMetadata_DetailedKeepsShortHash()
    {
        var v = AppVersion.Parse("2.0.0-alpha.0.10+23cd2c9b642510ab02794fd10fd08b9c2f4e564f");

        v.Display.Should().Be("2.0.0-alpha.0.10");
        v.Detailed.Should().Be("2.0.0-alpha.0.10+23cd2c9");
        AppVersion.Parse("2.1.0").Detailed.Should().Be("2.1.0");
    }

    [Theory]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("2.1.0", "2.0.9")]
    [InlineData("2.0.1", "2.0.0")]
    [InlineData("2.0.0", "2.0.0-rc.1")]                 // a release outranks its pre-release
    [InlineData("2.0.0-rc.1", "2.0.0-beta.2")]
    [InlineData("2.0.0-beta.11", "2.0.0-beta.2")]       // numeric identifiers compare as numbers
    [InlineData("2.0.0-beta", "2.0.0-alpha.9")]
    [InlineData("2.0.0-beta.1", "2.0.0-beta")]          // more identifiers outrank fewer
    [InlineData("2.0.0-alpha", "2.0.0-1")]              // alphanumeric outranks numeric
    public void Ordering_FollowsSemVer(string higher, string lower)
    {
        var h = AppVersion.Parse(higher);
        var l = AppVersion.Parse(lower);

        (h > l).Should().BeTrue();
        (l < h).Should().BeTrue();
        h.CompareTo(l).Should().BePositive();
        l.CompareTo(h).Should().BeNegative();
    }

    [Fact]
    public void Build_Metadata_IsIgnoredByComparisonAndEquality()
    {
        var a = AppVersion.Parse("2.0.0+abc1234");
        var b = AppVersion.Parse("2.0.0+def5678");

        a.CompareTo(b).Should().Be(0);
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void FromAssembly_ReadsTheStampedVersion()
    {
        var v = AppVersion.FromAssembly(typeof(AppVersion).Assembly);

        // MinVer stamps every build; the minimum is configured in Directory.Build.props.
        v.Should().BeGreaterThanOrEqualTo(new AppVersion(2, 0, 0, "0"));
    }
}
