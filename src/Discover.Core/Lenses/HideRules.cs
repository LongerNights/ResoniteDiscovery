using Discover.Core.Model;
using Discover.Core.Settings;

namespace Discover.Core.Lenses;

/// <summary>
/// Hide lists (spec F19): worlds, authors or hosts, and words the user never wants to see in the live, search and
/// insight lenses. Favorites, lists, Recent, My Worlds and Current Worlds are the user's own picks and are never hidden.
/// </summary>
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

    public static DiscoverSettings HideWorld(DiscoverSettings settings, string key) =>
        settings.HiddenWorlds.Contains(key, StringComparer.OrdinalIgnoreCase)
            ? settings
            : settings with { HiddenWorlds = [.. settings.HiddenWorlds, key] };

    public static DiscoverSettings HideAuthor(DiscoverSettings settings, string authorId) =>
        authorId.Length == 0 || settings.HiddenAuthors.Contains(authorId, StringComparer.OrdinalIgnoreCase)
            ? settings
            : settings with { HiddenAuthors = [.. settings.HiddenAuthors, authorId] };

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
        settings with { HiddenWorlds = [], HiddenAuthors = [], HiddenWords = [] };

    /// <summary>The author to hide for an entry: the publisher, else a session host. Empty when there is none.</summary>
    public static string AuthorOf(DiscoveryEntry entry) =>
        entry.Record is { OwnerId.Length: > 0 } record ? record.OwnerId : entry.Sessions.Select(s => s.HostUserId).FirstOrDefault(id => id.Length > 0) ?? "";

    private static bool Has(string? value, string word) => value is not null && value.Contains(word, StringComparison.OrdinalIgnoreCase);
}
