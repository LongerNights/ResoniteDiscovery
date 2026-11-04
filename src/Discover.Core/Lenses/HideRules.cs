using Discover.Core.Model;
using Discover.Core.Settings;

namespace Discover.Core.Lenses;

/// <summary>
/// Hide lists (spec F19): worlds, authors or hosts, and words the user never wants to see in the live, search and
/// insight lenses. Favorites, lists, Recent, My Worlds and Current Worlds are the user's own picks and are never hidden.
/// </summary>
public enum HiddenKind
{
    Author,
    World,
    Word,
}

/// <summary>One entry of the hidden list in Settings.</summary>
public sealed record HiddenItem(HiddenKind Kind, string Id, string Name);

public static class HideRules
{
    public const int MaxWords = 50;
    public const int MaxWordLength = 40;

    public static bool IsEmpty(DiscoverSettings settings) =>
        settings.HiddenWorlds.Count == 0 && settings.HiddenAuthors.Count == 0 && settings.HiddenWords.Count == 0;

    public static bool IsHidden(DiscoveryEntry entry, DiscoverSettings settings)
    {
        if (IsEmpty(settings))
        {
            return false;
        }

        if (settings.HiddenWorlds.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (settings.HiddenAuthors.Count > 0)
        {
            if (entry.Record is { OwnerId.Length: > 0 } record && settings.HiddenAuthors.Contains(record.OwnerId, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry.Sessions.Any(s => s.HostUserId.Length > 0 && settings.HiddenAuthors.Contains(s.HostUserId, StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        foreach (var word in settings.HiddenWords)
        {
            if (Has(entry.DisplayName, word) || Has(entry.Record?.Name, word) || entry.Sessions.Any(s => Has(s.Name, word)))
            {
                return true;
            }
        }

        return false;
    }

    public static DiscoverSettings HideWorld(DiscoverSettings settings, string key, string? name = null) =>
        settings.HiddenWorlds.Contains(key, StringComparer.OrdinalIgnoreCase)
            ? settings
            : settings with { HiddenWorlds = [.. settings.HiddenWorlds, key], HiddenNames = WithName(settings, key, name) };

    public static DiscoverSettings HideAuthor(DiscoverSettings settings, string authorId, string? name = null) =>
        authorId.Length == 0 || settings.HiddenAuthors.Contains(authorId, StringComparer.OrdinalIgnoreCase)
            ? settings
            : settings with { HiddenAuthors = [.. settings.HiddenAuthors, authorId], HiddenNames = WithName(settings, authorId, name) };

    private static IReadOnlyDictionary<string, string> WithName(DiscoverSettings settings, string id, string? name)
    {
        var names = new Dictionary<string, string>(settings.HiddenNames, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(name))
        {
            names[id] = name;
        }

        return names;
    }

    /// <summary>What Settings lists: authors, then worlds, then words, each with its name (the id when none was kept).</summary>
    public static IReadOnlyList<HiddenItem> Items(DiscoverSettings settings)
    {
        string NameOf(string id) => settings.HiddenNames.TryGetValue(id, out var name) && name.Length > 0 ? name : id;
        return
        [
            .. settings.HiddenAuthors.Select(id => new HiddenItem(HiddenKind.Author, id, NameOf(id))),
            .. settings.HiddenWorlds.Select(id => new HiddenItem(HiddenKind.World, id, NameOf(id))),
            .. settings.HiddenWords.Select(word => new HiddenItem(HiddenKind.Word, word, word)),
        ];
    }

    /// <summary>Takes one hidden thing off the list again.</summary>
    public static DiscoverSettings Restore(DiscoverSettings settings, HiddenKind kind, string id)
    {
        bool Not(string value) => !string.Equals(value, id, StringComparison.OrdinalIgnoreCase);
        var names = settings.HiddenNames.Where(pair => Not(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return kind switch
        {
            HiddenKind.Author => settings with { HiddenAuthors = settings.HiddenAuthors.Where(Not).ToList(), HiddenNames = names },
            HiddenKind.World => settings with { HiddenWorlds = settings.HiddenWorlds.Where(Not).ToList(), HiddenNames = names },
            _ => settings with { HiddenWords = settings.HiddenWords.Where(Not).ToList() },
        };
    }

    public static DiscoverSettings HideWord(DiscoverSettings settings, string word)
    {
        word = word.Trim();
        if (word.Length == 0 || settings.HiddenWords.Count >= MaxWords || settings.HiddenWords.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            return settings;
        }

        if (word.Length > MaxWordLength)
        {
            word = word[..MaxWordLength];
        }

        return settings with { HiddenWords = [.. settings.HiddenWords, word] };
    }

    public static DiscoverSettings RestoreAll(DiscoverSettings settings) =>
        settings with { HiddenWorlds = [], HiddenAuthors = [], HiddenWords = [], HiddenNames = new Dictionary<string, string>() };

    /// <summary>The author to hide for an entry: the publisher, else a session host. Empty when there is none.</summary>
    public static string AuthorOf(DiscoveryEntry entry) =>
        entry.Record is { OwnerId.Length: > 0 } record ? record.OwnerId : entry.Sessions.Select(s => s.HostUserId).FirstOrDefault(id => id.Length > 0) ?? "";

    private static bool Has(string? value, string word) => value is not null && value.Contains(word, StringComparison.OrdinalIgnoreCase);
}
