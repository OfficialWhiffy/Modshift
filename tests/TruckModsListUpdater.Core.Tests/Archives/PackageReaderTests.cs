using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;
using static TruckModsListUpdater.Core.Tests.Fixtures.PackageBuilder;

namespace TruckModsListUpdater.Core.Tests.Archives;

public class PackageReaderTests
{
    static Dictionary<string, byte[]> ModFiles(string name) => new()
    {
        ["manifest.sii"] = Utf8(Manifest(name)),
        ["description.txt"] = Utf8("[COLOR=#ff0000]Hello[/COLOR] trucker"),
        ["icon.jpg"] = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3],
        ["def/vehicle/truck/some/very/long/path/that/exceeds/sixty/four/bytes/for/sure/data.sii"] = Utf8("SiiNunit { }"),
    };

    [Fact]
    public void Zip_ReadsManifestIconAndDescription()
    {
        using var tmp = new TempDir();
        var path = tmp.File("mod.scs");
        Zip(path, ModFiles("Zip Mod"));

        PackageReader.DetectKind(path).Should().Be(PackageKind.Zip);
        using var reader = PackageReader.Open(path);
        var meta = ManifestReader.Read(reader);
        meta.Manifest!.DisplayName.Should().Be("Zip Mod");
        meta.Description.Should().Be("[COLOR=#ff0000]Hello[/COLOR] trucker", "colour tags are kept and rendered when shown");
        meta.Icon.Should().Equal(0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3);
    }

    [Fact]
    public void ProtectedZip_WithBogusHeadersAndCrcs_IsStillReadable()
    {
        using var tmp = new TempDir();
        var path = tmp.File("protected.scs");
        ProtectedZip(path, ModFiles("Protected Mod"));

        using var reader = PackageReader.Open(path);
        ManifestReader.Read(reader).Manifest!.DisplayName.Should().Be("Protected Mod");
    }

    [Fact]
    public void Zip_MissingFile_ReturnsFalse()
    {
        using var tmp = new TempDir();
        var path = tmp.File("mod.zip");
        Zip(path, new Dictionary<string, byte[]> { ["def/a.sii"] = Utf8("x") });

        using var reader = PackageReader.Open(path);
        reader.TryReadFile("manifest.sii", out _).Should().BeFalse();
        ManifestReader.Read(reader).Manifest.Should().BeNull();
    }

    [Fact]
    public void HashFs_ReadsFilesByHash_IncludingLongPaths()
    {
        using var tmp = new TempDir();
        var path = tmp.File("mod.scs");
        var files = ModFiles("HashFS Mod");
        HashFs(path, files);

        PackageReader.DetectKind(path).Should().Be(PackageKind.HashFs);
        using var reader = (HashFsReader)PackageReader.Open(path);
        ManifestReader.Read(reader).Manifest!.DisplayName.Should().Be("HashFS Mod");

        var longPath = files.Keys.Single(k => k.Length > 64);
        reader.TryReadFile(longPath, out var data).Should().BeTrue();
        data.Should().Equal(files[longPath]);
        reader.ListDirectory("").Should().Contain(["manifest.sii", "icon.jpg", "*def"]);
    }

    [Fact]
    public void HashFsV2_ReadsFilesAndListings()
    {
        using var tmp = new TempDir();
        var path = tmp.File("v2.scs");
        var files = ModFiles("HashFS v2 Mod");
        HashFsV2(path, files);

        using var reader = (HashFsReader)PackageReader.Open(path);
        reader.Version.Should().Be(2);
        ManifestReader.Read(reader).Manifest!.DisplayName.Should().Be("HashFS v2 Mod");
        reader.ListDirectory("").Should().Contain(["manifest.sii", "icon.jpg", "*def"]);
        var longPath = files.Keys.Single(k => k.Length > 64);
        reader.TryReadFile(longPath, out var data).Should().BeTrue();
        data.Should().Equal(files[longPath]);
    }

    [Fact]
    public void HashFs_UnknownVersion_IsReportedAsUnsupported()
    {
        using var tmp = new TempDir();
        var path = tmp.File("v3.scs");
        File.WriteAllBytes(path, [.. "SCS#"u8, 3, 0, 0, 0, .. "CITY"u8, .. new byte[40]]);
        var act = () => PackageReader.Open(path);
        act.Should().Throw<PackageFormatException>().WithMessage("*version 3*");
    }

    [Fact]
    public void Folder_ReadsFiles_AndRefusesEscapes()
    {
        using var tmp = new TempDir();
        tmp.File("mod/manifest.sii", Manifest("Folder Mod"));
        tmp.File("secret.txt", "nope");

        using var reader = PackageReader.Open(Path.Combine(tmp.Path, "mod"));
        reader.Kind.Should().Be(PackageKind.Folder);
        ManifestReader.Read(reader).Manifest!.DisplayName.Should().Be("Folder Mod");
        reader.TryReadFile("../secret.txt", out _).Should().BeFalse();
    }

    [Fact]
    public void EncryptedManifest_IsDecoded()
    {
        using var tmp = new TempDir();
        var path = tmp.File("enc.scs");
        Zip(path, new Dictionary<string, byte[]> { ["manifest.sii"] = SiiCodec.Encode3nK(Utf8(Manifest("Encoded")), 7) });

        using var reader = PackageReader.Open(path);
        ManifestReader.Read(reader).Manifest!.DisplayName.Should().Be("Encoded");
    }

    [Fact]
    public void UnknownFormat_Throws()
    {
        using var tmp = new TempDir();
        var path = tmp.File("junk.scs", "definitely not a mod");
        var act = () => PackageReader.Open(path);
        act.Should().Throw<PackageFormatException>();
    }
}
