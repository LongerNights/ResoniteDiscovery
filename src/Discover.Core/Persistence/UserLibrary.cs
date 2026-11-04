using Discover.Core.Model;

namespace Discover.Core.Persistence;

public enum LibraryFile
{
    Favorites,
    Lists,
    Tags,
    Recent,
}

public sealed class FavoriteEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime StarredAt { get; set; }
}

public sealed class ListMember
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime AddedAt { get; set; }
}

public sealed class SavedList
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public List<ListMember> Members { get; set; } = [];
}

public sealed class RecentEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime OpenedAt { get; set; }
}

public sealed class FavoritesFile
{
    public int Version { get; set; } = 1;
    public List<FavoriteEntry> Items { get; set; } = [];
}

public sealed class ListsFile
{
    public int Version { get; set; } = 1;
    public List<SavedList> Lists { get; set; } = [];
}

public sealed class TagsFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, List<string>> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RecentFile
{
    public int Version { get; set; } = 1;
    public List<RecentEntry> Items { get; set; } = [];

    /// <summary>Keys of every world seen open (oldest first), so cards can say "visited". Capped at UserLibrary.MaxVisited.</summary>
    public List<string> Visited { get; set; } = [];
}

/// <summary>One versioned file holding favorites, lists and tags (spec F23).</summary>
public sealed class LibraryExport
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public DateTime ExportedAt { get; set; }
    public List<FavoriteEntry> Favorites { get; set; } = [];
    public List<SavedList> Lists { get; set; } = [];
    public Dictionary<string, List<string>> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ImportResult(int FavoritesAdded, int ListsAdded, int ListMembersAdded, int TagsAdded);

/// <summary>
/// The user's favorites, named lists, personal tags and recents for one account (spec F9, F16, F21 to F23).
/// Only world keys can be saved; standalone sessions are refused. Time comes from an injected <see cref="TimeProvider"/>.
/// Not thread-safe: the controller owns it.
/// </summary>
public sealed class UserLibrary
{
    public const int MaxNameLength = 64;
    public const int MaxTagLength = 32;
    public const int MaxTagsPerWorld = 32;
    public const int MaxRecent = 50;

    private readonly TimeProvider _time;
    private readonly FavoritesFile _favorites;
    private readonly ListsFile _lists;
    private readonly TagsFile _tags;
    private readonly RecentFile _recent;

    public UserLibrary(TimeProvider? time = null, FavoritesFile? favorites = null, ListsFile? lists = null, TagsFile? tags = null, RecentFile? recent = null)
    {
        _time = time ?? TimeProvider.System;
        _favorites = favorites ?? new FavoritesFile();
        _lists = lists ?? new ListsFile();
        _tags = tags ?? new TagsFile();
        _recent = recent ?? new RecentFile();
    }

    /// <summary>Raised after any change, with the file that now needs saving.</summary>
    public event Action<LibraryFile>? Changed;

    internal FavoritesFile FavoritesData => _favorites;
    internal ListsFile ListsData => _lists;
    internal TagsFile TagsData => _tags;
    internal RecentFile RecentData => _recent;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ---- Favorites

    public IReadOnlyList<FavoriteEntry> Favorites => _favorites.Items;

    public bool IsStarred(string key) => _favorites.Items.Any(f => KeyEquals(f.Key, key));

    /// <summary>Stars a world. Idempotent (the original date is kept). False for standalone sessions or empty keys.</summary>
    public bool Star(string key, string name)
    {
        if (!DiscoveryKeys.IsSavable(key))
        {
            return false;
        }

        if (!IsStarred(key))
        {
            _favorites.Items.Add(new FavoriteEntry { Key = key, Name = Clean(name), StarredAt = Now });
            Changed?.Invoke(LibraryFile.Favorites);
        }

        return true;
    }

    public bool Unstar(string key)
    {
        var removed = _favorites.Items.RemoveAll(f => KeyEquals(f.Key, key)) > 0;
        if (removed)
        {
            Changed?.Invoke(LibraryFile.Favorites);
        }

        return removed;
    }

    // ---- Named lists

    public IReadOnlyList<SavedList> Lists => _lists.Lists.OrderBy(l => l.Order).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public SavedList? GetList(string listId) => _lists.Lists.FirstOrDefault(l => l.Id == listId);

    /// <summary>Creates a list at the end. Null for a blank name. Duplicate names are allowed (ids differ).</summary>
    public SavedList? CreateList(string name)
    {
        name = Clean(name);
        if (name.Length == 0)
        {
            return null;
        }

        var list = new SavedList
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Order = _lists.Lists.Count == 0 ? 0 : _lists.Lists.Max(l => l.Order) + 1,
        };
        _lists.Lists.Add(list);
        Changed?.Invoke(LibraryFile.Lists);
        return list;
    }

    public bool RenameList(string listId, string name)
    {
        name = Clean(name);
        var list = GetList(listId);
        if (list is null || name.Length == 0)
        {
            return false;
        }

        list.Name = name;
        Changed?.Invoke(LibraryFile.Lists);
        return true;
    }

    /// <summary>Applies the given order; ids not mentioned keep their relative order after the mentioned ones.</summary>
    public void ReorderLists(IEnumerable<string> listIdsInOrder)
    {
        var order = 0;
        var seen = new HashSet<string>();
        foreach (var id in listIdsInOrder)
        {
            if (GetList(id) is { } list && seen.Add(id))
            {
                list.Order = order++;
            }
        }

        foreach (var list in _lists.Lists.Where(l => !seen.Contains(l.Id)).OrderBy(l => l.Order).ToList())
        {
            list.Order = order++;
        }

        Changed?.Invoke(LibraryFile.Lists);
    }

    public bool DeleteList(string listId)
    {
        var removed = _lists.Lists.RemoveAll(l => l.Id == listId) > 0;
        if (removed)
        {
            Changed?.Invoke(LibraryFile.Lists);
        }

        return removed;
    }

    /// <summary>A world may be in many lists. False for standalone sessions or an unknown list.</summary>
    public bool AddToList(string listId, string key, string name)
    {
        var list = GetList(listId);
        if (list is null || !DiscoveryKeys.IsSavable(key))
        {
            return false;
        }

        if (!list.Members.Any(m => KeyEquals(m.Key, key)))
        {
            list.Members.Add(new ListMember { Key = key, Name = Clean(name), AddedAt = Now });
            Changed?.Invoke(LibraryFile.Lists);
        }

        return true;
    }

    public bool RemoveFromList(string listId, string key)
    {
        var removed = GetList(listId)?.Members.RemoveAll(m => KeyEquals(m.Key, key)) > 0;
        if (removed)
        {
            Changed?.Invoke(LibraryFile.Lists);
        }

        return removed;
    }

    public IReadOnlyList<string> ListIdsContaining(string key) => _lists.Lists
        .Where(l => l.Members.Any(m => KeyEquals(m.Key, key)))
        .Select(l => l.Id)
        .ToList();

    // ---- Personal tags

    public IReadOnlyList<string> TagsFor(string key) =>
        _tags.Tags.TryGetValue(key, out var tags) ? tags : [];

    /// <summary>Every personal tag on every saved item, repeats included (for tag suggestions).</summary>
    public IEnumerable<string> AllTags => _tags.Tags.Values.SelectMany(t => t);

    public bool HasTagNamed(string tag) =>
        _tags.Tags.Values.Any(list => list.Contains(NormalizeTag(tag), StringComparer.Ordinal));

    /// <summary>Adds a lower-cased, trimmed tag. False for standalone sessions, blank tags or the per-world limit.</summary>
    public bool AddTag(string key, string tag)
    {
        tag = NormalizeTag(tag);
        if (!DiscoveryKeys.IsSavable(key) || tag.Length == 0)
        {
            return false;
        }

        if (!_tags.Tags.TryGetValue(key, out var tags))
        {
            tags = [];
            _tags.Tags[key] = tags;
        }

        if (tags.Contains(tag, StringComparer.Ordinal))
        {
            return true;
        }

        if (tags.Count >= MaxTagsPerWorld)
        {
            return false;
        }

        tags.Add(tag);
        Changed?.Invoke(LibraryFile.Tags);
        return true;
    }

    public bool RemoveTag(string key, string tag)
    {
        if (!_tags.Tags.TryGetValue(key, out var tags) || tags.RemoveAll(t => t == NormalizeTag(tag)) == 0)
        {
            return false;
        }

        if (tags.Count == 0)
        {
            _tags.Tags.Remove(key);
        }

        Changed?.Invoke(LibraryFile.Tags);
        return true;
    }

    // ---- Recent

    public IReadOnlyList<RecentEntry> Recent => _recent.Items;

    public const int MaxVisited = 5000;
    private HashSet<string>? _visitedSet;

    private HashSet<string> VisitedSet => _visitedSet ??= new HashSet<string>(
        _recent.Visited.Concat(_recent.Items.Select(r => r.Key)), StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a world the user has been in (open while this mod ran) or opened from here.</summary>
    public bool IsVisited(string key) => VisitedSet.Contains(key);

    /// <summary>Notes that the user has been in this world. Saves only when it is new.</summary>
    public void MarkVisited(string key)
    {
        if (!DiscoveryKeys.IsSavable(key) || !VisitedSet.Add(key))
        {
            return;
        }

        _recent.Visited.Add(key);
        if (_recent.Visited.Count > MaxVisited)
        {
            _recent.Visited.RemoveRange(0, _recent.Visited.Count - MaxVisited);
        }

        Changed?.Invoke(LibraryFile.Recent);
    }

    /// <summary>Notes a world opened through the screen: newest first, no duplicates, capped at <see cref="MaxRecent"/>.</summary>
    public void RecordOpened(string key, string name)
    {
        if (!DiscoveryKeys.IsSavable(key))
        {
            return;
        }

        VisitedSet.Add(key);
        if (!_recent.Visited.Any(v => KeyEquals(v, key)))
        {
            _recent.Visited.Add(key);
        }

        _recent.Items.RemoveAll(r => KeyEquals(r.Key, key));
        _recent.Items.Insert(0, new RecentEntry { Key = key, Name = Clean(name), OpenedAt = Now });
        if (_recent.Items.Count > MaxRecent)
        {
            _recent.Items.RemoveRange(MaxRecent, _recent.Items.Count - MaxRecent);
        }

        Changed?.Invoke(LibraryFile.Recent);
    }

    // ---- Export and import (F23)

    public LibraryExport Export() => new()
    {
        ExportedAt = Now,
        Favorites = _favorites.Items.Select(f => new FavoriteEntry { Key = f.Key, Name = f.Name, StarredAt = f.StarredAt }).ToList(),
        Lists = Lists.Select(l => new SavedList
        {
            Id = l.Id,
            Name = l.Name,
            Order = l.Order,
            Members = l.Members.Select(m => new ListMember { Key = m.Key, Name = m.Name, AddedAt = m.AddedAt }).ToList(),
        }).ToList(),
        Tags = _tags.Tags.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Merges by world key and never deletes. Favorites keep an existing date; lists merge by id (a new id adds a list,
    /// appended after the current ones); tags union. Entries with standalone keys are skipped.
    /// </summary>
    /// <exception cref="InvalidDataException">The file's format version is newer than this build understands.</exception>
    public ImportResult Import(LibraryExport data)
    {
        if (data.FormatVersion > LibraryExport.CurrentFormatVersion || data.FormatVersion < 1)
        {
            throw new InvalidDataException($"Unsupported export format version {data.FormatVersion}.");
        }

        int favorites = 0, lists = 0, members = 0, tags = 0;

        foreach (var f in data.Favorites.Where(f => DiscoveryKeys.IsSavable(f.Key) && !IsStarred(f.Key)))
        {
            _favorites.Items.Add(new FavoriteEntry { Key = f.Key, Name = Clean(f.Name), StarredAt = f.StarredAt });
            favorites++;
        }

        foreach (var incoming in data.Lists.OrderBy(l => l.Order))
        {
            var list = GetList(incoming.Id);
            if (list is null)
            {
                list = new SavedList
                {
                    Id = string.IsNullOrWhiteSpace(incoming.Id) ? Guid.NewGuid().ToString("N") : incoming.Id,
                    Name = Clean(incoming.Name),
                    Order = _lists.Lists.Count == 0 ? 0 : _lists.Lists.Max(l => l.Order) + 1,
                };
                if (list.Name.Length == 0)
                {
                    continue;
                }

                _lists.Lists.Add(list);
                lists++;
            }

            foreach (var m in incoming.Members.Where(m => DiscoveryKeys.IsSavable(m.Key) && !list.Members.Any(x => KeyEquals(x.Key, m.Key))))
            {
                list.Members.Add(new ListMember { Key = m.Key, Name = Clean(m.Name), AddedAt = m.AddedAt });
                members++;
            }
        }

        foreach (var (key, incomingTags) in data.Tags.Where(p => DiscoveryKeys.IsSavable(p.Key)))
        {
            foreach (var tag in incomingTags.Select(NormalizeTag).Where(t => t.Length > 0))
            {
                if (!_tags.Tags.TryGetValue(key, out var existing))
                {
                    existing = [];
                    _tags.Tags[key] = existing;
                }

                if (!existing.Contains(tag, StringComparer.Ordinal) && existing.Count < MaxTagsPerWorld)
                {
                    existing.Add(tag);
                    tags++;
                }
            }
        }

        if (favorites > 0) Changed?.Invoke(LibraryFile.Favorites);
        if (lists > 0 || members > 0) Changed?.Invoke(LibraryFile.Lists);
        if (tags > 0) Changed?.Invoke(LibraryFile.Tags);
        return new ImportResult(favorites, lists, members, tags);
    }

    private static bool KeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTag(string tag)
    {
        tag = (tag ?? "").Trim().ToLowerInvariant();
        return tag.Length > MaxTagLength ? tag[..MaxTagLength] : tag;
    }

    private static string Clean(string? name)
    {
        name = (name ?? "").Trim();
        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }
}
