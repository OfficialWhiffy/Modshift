using TruckModsListUpdater.Core.Storage;

namespace TruckModsListUpdater.Core.Tests;

public sealed class AppPathsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "modshift-paths-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    string Roaming => Path.Combine(_root, "roaming");
    string Local => Path.Combine(_root, "local");

    [Fact]
    public void Moves_the_old_folders_to_the_new_names()
    {
        Directory.CreateDirectory(Path.Combine(Roaming, AppPaths.LegacyAppFolderName, "presets"));
        File.WriteAllText(Path.Combine(Roaming, AppPaths.LegacyAppFolderName, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(Local, AppPaths.LegacyAppFolderName, "backups"));

        AppPaths.MigrateLegacyFolders(Roaming, Local);

        Assert.True(File.Exists(Path.Combine(Roaming, AppPaths.AppFolderName, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(Local, AppPaths.AppFolderName, "backups")));
        Assert.False(Directory.Exists(Path.Combine(Roaming, AppPaths.LegacyAppFolderName)));
        Assert.False(Directory.Exists(Path.Combine(Local, AppPaths.LegacyAppFolderName)));
    }

    [Fact]
    public void Leaves_everything_alone_when_the_new_folder_already_exists()
    {
        Directory.CreateDirectory(Path.Combine(Roaming, AppPaths.LegacyAppFolderName));
        Directory.CreateDirectory(Path.Combine(Roaming, AppPaths.AppFolderName));

        AppPaths.MigrateLegacyFolders(Roaming, Local);

        Assert.True(Directory.Exists(Path.Combine(Roaming, AppPaths.LegacyAppFolderName)));
    }

    [Fact]
    public void Does_nothing_when_there_is_no_old_folder()
    {
        Directory.CreateDirectory(Roaming);

        AppPaths.MigrateLegacyFolders(Roaming, Local);

        Assert.False(Directory.Exists(Path.Combine(Roaming, AppPaths.AppFolderName)));
    }
}
