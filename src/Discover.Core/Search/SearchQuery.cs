using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Core.Search;

/// <summary>
/// One search term. Tag terms (<c>tag:name</c>) match a tag exactly; author terms (<c>author:name</c>) match the world's
/// owner or host name as a substring; plain terms match as a substring.
/// </summary>
public sealed record SearchTerm(string Text, bool IsTag, bool IsAuthor = false);

/// <summary>
/// The spec's search syntax (F4): plain terms are optional (at least one must match), <c>+term</c> is required,
/// <c>-term</c> is excluded, <c>+tag:name</c> / <c>-tag:name</c> / <c>tag:name</c> are tag terms, and a term may be a
/// "quoted phrase". Same meaning as the engine's <c>SkyFrost.Base.SearchQueryParser</c>, but engine-free.
/// Input is capped at <see cref="MaxLength"/> characters and <see cref="MaxTerms"/> terms.
/// </summary>
public sealed class SearchQuery
{
    public const int MaxLength = 512;
    public const int MaxTerms = 24;
    private const string TagPrefix = "tag:";
    private const string AuthorPrefix = "author:";

    public static SearchQuery Empty { get; } = new([], [], []);

    public IReadOnlyList<SearchTerm> Optional { get; }
    public IReadOnlyList<SearchTerm> Required { get; }
    public IReadOnlyList<SearchTerm> Excluded { get; }

    private SearchQuery(IReadOnlyList<SearchTerm> optional, IReadOnlyList<SearchTerm> required, IReadOnlyList<SearchTerm> excluded)
    {
        Optional = optional;
        Required = required;
        Excluded = excluded;
    }

    /// <summary>The names in <c>author:</c> terms that are not excluded, each once; the controller resolves them to user ids.</summary>
    public IEnumerable<string> AuthorNames => Optional.Concat(Required)
        .Where(t => t.IsAuthor)
        .Select(t => t.Text)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Optional.Count == 0 && Required.Count == 0 && Excluded.Count == 0;

    public static SearchQuery Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Empty;
        }

        if (input.Length > MaxLength)
        {
            input = input[..MaxLength];
        }

        var optional = new List<SearchTerm>();
        var required = new List<SearchTerm>();
        var excluded = new List<SearchTerm>();
        var pos = 0;

        while (pos < input.Length && optional.Count + required.Count + excluded.Count < MaxTerms)
        {
            while (pos < input.Length && char.IsWhiteSpace(input[pos]))
            {
                pos++;
            }

            if (pos >= input.Length)
            {
                break;
            }

            var target = optional;
            if (input[pos] == '+')
            {
                target = required;
                pos++;
            }
            else if (input[pos] == '-')
            {
                target = excluded;
                pos++;
            }

            var isTag = false;
            var isAuthor = false;
            if (input.AsSpan(pos).StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                isTag = true;
                pos += TagPrefix.Length;
            }
            else if (input.AsSpan(pos).StartsWith(AuthorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                isAuthor = true;
                pos += AuthorPrefix.Length;
            }

            string text;
            if (pos < input.Length && input[pos] == '"')
            {
                var close = input.IndexOf('"', pos + 1);
                var end = close < 0 ? input.Length : close;
                text = input[(pos + 1)..end];
                pos = close < 0 ? input.Length : close + 1;
            }
            else
            {
                var start = pos;
                while (pos < input.Length && !char.IsWhiteSpace(input[pos]))
                {
                    pos++;
                }

                text = input[start..pos];
            }

            text = text.Trim();
            if (text.Length > 0)
            {
                target.Add(new SearchTerm(text, isTag, isAuthor));
            }
        }

        return new SearchQuery(optional, required, excluded);
    }

    /// <summary>
    /// True when the entry satisfies every required term, no excluded term, and at least one optional term
    /// (when there are any). Matches against name, description, record tags, session tags, author/host and the
    /// user's personal tags (spec F4, F22).
    /// </summary>
    public bool Matches(DiscoveryEntry entry)
    {
        foreach (var term in Excluded)
        {
            if (TermMatches(entry, term))
            {
                return false;
            }
        }

        foreach (var term in Required)
        {
            if (!TermMatches(entry, term))
            {
                return false;
            }
        }

        return Optional.Count == 0 || Optional.Any(term => TermMatches(entry, term));
    }

    public IReadOnlyList<DiscoveryEntry> Filter(IEnumerable<DiscoveryEntry> entries) =>
        IsEmpty ? entries.ToList() : entries.Where(Matches).ToList();

    /// <summary>
    /// Sends tag terms to the server as tag lists (spec "Search term syntax"). Tags the user holds as personal tags
    /// never go to the server, since it cannot know them; those still filter locally through <see cref="Matches"/>.
    /// Plain terms are never sent: they are filtered client-side on the returned page.
    /// </summary>
    public RecordQuery ApplyTo(RecordQuery query, Func<string, bool>? isPersonalTag = null)
    {
        IReadOnlyList<string> Tags(IReadOnlyList<SearchTerm> terms, IReadOnlyList<string> existing) => existing
            .Concat(terms.Where(t => t.IsTag && !(isPersonalTag?.Invoke(t.Text) ?? false)).Select(t => t.Text))
            .ToList();

        return query with
        {
            OptionalTags = Tags(Optional, query.OptionalTags),
            RequiredTags = Tags(Required, query.RequiredTags),
            ExcludedTags = Tags(Excluded, query.ExcludedTags),
        };
    }

    private static bool TermMatches(DiscoveryEntry entry, SearchTerm term)
    {
        if (term.IsAuthor)
        {
            // Client-side only (V30 is open: no engine name lookup yet), so it narrows the loaded results by owner or host name.
            return Contains(entry.Record?.OwnerName, term.Text)
                || entry.Sessions.Any(s => Contains(s.HostUsername, term.Text));
        }

        if (term.IsTag)
        {
            return AnyTagEquals(entry.Record?.Tags, term.Text)
                || AnyTagEquals(entry.PersonalTags, term.Text)
                || entry.Sessions.Any(s => AnyTagEquals(s.Tags, term.Text));
        }

        return Contains(entry.DisplayName, term.Text)
            || Contains(entry.Record?.Name, term.Text)
            || Contains(entry.Record?.Description, term.Text)
            || Contains(entry.Record?.OwnerName, term.Text)
            || AnyTagContains(entry.Record?.Tags, term.Text)
            || AnyTagContains(entry.PersonalTags, term.Text)
            || entry.Sessions.Any(s => Contains(s.Name, term.Text)
                || Contains(s.Description, term.Text)
                || Contains(s.HostUsername, term.Text)
                || AnyTagContains(s.Tags, term.Text));
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool AnyTagContains(IReadOnlyList<string>? tags, string term) =>
        tags is not null && tags.Any(t => Contains(t, term));

    private static bool AnyTagEquals(IReadOnlyList<string>? tags, string term) =>
        tags is not null && tags.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase));
}
