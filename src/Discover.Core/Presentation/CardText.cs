using System.Globalization;

namespace Discover.Core.Presentation;

/// <summary>Engine-free text for cards (spec F6): ages, counts and the string table.</summary>
public static class CardText
{
    /// <summary>"now", "5 m", "3 h", "3 d", "2 mo", "1 y". Negative spans read as "now".</summary>
    public static string Age(DateTime then, DateTime now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} m";
        }

        if (span < TimeSpan.FromDays(1))
        {
            return $"{(int)span.TotalHours} h";
        }

        if (span < TimeSpan.FromDays(60))
        {
            return $"{(int)span.TotalDays} d";
        }

        if (span < TimeSpan.FromDays(730))
        {
            return $"{(int)(span.TotalDays / 30)} mo";
        }

        return $"{(int)(span.TotalDays / 365)} y";
    }

    /// <summary>"950", "12.4k", "3.2M". Invariant culture, one decimal, trailing ".0" dropped.</summary>
    public static string Compact(long value)
    {
        if (value < 0)
        {
            return "0";
        }

        if (value < 1000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var (scaled, suffix) = value < 1_000_000 ? (value / 1000.0, "k") : (value / 1_000_000.0, "M");
        var text = (Math.Floor(scaled * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);
        return text + suffix;
    }

    /// <summary>
    /// Cuts <paramref name="value"/> to at most <paramref name="maxChars"/> characters, ending in "…" when cut. UIX text
    /// has no overflow ellipsis, so cards truncate here to keep one line at a steady font size.
    /// Never splits a surrogate pair; trailing spaces before the ellipsis are dropped.
    /// </summary>
    public static string Ellipsize(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0)
        {
            return "";
        }

        value = value.Trim();
        if (value.Length <= maxChars)
        {
            return value;
        }

        var cut = maxChars - 1;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut].TrimEnd() + "…";
    }

    /// <summary>"5/10", or just "5" when the maximum is unknown.</summary>
    public static string Users(int active, int maximum) =>
        maximum > 0 ? $"{active}/{maximum}" : active.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The one string table (CLAUDE.md conventions), keyed by id so it can be localized later.
/// Labels are kept to two words where possible.
/// </summary>
public static class UiStrings
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        ["screen.title"] = "Galaxy Sieve",
        ["lens.liveNow"] = "Live Now",
        ["lens.friends"] = "Friends",
        ["rail.live"] = "LIVE",
        ["card.join"] = "Join",
        ["card.focus"] = "Open",
        ["card.full"] = "Full",
        ["card.open"] = "Open",
        ["card.mobile"] = "Mobile",
        ["card.friend"] = "1 friend",
        ["card.friends"] = "{0} friends",
        ["card.sessions"] = "+{0} sessions",
        ["card.users"] = "{0} users",
        ["card.unnamed"] = "Unnamed",
        ["card.vr"] = "VR {0}",
        ["card.screen"] = "Screen {0}",
        ["status.summary"] = "{0} worlds · {1} people",
        ["status.truncated"] = "first {0} shown",
        ["status.paused"] = "Galaxy Sieve paused after repeated errors",
        ["empty.liveNow"] = "Nobody is online right now.",
        ["empty.friends"] = "None of your contacts are in a joinable session.",
        ["error.joinEnded"] = "That session has ended.",
        ["error.joinFailed"] = "Could not join: {0}",
        ["error.joinFull"] = "That session is full.",
    };

    public static string Get(string id) => Table.TryGetValue(id, out var text) ? text : id;

    public static string Format(string id, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(id), args);

    public static IReadOnlyCollection<string> Ids => Table.Keys;
}
