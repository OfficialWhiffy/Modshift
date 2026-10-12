using Moq;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;
using Xunit.Abstractions;

namespace TruckModsListUpdater.Core.Tests.LocalMachine;

/// <summary>Writes to COPIES of the real profiles in a temp folder; the game's files are only read.</summary>
[Trait("Category", "LocalMachine")]
public class RealProfileWriteTests(ITestOutputHelper output)
{
    [LocalMachineFact]
    public void RewritingCopiesOfRealProfiles_ChangesOnlyTheModList()
    {
        var install = GameLocator.Locate(GameDefinition.Ets2)!;
        var profiles = ProfileStore.Enumerate(install);
        profiles.Should().NotBeEmpty();

        using var tmp = new TempDir();
        var monitor = new Mock<IGameProcessMonitor>();
        var writer = new ProfileWriter(monitor.Object, new ProfileBackups(tmp.Dir("backups")));

        foreach (var real in profiles)
        {
            var copyDir = tmp.Dir(Path.Combine(real.IsCloud ? "cloud" : "local", real.FolderName));
            File.Copy(real.SiiPath, Path.Combine(copyDir, "profile.sii"));
            var copy = real with { Dir = copyDir };

            var session = ProfileSession.Load(copy);
            var originalText = session.Document.Text!;
            var reversed = session.ActiveMods.Reverse().Append(ActiveModEntry.Create("tmm_test_mod", "Test \"mod\" ü")).ToList();

            var result = writer.WriteActiveMods(GameDefinition.Ets2, session, reversed);

            result.Session.ActiveMods.Select(m => m.RawValue).Should().Equal(reversed.Select(m => m.RawValue));
            ActiveModsBlock.WithoutBlock(result.Session.Document.Text!).Should().Be(ActiveModsBlock.WithoutBlock(originalText));
            result.Session.ProfileName.Should().Be(session.ProfileName);

            // Restoring the backup gives back the exact original (encrypted) bytes.
            writer.Restore(GameDefinition.Ets2, copy, result.Backup);
            File.ReadAllBytes(copy.SiiPath).Should().Equal(File.ReadAllBytes(real.SiiPath));
            output.WriteLine($"{real.Kind,-10} {real.DisplayName,-22} {session.ActiveMods.Count} mods rewritten and restored");
        }
    }
}
