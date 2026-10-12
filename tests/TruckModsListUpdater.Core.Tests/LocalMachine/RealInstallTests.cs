using System.Diagnostics;
using TruckModsListUpdater.Core.Games;
using TruckModsListUpdater.Core.Mods;
using TruckModsListUpdater.Core.Profiles;
using TruckModsListUpdater.Core.Sii;
using TruckModsListUpdater.Core.Tests.Fixtures;
using Xunit.Abstractions;

namespace TruckModsListUpdater.Core.Tests.LocalMachine;

/// <summary>Read-only end-to-end check of the real install: locate, scan, list profiles, resolve active mods.</summary>
[Trait("Category", "LocalMachine")]
public class RealInstallTests(ITestOutputHelper output)
{
    [LocalMachineFact]
    public async Task LocateScanAndResolveProfiles()
    {
        var install = GameLocator.Locate(GameDefinition.Ets2)!;
        install.Should().NotBeNull();
        output.WriteLine($"Documents: {install.DocumentsDir}");
        output.WriteLine($"Install:   {install.InstallDir}  version {install.GameVersion}  DLCs {install.InstalledDlc.Count}");
        output.WriteLine($"Workshop:  {string.Join(", ", install.WorkshopDirs)}");
        output.WriteLine($"Cloud:     {string.Join(", ", install.SteamRemoteDirs)}");

        using var tmp = new TempDir();
        var cache = new ModMetadataCache(tmp.Path);
        var sw = Stopwatch.StartNew();
        var catalog = await new ModScanner(cache).ScanAsync(install);
        var cold = sw.Elapsed;
        cache.Save();
        sw.Restart();
        var warmCache = new ModMetadataCache(tmp.Path);
        warmCache.Load();
        await new ModScanner(warmCache).ScanAsync(install);
        output.WriteLine($"Scan: {catalog.Packages.Count} packages, cold {cold.TotalMilliseconds:F0} ms, cached {sw.Elapsed.TotalMilliseconds:F0} ms");

        foreach (var p in catalog.Packages)
        {
            var issues = CompatibilityChecker.Check(p, install).Where(i => i.Severity != IssueSeverity.Info).Select(i => i.Kind);
            output.WriteLine($"  {p.Source,-8} {p.Kind,-7} {p.PackageId,-45} {p.DisplayName,-50} {(p.IconPath != null ? "icon" : "    ")} {string.Join(",", issues)}");
        }

        var profiles = ProfileStore.Enumerate(install);
        var active = ProfileStore.FindActive(install, profiles);
        output.WriteLine($"Profiles: {profiles.Count}, active: {active?.DisplayName} ({active?.Kind})");
        foreach (var profile in profiles)
        {
            var session = ProfileSession.Load(profile);
            var missing = session.ActiveMods.Count(m => catalog.Find(m.PackageId) == null);
            output.WriteLine($"  {profile.Kind,-10} {profile.DisplayName,-25} {session.Document.Container}/{session.Document.Content} mods {session.ActiveMods.Count}, missing {missing}");
            foreach (var m in LoadOrderMapper.ToDisplayOrder(session.ActiveMods))
                output.WriteLine($"      {(catalog.Find(m.PackageId) == null ? "MISSING " : "        ")}{m.PackageId} | {m.DisplayName}");
            foreach (var (m, r) in MissingModResolver.Suggest(session.ActiveMods, catalog))
                output.WriteLine($"      FIX     {m.PackageId} -> {r.PackageId}");
        }

        catalog.Packages.Should().Contain(p => p.Source == ModSource.Local);
        profiles.Should().NotBeEmpty();
    }
}
