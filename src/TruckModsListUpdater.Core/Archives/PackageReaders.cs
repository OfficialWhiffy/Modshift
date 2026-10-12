namespace TruckModsListUpdater.Core.Archives;

/// <summary>Kind of mod package on disk.</summary>
public enum PackageKind { Zip, HashFs, Folder, Unknown }

/// <summary>
/// Read-only access to single small files (manifest, icon, description) inside a mod package.
/// Implementations read only the index and the requested entries, never the whole archive.
/// </summary>
public interface IPackageReader : IDisposable
{
    PackageKind Kind { get; }

    /// <summary>Reads a file by its path relative to the package root ("manifest.sii").</summary>
    bool TryReadFile(string path, out byte[] data);
}

public sealed class PackageFormatException(string message, Exception? inner = null) : Exception(message, inner);

public static class PackageReader
{
    /// <summary>Largest single file we read out of a package (icons and manifests are tiny).</summary>
    public const int MaxEntrySize = 16 * 1024 * 1024;

    /// <summary>Detects the package kind from its first bytes (or directory-ness).</summary>
    public static PackageKind DetectKind(string path)
    {
        if (Directory.Exists(path)) return PackageKind.Folder;
        if (!File.Exists(path)) return PackageKind.Unknown;
        Span<byte> magic = stackalloc byte[4];
        using var fs = OpenShared(path);
        if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4) return PackageKind.Unknown;
        if (magic.SequenceEqual("SCS#"u8)) return PackageKind.HashFs;
        if (magic[0] == 'P' && magic[1] == 'K') return PackageKind.Zip;
        return PackageKind.Unknown;
    }

    /// <summary>Opens the right reader for a .scs/.zip file or a folder mod.</summary>
    public static IPackageReader Open(string path) => DetectKind(path) switch
    {
        PackageKind.Folder => new FolderPackageReader(path),
        PackageKind.HashFs => HashFsReader.Open(path),
        PackageKind.Zip => new ZipPackageReader(path),
        _ => throw new PackageFormatException($"Unrecognised package format: {Path.GetFileName(path)}"),
    };

    /// <summary>Opens for reading without blocking the game (or Steam) from using the file.</summary>
    internal static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);

    internal static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}

public sealed class FolderPackageReader(string root) : IPackageReader
{
    public PackageKind Kind => PackageKind.Folder;

    public bool TryReadFile(string path, out byte[] data)
    {
        var full = Path.GetFullPath(Path.Combine(root, PackageReader.Normalize(path)));
        // Refuse paths that escape the package folder ("../").
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)
            || new FileInfo(full).Length > PackageReader.MaxEntrySize)
        {
            data = [];
            return false;
        }
        data = File.ReadAllBytes(full);
        return true;
    }

    public void Dispose() { }
}
