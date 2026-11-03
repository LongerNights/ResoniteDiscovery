namespace Discover.Core.Search;

/// <summary>
/// Tag prediction for the search bar and the organize panel: ranks the tags already in use (on worlds, sessions and the
/// user's own tags) against what has been typed. Prefix matches come first, then substring matches; busier tags first.
/// </summary>
public static class TagSuggester
{
    public const int DefaultMax = 5;

    /// <param name="prefix">What the user typed. Blank returns the most-used tags.</param>
    /// <param name="tags">Every tag occurrence (repeats count as popularity).</param>
    /// <param name="exclude">Tags to leave out (already applied).</param>
    public static IReadOnlyList<string> Suggest(string? prefix, IEnumerable<string> tags, IEnumerable<string>? exclude = null, int max = DefaultMax)
    {
        prefix = (prefix ?? "").Trim();
        var skip = new HashSet<string>(exclude ?? [], StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, (string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            var tag = raw?.Trim();
            if (string.IsNullOrEmpty(tag) || skip.Contains(tag))
            {
                continue;
            }

            counts[tag] = counts.TryGetValue(tag, out var seen) ? (seen.Name, seen.Count + 1) : (tag, 1);
        }

        return counts.Values
            .Select(c => (c.Name, c.Count, Rank: Rank(c.Name, prefix)))
            .Where(c => c.Rank >= 0)
            .OrderBy(c => c.Rank)
            .ThenByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(c => c.Name)
            .ToList();
    }

    private static int Rank(string tag, string prefix)
    {
        if (prefix.Length == 0 || tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return tag.Equals(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        return tag.Contains(prefix, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }

    /// <summary>
    /// The word being typed at the end of a search box, for tag completion: the leading + or -, and the text after any
    /// <c>tag:</c>. Null when the box ends in a space or in a quote, where there is nothing to complete.
    /// </summary>
    public static SearchWord? LastWord(string? text)
    {
        text ??= "";
        if (text.Length == 0 || char.IsWhiteSpace(text[^1]) || text.Count(c => c == '"') % 2 == 1)
        {
            return null;
        }

        var start = text.LastIndexOfAny([' ', '\t', '\n']) + 1;
        var word = text[start..];
        if (word.Contains('"'))
        {
            return null;
        }

        var sign = word.Length > 0 && word[0] is '+' or '-' ? word[0].ToString() : "";
        word = word[sign.Length..];
        if (word.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            word = word[4..];
        }

        return new SearchWord(text[..start], sign, word);
    }

    /// <summary>The search text with its last word replaced by <c>tag:name</c> (keeping +/-), ready for the next word.</summary>
    public static string Complete(SearchWord word, string tag) => word.Head + word.Sign + "tag:" + tag + " ";
}

/// <summary>The unfinished last word of a search box: everything before it, its +/- sign, and what is typed so far.</summary>
public readonly record struct SearchWord(string Head, string Sign, string Typed);
