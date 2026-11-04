using Discover.Core.History;
using Discover.Core.Settings;

namespace Discover.Core.Persistence;

/// <summary>Loads and saves one account's files (favorites.json, lists.json, tags.json, recent.json, settings.json, exports\).</summary>
public sealed class UserLibraryStore
{
    public const string FavoritesFileName = "favorites.json";
    public const string ListsFileName = "lists.json";
    public const string TagsFileName = "tags.json";
    public const string RecentFileName = "recent.json";
    public const string SettingsFileName = "settings.json";

    private readonly JsonFileStore _files;

    public UserLibraryStore(string accountDirectory, Action<string>? log = null)
    {
        _files = new JsonFileStore(accountDirectory, log);
    }

    public string AccountDirectory => _files.Directory;

    /// <summary>The fixed folder export and import use, since the engine has no file-picker API (V20).</summary>
    public string ExportsDirectory => Path.Combine(_files.Directory, "exports");

    public IReadOnlyList<string> RecoveredFiles => _files.Recovered;

    public UserLibrary LoadLibrary(TimeProvider? time = null)
    {
        var library = new UserLibrary(
            time,
            _files.Load(FavoritesFileName, () => new FavoritesFile()),
            _files.Load(ListsFileName, () => new ListsFile()),
            _files.Load(TagsFileName, () => new TagsFile()),
            _files.Load(RecentFileName, () => new RecentFile()));
        return library;
    }

    public const string HistoryFileName = "history.json";

    public HistoryLog LoadHistory() => HistoryLog.FromFile(_files.Load(HistoryFileName, () => new HistoryFile()));

    public void SaveHistory(HistoryLog log) => _files.Save(HistoryFileName, log.ToFile());

    public DiscoverSettings LoadSettings() => _files.Load(SettingsFileName, () => new DiscoverSettings());

    public void SaveSettings(DiscoverSettings settings) => _files.Save(SettingsFileName, settings);

    public void Save(UserLibrary library, LibraryFile file)
    {
        switch (file)
        {
            case LibraryFile.Favorites: _files.Save(FavoritesFileName, library.FavoritesData); break;
            case LibraryFile.Lists: _files.Save(ListsFileName, library.ListsData); break;
            case LibraryFile.Tags: _files.Save(TagsFileName, library.TagsData); break;
            case LibraryFile.Recent: _files.Save(RecentFileName, library.RecentData); break;
        }
    }

    /// <summary>Writes the export file into <see cref="ExportsDirectory"/> and returns its path.</summary>
    public string WriteExport(UserLibrary library, string fileName = ExportFileName)
    {
        var exports = new JsonFileStore(ExportsDirectory);
        exports.Save(Path.GetFileName(fileName), library.Export());
        return exports.PathOf(Path.GetFileName(fileName));
    }

    /// <summary>Reads an export from <see cref="ExportsDirectory"/>. Null when missing or unreadable.</summary>
    public LibraryExport? ReadExport(string fileName = ExportFileName)
    {
        var exports = new JsonFileStore(ExportsDirectory);
        var name = Path.GetFileName(fileName);
        if (!File.Exists(exports.PathOf(name)) && name == ExportFileName && File.Exists(exports.PathOf(LegacyExportFileName)))
        {
            name = LegacyExportFileName; // exported before the mod was renamed
        }

        return File.Exists(exports.PathOf(name))
            ? exports.Load<LibraryExport>(name, () => null!)
            : null;
    }

    public const string ExportFileName = "resonite-discovery-export.json";
    public const string LegacyExportFileName = "galaxy-sieve-export.json";
}

/// <summary>
/// Debounced writes: at most one write per file every <see cref="DiscoverSettings.MinWriteInterval"/> (spec Integration §5).
/// Call <see cref="Tick"/> from the controller's update; call <see cref="Flush"/> on shutdown.
/// </summary>
public sealed class LibrarySaver
{
    private readonly UserLibraryStore _store;
    private readonly UserLibrary _library;
    private readonly TimeProvider _time;
    private readonly TimeSpan _minInterval;
    private readonly HashSet<LibraryFile> _dirty = [];
    private readonly Dictionary<LibraryFile, DateTimeOffset> _lastWrite = [];

    public LibrarySaver(UserLibraryStore store, UserLibrary library, TimeSpan minInterval, TimeProvider? time = null)
    {
        _store = store;
        _library = library;
        _minInterval = minInterval;
        _time = time ?? TimeProvider.System;
        _library.Changed += file => _dirty.Add(file);
    }

    public int PendingFiles => _dirty.Count;

    /// <summary>Writes each dirty file whose last write is old enough. Returns how many files were written.</summary>
    public int Tick()
    {
        var now = _time.GetUtcNow();
        var written = 0;
        foreach (var file in _dirty.ToList())
        {
            if (_lastWrite.TryGetValue(file, out var last) && now - last < _minInterval)
            {
                continue;
            }

            _store.Save(_library, file);
            _lastWrite[file] = now;
            _dirty.Remove(file);
            written++;
        }

        return written;
    }

    /// <summary>Writes everything dirty now, ignoring the interval.</summary>
    public int Flush()
    {
        var now = _time.GetUtcNow();
        var written = 0;
        foreach (var file in _dirty.ToList())
        {
            _store.Save(_library, file);
            _lastWrite[file] = now;
            _dirty.Remove(file);
            written++;
        }

        return written;
    }
}
