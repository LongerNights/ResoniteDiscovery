using System.Text.Json;
using System.Text.Json.Serialization;

namespace Discover.Core.Persistence;

/// <summary>
/// One folder of JSON files (spec Integration §5). Writes are atomic (temp file, then replace); a file that cannot be
/// read is renamed to <c>.bad</c>, reported, and replaced with defaults. Never stores session user lists.
/// </summary>
public sealed class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new Lenses.LegacyNames.LensConverter(), new Lenses.LegacyNames.SectionConverter(), new JsonStringEnumConverter() },
    };

    /// <summary>The same options without indentation, for large machine-written files (history).</summary>
    public static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    /// <summary>Larger files are treated as corrupt rather than parsed on the update thread.</summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    private readonly Action<string>? _log;

    public JsonFileStore(string directory, Action<string>? log = null)
    {
        Directory = directory;
        _log = log;
    }

    public string Directory { get; }

    /// <summary>File names recovered since this store was created, for a one-time user notice.</summary>
    public List<string> Recovered { get; } = [];

    public string PathOf(string fileName) => System.IO.Path.Combine(Directory, fileName);

    /// <summary>Returns the stored value, or <paramref name="defaults"/> when the file is missing, empty or corrupt.</summary>
    public T Load<T>(string fileName, Func<T> defaults) where T : class
    {
        var path = PathOf(fileName);
        if (!File.Exists(path))
        {
            return defaults();
        }

        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                throw new JsonException("File too large.");
            }

            using var stream = File.OpenRead(path);
            var value = JsonSerializer.Deserialize<T>(stream, Options);
            if (value is not null)
            {
                return value;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // fall through to recovery
        }

        Quarantine(path, fileName);
        return defaults();
    }

    /// <summary>Writes to a temp file in the same folder, then replaces the target, so a crash never leaves half a file.</summary>
    public void Save<T>(string fileName, T value, bool indented = true) where T : class
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathOf(fileName);
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, indented ? Options : CompactOptions);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception) { /* leave it; the next save overwrites it */ }
            throw;
        }
    }

    private void Quarantine(string path, string fileName)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Could not rename; the next Save will replace it anyway.
        }

        Recovered.Add(fileName);
        _log?.Invoke($"[Discover] {fileName} was unreadable; renamed to .bad and reset to defaults");
    }
}

public static class AccountPaths
{
    /// <summary>
    /// <c>&lt;root&gt;\ResoniteDiscovery\&lt;userId&gt;\</c>, one folder per account (the mod's earlier name,
    /// <c>GalaxySieve</c>, is copied over once if its folder is the only one there). The caller passes
    /// <c>Environment.SpecialFolder.ApplicationData</c>. Anything but letters, digits, '-' and '_' in the id becomes '_',
    /// so an id can never escape the folder.
    /// </summary>
    public static string AccountDirectory(string root, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        var safe = new string(userId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        var current = Path.Combine(root, "ResoniteDiscovery", safe);
        var legacy = Path.Combine(root, "GalaxySieve", safe);
        if (!Directory.Exists(current) && Directory.Exists(legacy))
        {
            try
            {
                CopyDirectory(legacy, current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The new folder starts empty; the old one is left as it was.
            }
        }

        return current;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }
}
