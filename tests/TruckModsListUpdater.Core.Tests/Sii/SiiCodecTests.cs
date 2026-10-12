using System.Text;
using TruckModsListUpdater.Core.Sii;

namespace TruckModsListUpdater.Core.Tests.Sii;

public class SiiCodecTests
{
    const string Text = "SiiNunit\n{\nuser_profile : _nameless.1234 {\n active_mods: 0\n profile_name: \"Test\"\n}\n}\n";

    [Fact]
    public void Decode_PlainText()
    {
        var doc = SiiCodec.Decode(Encoding.UTF8.GetBytes(Text));
        doc.Container.Should().Be(SiiContainer.Plain);
        doc.Content.Should().Be(SiiContent.Text);
        doc.Text.Should().Be(Text);
        doc.IsWritable.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Decode_StripsAnyNumberOfBoms(int boms)
    {
        var bytes = Enumerable.Repeat(new byte[] { 0xEF, 0xBB, 0xBF }, boms).SelectMany(b => b)
            .Concat(Encoding.UTF8.GetBytes(Text)).ToArray();
        SiiCodec.Decode(bytes).Text.Should().Be(Text);
    }

    [Fact]
    public void Decode_ScsC_RoundTrips()
    {
        var encrypted = SiiCodec.EncodeScsC(Encoding.UTF8.GetBytes(Text));
        encrypted.AsSpan(0, 4).ToArray().Should().Equal("ScsC"u8.ToArray());

        var doc = SiiCodec.Decode(encrypted);
        doc.Container.Should().Be(SiiContainer.ScsC);
        doc.Text.Should().Be(Text);
    }

    [Fact]
    public void Decode_3nK_RoundTrips()
    {
        var encoded = SiiCodec.Encode3nK(Encoding.UTF8.GetBytes(Text), seed: 200);
        var doc = SiiCodec.Decode(encoded);
        doc.Container.Should().Be(SiiContainer.ThreeNK);
        doc.Text.Should().Be(Text);
    }

    [Fact]
    public void ThreeNKKeyTable_MatchesPublishedValues()
    {
        // First and last rows of the table in SII_Decrypt's SII_3nK_Transcoder.pas.
        Enumerable.Range(0, 4).Select(i => SiiCodec.ThreeNKKey((byte)i)).Should().Equal(0xF8, 0xD1, 0xAA, 0x83);
        Enumerable.Range(252, 4).Select(i => SiiCodec.ThreeNKKey((byte)i)).Should().Equal(0x64, 0x4D, 0x36, 0x1F);
    }

    [Fact]
    public void Decode_CorruptScsC_Throws()
    {
        var encrypted = SiiCodec.EncodeScsC(Encoding.UTF8.GetBytes(Text));
        encrypted[^1] ^= 0xFF;
        var act = () => SiiCodec.Decode(encrypted);
        act.Should().Throw<SiiFormatException>();
    }

    [Fact]
    public void Decode_Garbage_Throws()
    {
        var act = () => SiiCodec.Decode(Encoding.UTF8.GetBytes("hello world"));
        act.Should().Throw<SiiFormatException>();
    }

    [Fact]
    public void Decode_BinarySii_IsReadOnly_AndReadable()
    {
        var doc = SiiCodec.Decode(BsiiFixture.UserProfile(["mod_a|Mod A", "mod_workshop_package.00000000029A002E|Mirror"]));
        doc.Content.Should().Be(SiiContent.Binary);
        doc.IsWritable.Should().BeFalse();

        var unit = doc.ReadUnits().Single();
        unit.Type.Should().Be("user_profile");
        unit.GetArray("active_mods").Should().Equal("mod_a|Mod A", "mod_workshop_package.00000000029A002E|Mirror");
        unit.Get("profile_name").Should().Be("Binary");
    }
}

/// <summary>Builds a tiny BSII v2 file with one user_profile unit.</summary>
static class BsiiFixture
{
    public static byte[] UserProfile(string[] activeMods)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("BSII"u8);
        w.Write(2u);
        // Structure block: id 1 "user_profile" { string_array active_mods; string profile_name }
        w.Write(0u); w.Write((byte)1); w.Write(1u); Str(w, "user_profile");
        w.Write(0x02u); Str(w, "active_mods");
        w.Write(0x01u); Str(w, "profile_name");
        w.Write(0u);
        // Data block: id (one encoded part "abc"), then values.
        w.Write(1u);
        w.Write((byte)1); w.Write(EncodeToken("abc"));
        w.Write((uint)activeMods.Length);
        foreach (var m in activeMods) Str(w, m);
        Str(w, "Binary");
        // End marker.
        w.Write(0u); w.Write((byte)0);
        return ms.ToArray();
    }

    static void Str(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write((uint)b.Length);
        w.Write(b);
    }

    static ulong EncodeToken(string s)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyz_";
        ulong v = 0;
        for (int i = s.Length - 1; i >= 0; i--) v = v * 38 + (ulong)(chars.IndexOf(s[i]) + 1);
        return v;
    }
}
