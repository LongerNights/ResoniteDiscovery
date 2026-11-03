using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Core.Persistence;

/// <summary>
/// Turns the user's library into lens contents (spec "Lens definitions": Favorites, named lists, Recent). Worlds that are
/// not live and have no hydrated record yet still appear: from their saved name and key (<see cref="RecordKeys"/>).
/// </summary>
public static class LibraryViews
{
    public static IEnumerable<(string Key, string Name, DateTime AddedAt)> FavoriteMembers(UserLibrary library) =>
        library.Favorites.Select(f => (f.Key, f.Name, f.StarredAt));

    public static IEnumerable<(string Key, string Name, DateTime AddedAt)> ListMembers(UserLibrary library, string listId) =>
        library.GetList(listId)?.Members.Select(m => (m.Key, m.Name, m.AddedAt)) ?? [];

    public static IEnumerable<(string Key, string Name, DateTime OpenedAt)> RecentMembers(UserLibrary library) =>
        library.Recent.Select(r => (r.Key, r.Name, r.OpenedAt));

    public static IReadOnlyList<ListInfo> ListInfos(UserLibrary library) =>
        library.Lists.Select(l => new ListInfo(l.Id, l.Name, l.Members.Count)).ToList();

    /// <summary>
    /// The entry to show for a saved item: the live or hydrated entry when there is one, else a placeholder carrying the
    /// saved name and the owner and record ids parsed from the key, so the card can still start a new session.
    /// </summary>
    public static DiscoveryEntry EntryFor(SavedItem item)
    {
        if (item.Entry is not null)
        {
            return item.Entry;
        }

        // A saved standalone session has no record: it shows from its saved name and cannot start a new session.
        var owner = "";
        var record = "";
        if (DiscoveryKeys.IsWorldKey(item.Key))
        {
            RecordKeys.TryParse(item.Key, out owner, out record);
        }

        return new DiscoveryEntry
        {
            Key = item.Key,
            Record = new RecordSnapshot { Key = item.Key, OwnerId = owner, RecordId = record, Name = item.Name },
        };
    }

    /// <summary>Applies the user's lists and personal tags to an entry, so search, chips and cards see them.</summary>
    public static DiscoveryEntry Decorate(DiscoveryEntry entry, UserLibrary? library) =>
        library is null || !entry.CanSave
            ? entry
            : entry with { Lists = library.ListIdsContaining(entry.Key), PersonalTags = library.TagsFor(entry.Key) };

    /// <summary>
    /// Orders saved items for a local lens. The default (date added, newest first) is the lens order from the spec:
    /// live worlds first, then newest saved. Name sorts by saved name; the direction flips either.
    /// </summary>
    public static IReadOnlyList<SavedItem> Order(IReadOnlyList<SavedItem> items, SortKey sort, bool descending)
    {
        if (sort == SortKey.Name)
        {
            return (descending
                ? items.OrderByDescending(i => i.Entry?.DisplayName ?? i.Name, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(i => i.Entry?.DisplayName ?? i.Name, StringComparer.OrdinalIgnoreCase))
                .ThenBy(i => i.Key, StringComparer.Ordinal)
                .ToList();
        }

        // DateAdded: the incoming order already puts live worlds first and the newest first; "low to high" reverses dates.
        return descending
            ? items
            : items.OrderBy(i => i.Date).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
    }
}

/// <summary>Record keys are <c>OwnerId:RecordId</c> (SkyFrost.Base.RecordId.ToString, verification).</summary>
public static class RecordKeys
{
    public static bool TryParse(string key, out string ownerId, out string recordId)
    {
        ownerId = "";
        recordId = "";
        var split = key.IndexOf(':');
        if (split <= 0 || split == key.Length - 1)
        {
            return false;
        }

        ownerId = key[..split];
        recordId = key[(split + 1)..];
        return true;
    }
}
