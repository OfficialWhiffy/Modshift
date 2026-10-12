using System.IO.Compression;
using System.Text;
using Moq;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;

namespace TruckModsListUpdater.Core.Tests.Profiles;

public class ProfileWriterTests : IDisposable
{
    readonly TempDir _tmp = new();
    readonly Mock<IGameProcessMonitor> _monitor = new();
    readonly ProfileRef _profile;
    readonly ProfileBackups _backups;

    const string Text =
        "SiiNunit\r\n{\r\nuser_profile : _nameless.1 {\r\n face: 0\r\n active_mods: 2\r\n" +
        " active_mods[0]: \"a|A\"\r\n active_mods[1]: \"b|B\"\r\n profile_name: \"P\"\r\n}\r\n\r\n}\r\n";

    public ProfileWriterTests()
    {
        var dir = _tmp.Dir("profiles/50");
        File.WriteAllBytes(Path.Combine(dir, "profile.sii"), SiiCodec.EncodeScsC(Encoding.UTF8.GetBytes(Text)));
        File.WriteAllText(Path.Combine(dir, "profile.bak.sii"), "game's own fallback");
        _tmp.File("profiles/50/save/autosave/game.sii", "save data");
        _profile = new ProfileRef(ProfileKind.Local, dir, "P", DateTime.UtcNow);
        _backups = new ProfileBackups(_tmp.Dir("backups"));
    }

    public void Dispose() => _tmp.Dispose();

    ProfileWriter Writer() => new(_monitor.Object, _backups);

    [Fact]
    public void Write_ReplacesModList_AsPlainTextWithoutBom()
    {
        var session = ProfileSession.Load(_profile);
        var mods = new[] { session.ActiveMods[1], ActiveModEntry.Create("c", "C") };

        var result = Writer().WriteActiveMods(GameDefinition.Ets2, session, mods);

        var bytes = File.ReadAllBytes(_profile.SiiPath);
        bytes.AsSpan(0, 8).ToArray().Should().Equal("SiiNunit"u8.ToArray());
        result.Session.ActiveMods.Select(m => m.PackageId).Should().Equal("b", "c");
        result.Session.Document.Container.Should().Be(SiiContainer.Plain);
        File.ReadAllText(Path.Combine(_profile.Dir, "profile.bak.sii")).Should().Be("game's own fallback");
        File.Exists(_profile.SiiPath + ProfileWriter.TempSuffix).Should().BeFalse();
        Encoding.UTF8.GetString(bytes).Should().Be(Text
            .Replace(" active_mods[0]: \"a|A\"\r\n active_mods[1]: \"b|B\"", " active_mods[0]: \"b|B\"\r\n active_mods[1]: \"c|C\""));
    }

    [Fact]
    public void Write_CreatesBackupOfProfileFiles_OutsideProfileFolder()
    {
        var original = File.ReadAllBytes(_profile.SiiPath);
        var result = Writer().WriteActiveMods(GameDefinition.Ets2, ProfileSession.Load(_profile), []);

        result.Backup.Path.Should().StartWith(Path.Combine(_tmp.Path, "backups"));
        using var zip = ZipFile.OpenRead(result.Backup.Path);
        zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo("profile.sii", "profile.bak.sii");
        ProfileBackups.ReadProfileSii(result.Backup).Should().Equal(original);
        Directory.GetFiles(_profile.Dir).Select(Path.GetFileName).Should().BeEquivalentTo("profile.sii", "profile.bak.sii");
    }

    [Fact]
    public void Write_RefusesWhileGameRuns()
    {
        _monitor.Setup(m => m.IsRunning(GameDefinition.Ets2)).Returns(true);
        var before = File.ReadAllBytes(_profile.SiiPath);

        var act = () => Writer().WriteActiveMods(GameDefinition.Ets2, ProfileSession.Load(_profile), []);

        act.Should().Throw<GameRunningException>();
        File.ReadAllBytes(_profile.SiiPath).Should().Equal(before);
    }

    [Fact]
    public void Write_RefusesWhenFileChangedSinceLoad()
    {
        var session = ProfileSession.Load(_profile);
        File.WriteAllBytes(_profile.SiiPath, SiiCodec.EncodeScsC(Encoding.UTF8.GetBytes(Text.Replace("face: 0", "face: 1"))));

        var act = () => Writer().WriteActiveMods(GameDefinition.Ets2, session, []);
        act.Should().Throw<ProfileChangedException>();
    }

    [Fact]
    public void Write_RefusesBinaryProfiles()
    {
        File.WriteAllBytes(_profile.SiiPath, BsiiFixtureAccess.UserProfile());
        var act = () => Writer().WriteActiveMods(GameDefinition.Ets2, ProfileSession.Load(_profile), []);
        act.Should().Throw<ProfileWriteException>().WithMessage("*binary*");
    }

    [Fact]
    public void Write_GameStartsDuringWrite_LeavesOriginalIntact()
    {
        // First check passes, the re-check right before the swap sees the game.
        _monitor.SetupSequence(m => m.IsRunning(GameDefinition.Ets2)).Returns(false).Returns(true);
        var before = File.ReadAllBytes(_profile.SiiPath);

        var act = () => Writer().WriteActiveMods(GameDefinition.Ets2, ProfileSession.Load(_profile), []);

        act.Should().Throw<GameRunningException>();
        File.ReadAllBytes(_profile.SiiPath).Should().Equal(before);
        File.Exists(_profile.SiiPath + ProfileWriter.TempSuffix).Should().BeFalse();
    }

    [Fact]
    public void Restore_PutsBackupContentBack()
    {
        var session = ProfileSession.Load(_profile);
        var original = File.ReadAllBytes(_profile.SiiPath);
        var first = Writer().WriteActiveMods(GameDefinition.Ets2, session, []);

        var restored = Writer().Restore(GameDefinition.Ets2, _profile, first.Backup);

        File.ReadAllBytes(_profile.SiiPath).Should().Equal(original);
        restored.Session.ActiveMods.Select(m => m.PackageId).Should().Equal("a", "b");
    }

    [Fact]
    public void Backups_ArePruned_KeepingLatestAndOnePerDay()
    {
        var backups = new ProfileBackups(_tmp.Dir("prune"), keepLatest: 3, keepDailyDays: 5);
        var now = new DateTime(2026, 9, 27, 12, 0, 0);
        for (int i = 0; i < 10; i++) backups.Create(GameDefinition.Ets2, _profile, now.AddDays(-i).AddMinutes(-1));
        for (int i = 0; i < 4; i++) backups.Create(GameDefinition.Ets2, _profile, now.AddMinutes(i));

        var kept = backups.List(GameDefinition.Ets2, _profile).Select(b => b.CreatedLocal).ToList();
        // 3 newest (today), plus one per day for the 5 previous days (days 1..5); older ones are gone.
        kept.Should().HaveCount(3 + 5);
        kept.Min().Should().Be(now.AddDays(-5).AddMinutes(-1));
    }
}

static class BsiiFixtureAccess
{
    public static byte[] UserProfile() => Sii.BsiiFixture.UserProfile(["x|X"]);
}
