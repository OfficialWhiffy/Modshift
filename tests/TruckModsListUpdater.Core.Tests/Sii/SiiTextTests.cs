using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Tests.Sii;

public class SiiTextTests
{
    [Fact]
    public void Parse_Manifest()
    {
        const string text = """
            SiiNunit
            {
            mod_package : .package_name {
                package_version: "2.84"
                display_name: "ProMods \"Definition\" Package"
                author: "ProMods Team"
                category[]: "map"
                category[]: "truck" # second category
                icon: "mod_icon.jpg"
                compatible_versions[]: "1.61.*"
                dlc_dependencies[]: "dlc_east"
                mp_mod_optional: true
            }
            }
            """;
        var unit = SiiText.Parse(text).Single();
        unit.Type.Should().Be("mod_package");
        unit.Id.Should().Be(".package_name");
        unit.Get("display_name").Should().Be("ProMods \"Definition\" Package");
        unit.GetArray("category").Should().Equal("map", "truck");
        unit.GetArray("compatible_versions").Should().Equal("1.61.*");
        unit.GetBool("mp_mod_optional").Should().BeTrue();
    }

    [Fact]
    public void Parse_UnitsOnOneLine_AndComments()
    {
        const string text = """
            SiiNunit { /* block
            comment */
            package_version_info : .v1 { package_name: "159_content" compatible_versions[]: "1.59.*" }
            // line comment
            package_version_info : .latest { package_name: "latest" }
            }
            """;
        var units = SiiText.Parse(text);
        units.Select(u => u.Get("package_name")).Should().Equal("159_content", "latest");
        units[0].GetArray("compatible_versions").Should().Equal("1.59.*");
    }

    [Fact]
    public void Parse_IndexedArraysAndTuples()
    {
        const string text = "SiiNunit\n{\nx : .y {\n pos: (1, 2, 3)\n list: 2\n list[0]: \"a\"\n list[1]: \"b\"\n}\n}";
        var unit = SiiText.Parse(text).Single();
        unit.Get("pos").Should().Be("(1, 2, 3)");
        unit.GetArray("list").Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("Plain", "\"Plain\"")]
    [InlineData("Quote \" and \\", "\"Quote \\\" and \\\\\"")]
    [InlineData("Müller", "\"M\\xc3\\xbcller\"")]
    public void Quote_EscapesLikeTheGame(string value, string expected)
    {
        SiiString.Quote(value).Should().Be(expected);
        SiiString.ReadQuoted(expected, 0, out var back);
        back.Should().Be(value);
    }
}
