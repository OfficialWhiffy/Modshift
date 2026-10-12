using TruckModsListUpdater.Core.Archives;
using TruckModsListUpdater.Core.Sii;
using Xunit.Abstractions;

namespace TruckModsListUpdater.Core.Tests.LocalMachine;

[Trait("Category", "LocalMachine")]
public class RealPackageTests(ITestOutputHelper output)
{
    static string ModDir => Path.Combine(LocalMachineFactAttribute.Ets2Documents, "mod");

    static List<string> PackageFiles() =>
        Directory.GetFiles(ModDir).Where(f => f.EndsWith(".scs") || f.EndsWith(".zip")).ToList();

    [LocalMachineFact]
    public void EveryLocalPackage_HasReadableManifest_OrNoneAtAll()
    {
        var files = PackageFiles();
        files.Should().NotBeEmpty();

        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                using var reader = PackageReader.Open(file);
                if (!reader.TryReadFile("manifest.sii", out var data))
                {
                    // Older mods ship without a manifest; that is fine only if the root really has none.
                    bool listed = reader is HashFsReader h
                        ? h.ListDirectory("").Contains("manifest.sii")
                        : ((ZipPackageReader)reader).EntryNames.Contains("manifest.sii");
                    if (listed) failures.Add($"{Path.GetFileName(file)} ({reader.Kind}): manifest.sii listed but unreadable");
                    output.WriteLine($"{Path.GetFileName(file),-50} {reader.Kind,-7} (no manifest)");
                    continue;
                }
                var unit = SiiCodec.Decode(data).ReadUnits().Single(u => u.Type == "mod_package");
                output.WriteLine($"{Path.GetFileName(file),-50} {reader.Kind,-7} {unit.Get("display_name")} {unit.Get("package_version")}");
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.GetType().Name}: {e.Message}");
            }
        }
        failures.Should().BeEmpty();
    }

    [LocalMachineFact]
    public void HashFs_EveryListedPath_IsFoundByHash()
    {
        // Walks the directory listings; paths longer than 64 bytes exercise CityHash's chunk loop.
        int checkedPaths = 0, longPaths = 0;
        foreach (var file in PackageFiles().Where(f => PackageReader.DetectKind(f) == PackageKind.HashFs).Take(3))
        {
            using var reader = HashFsReader.Open(file);
            var missing = new List<string>();
            int total = 0;
            var queue = new Queue<string>([""]);
            while (queue.Count > 0 && checkedPaths < 50_000)
            {
                var dir = queue.Dequeue();
                foreach (var name in reader.ListDirectory(dir))
                {
                    bool isDir = name.StartsWith('*');
                    var path = (dir.Length == 0 ? "" : dir + "/") + name.TrimStart('*');
                    checkedPaths++;
                    total++;
                    if (path.Length > 64) longPaths++;
                    if (!reader.Contains(path)) missing.Add(path);
                    if (isDir) queue.Enqueue(path);
                }
            }
            // Archives may list a few (empty) directories they never store; hashing bugs miss nearly everything.
            missing.Count.Should().BeLessThan(Math.Max(5, total / 100), $"listed paths of {Path.GetFileName(file)} should hash to entries");
            missing.Where(m => m.Length > 64).Should().BeEmpty();
        }
        output.WriteLine($"{checkedPaths} paths checked, {longPaths} longer than 64 bytes");
        longPaths.Should().BeGreaterThan(0);
    }
}
