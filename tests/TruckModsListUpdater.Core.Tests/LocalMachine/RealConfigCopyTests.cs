using Moq;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Platform;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Tests.Fixtures;
using Xunit.Abstractions;

namespace TruckModsListUpdater.Core.Tests.LocalMachine;

/// <summary>
/// Copies settings between <b>copies</b> of the real profiles (the real files are only read) and checks
/// that a restore gives back the exact original bytes.
/// </summary>
[Trait("Category", "LocalMachine")]
public class RealConfigCopyTests(ITestOutputHelper output)
{
    [LocalMachineFact]
    public void CopyAndRestoreBetweenCopiesOfRealProfiles()
    {
        var real = GameLocator.Locate(GameDefinition.Ets2)!;
        var realProfiles = ProfileStore.Enumerate(real);
        output.WriteLine($"{realProfiles.Count} profiles");
        foreach (var p in realProfiles)
        {
            var files = ProfileConfigs.All.SelectMany(c => ProfileConfigs.Find(real, p, c)).Select(f => $"{f.FileName}@{f.Root}");
            output.WriteLine($"  {p.Kind,-10} {p.DisplayName,-25} {string.Join(", ", files)}");
        }
        if (realProfiles.Count < 2) return;

        using var tmp = new TempDir();
        var docs = tmp.Dir("docs");
        var remote = tmp.Dir("remote");
        var install = real with { DocumentsDir = docs, SteamRemoteDirs = [remote] };
        foreach (var p in realProfiles)
        {
            CopyTopLevel(p.Dir, Path.Combine(p.IsCloud ? remote : docs, "profiles", p.FolderName));
            CopyTopLevel(Path.Combine(real.DocumentsDir, "steam_profiles", p.FolderName), Path.Combine(docs, "steam_profiles", p.FolderName));
        }

        var profiles = ProfileStore.Enumerate(install);
        // The richest profile as the source, the next one as the target.
        var source = profiles.OrderByDescending(p => ProfileConfigs.All.Sum(c => ProfileConfigs.Find(install, p, c).Count)).First();
        var target = profiles.First(p => p != source);
        var before = Snapshot(tmp.Path);

        var copier = new ProfileConfigCopier(new Mock<IGameProcessMonitor>().Object, new ConfigBackups(tmp.Dir("backups")));
        var plan = ProfileConfigCopier.Plan(install, source, target, ProfileConfigs.All);
        foreach (var f in plan.Files) output.WriteLine($"  {f.Action,-9} {f.FileName} -> {f.TargetRoot}");
        var result = copier.Execute(plan);

        foreach (var f in result.Written) File.ReadAllBytes(f.TargetPath).Should().Equal(File.ReadAllBytes(f.SourcePath));
        File.ReadAllBytes(target.SiiPath).Should().Equal(before[target.SiiPath]);

        if (result.Backup != null) copier.Restore(install, target, result.Backup);
        Snapshot(tmp.Path).Where(kv => !kv.Key.Contains("backups")).Should().BeEquivalentTo(before);
    }

    static void CopyTopLevel(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    }

    static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains("backups"))
            .ToDictionary(p => p, File.ReadAllBytes);
}
