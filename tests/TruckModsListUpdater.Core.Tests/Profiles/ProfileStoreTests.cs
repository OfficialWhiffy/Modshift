using System.Text;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;

namespace TruckModsListUpdater.Core.Tests.Profiles;

public class ProfileStoreTests
{
    static string ProfileText(string name, params string[] mods) =>
        "SiiNunit\n{\nuser_profile : _nameless.1 {\n" +
        $" active_mods: {mods.Length}\n" +
        string.Concat(mods.Select((m, i) => $" active_mods[{i}]: \"{m}\"\n")) +
        $" profile_name: \"{name}\"\n}}\n}}\n";

    static string Hex(string name) => Convert.ToHexString(Encoding.UTF8.GetBytes(name));

    [Theory]
    [InlineData("576869666679", "Whiffy")]
    [InlineData("426F7A6F20286D7029", " (mp)")]
    [InlineData("not-hex", "not-hex")]
    public void DecodeFolderName(string folder, string expected)
    {
        ProfileStore.DecodeFolderName(folder).Should().Be(expected);
    }

    [Fact]
    public void Enumerate_FindsLocalAndCloudProfiles_AndSkipsBackups()
    {
        using var tmp = new TempDir();
        var docs = tmp.Dir("docs");
        var remote = tmp.Dir("steam/userdata/1/227300/remote");
        tmp.File($"docs/profiles/{Hex("Local")}/profile.sii", ProfileText("Local"));
        tmp.File($"docs/profiles(1.60.0.0s).bak/{Hex("Old")}/profile.sii", ProfileText("Old"));
        tmp.File($"docs/steam_profiles/{Hex("Cloud")}/controls.sii", "x");
        tmp.File($"steam/userdata/1/227300/remote/profiles/{Hex("Cloud")}/profile.sii", ProfileText("Cloud"));
        tmp.Dir($"docs/profiles/{Hex("Empty")}");

        var install = new GameInstallation(GameDefinition.Ets2, docs, null, [], [remote], null, new HashSet<string>());
        var profiles = ProfileStore.Enumerate(install);

        profiles.Select(p => (p.DisplayName, p.Kind)).Should().BeEquivalentTo(new[]
        {
            ("Local", ProfileKind.Local),
            ("Cloud", ProfileKind.SteamCloud),
        });
    }

    [Fact]
    public void FindActive_UsesLastProfilePathInLog()
    {
        using var tmp = new TempDir();
        var docs = tmp.Dir("docs");
        var remote = tmp.Dir("remote");
        tmp.File($"docs/profiles/{Hex("Local")}/profile.sii", ProfileText("Local"));
        tmp.File($"remote/profiles/{Hex("Cloud")}/profile.sii", ProfileText("Cloud"));
        tmp.File("docs/game.log.txt",
            $"00:00:01 : [GET FILES] File /home/steam_profiles/{Hex("Cloud")}/ffb_lut.sii does not exist.\n" +
            $"00:00:02 : Loading /home/profiles/{Hex("Local")}/profile.sii\n" +
            $"00:00:03 : Loading /steam/profiles/{Hex("Cloud")}/save/autosave/game.sii\n");

        var install = new GameInstallation(GameDefinition.Ets2, docs, null, [], [remote], null, new HashSet<string>());
        var active = ProfileStore.FindActive(install, ProfileStore.Enumerate(install));
        active!.DisplayName.Should().Be("Cloud");
        active.Kind.Should().Be(ProfileKind.SteamCloud);
    }

    [Fact]
    public void Session_LoadsEncryptedProfile()
    {
        using var tmp = new TempDir();
        var path = tmp.File($"profiles/{Hex("P")}/profile.sii");
        File.WriteAllBytes(path, SiiCodec.EncodeScsC(Encoding.UTF8.GetBytes(ProfileText("P", "a|A", "b|B"))));

        var session = ProfileSession.Load(new ProfileRef(ProfileKind.Local, Path.GetDirectoryName(path)!, "P", DateTime.UtcNow));
        session.ActiveMods.Select(m => m.PackageId).Should().Equal("a", "b");
        session.ProfileName.Should().Be("P");
        session.IsWritable.Should().BeTrue();
        session.Document.Container.Should().Be(SiiContainer.ScsC);
    }

    [Fact]
    public void Vdf_ParsesLibraryFolders()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"F:\\Program Files (x86)\\Steam"
                    "apps" { "227300" "123" }
                }
                "1" { "path" "D:\\Steam Games" }
            }
            """;
        var node = VdfNode.Parse(vdf).Child("libraryfolders")!;
        node.Children.Values.Select(c => c["path"]).Should().Equal(@"F:\Program Files (x86)\Steam", @"D:\Steam Games");
        node.Child("0")!.Child("apps")!["227300"].Should().Be("123");
    }

    [Fact]
    public void SteamInstallation_FindsLibrariesUserdataAndWorkshop()
    {
        using var tmp = new TempDir();
        var steam = tmp.Dir("Steam");
        var lib2 = tmp.Dir("Lib2");
        tmp.File("Steam/steamapps/libraryfolders.vdf",
            $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steam.Replace("\\", "\\\\")}\" }} \"1\" {{ \"path\" \"{lib2.Replace("\\", "\\\\")}\" }} }}");
        tmp.File("Lib2/steamapps/appmanifest_227300.acf", "\"AppState\" { \"installdir\" \"Euro Truck Simulator 2\" }");
        tmp.Dir("Lib2/steamapps/common/Euro Truck Simulator 2");
        tmp.Dir("Lib2/steamapps/workshop/content/227300");
        tmp.Dir("Steam/userdata/111/227300/remote");
        tmp.Dir("Steam/userdata/222/227300/remote");
        tmp.File("Steam/config/loginusers.vdf", "\"users\" { \"76561197960265950\" { \"MostRecent\" \"1\" } }"); // account 222

        var s = SteamInstallation.FromSteamDir(steam)!;
        s.Libraries.Should().HaveCount(2);
        s.FindAppInstallDir(227300, "Euro Truck Simulator 2").Should().EndWith(Path.Combine("Lib2", "steamapps", "common", "Euro Truck Simulator 2"));
        s.WorkshopDirs(227300).Should().ContainSingle();
        s.RemoteDirs(227300)[0].Should().Contain(Path.Combine("userdata", "222"));
    }
}
