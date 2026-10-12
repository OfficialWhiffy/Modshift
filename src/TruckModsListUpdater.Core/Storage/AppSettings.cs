using System.Text.Json;

namespace TruckModsListUpdater.Core.Storage;

/// <summary>User preferences, stored as JSON in %APPDATA%\Modshift\settings.json.</summary>
public sealed class AppSettings
{
    public string? LastGameId { get; set; }
    public string? LastProfileDir { get; set; }
    public string? DocumentsOverride { get; set; }
    public bool CloudWarningAcknowledged { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

/// <summary>Minimal append-only daily log file; logging must never break the app.</summary>
public static class AppLog
{
    static string? _dir;
    static readonly object Lock = new();

    public static void Initialize(string logsDir) => _dir = logsDir;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? e = null) => Write("ERROR", e == null ? message : $"{message}: {e}");

    static void Write(string level, string message)
    {
        if (_dir == null) return;
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(Path.Combine(_dir, $"app-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
