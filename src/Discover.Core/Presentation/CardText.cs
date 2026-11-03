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

        // rail and lenses
        ["rail.live"] = "LIVE",
        ["rail.browse"] = "BROWSE",
        ["rail.mine"] = "MINE",
        ["rail.newList"] = "+ New list",
        ["lens.liveNow"] = "Live Now",
        ["lens.friends"] = "Friends",
        ["lens.new"] = "New",
        ["lens.mostVisited"] = "Most Visited",
        ["lens.featured"] = "Featured",
        ["lens.random"] = "Random",
        ["lens.favorites"] = "Favorites",
        ["lens.myWorlds"] = "My Worlds",
        ["lens.groupWorlds"] = "Group Worlds",
        ["lens.myWorlds.sub"] = "Worlds you published",
        ["lens.groupWorlds.sub"] = "Worlds published by a group you are in",
        ["empty.myWorlds"] = "You have not published any worlds yet.",
        ["empty.groupWorlds"] = "This group has no published worlds.",
        ["notice.noGroups"] = "You are not in any groups.",
        ["notice.moreGroups"] = "Showing the first {0} of your groups.",
        ["confirm.groupTitle"] = "Choose a group",
        ["confirm.groupBody"] = "Whose worlds do you want to see?",
        ["lens.recent"] = "Recent",
        ["lens.list"] = "List",
        ["lens.liveNow.sub"] = "Public sessions with people in them",
        ["lens.friends.sub"] = "Sessions where a contact is present",
        ["lens.new.sub"] = "Newly published worlds",
        ["lens.mostVisited.sub"] = "Published worlds, most visits first",
        ["lens.featured.sub"] = "Picked by the Resonite team",
        ["lens.random.sub"] = "A shuffle of published worlds",
        ["lens.favorites.sub"] = "Worlds you starred",
        ["lens.recent.sub"] = "Worlds you opened from here, newest first",
        ["lens.list.sub"] = "Your list",

        // top bar
        ["search.placeholder"] = "Search worlds or tags...",
        ["bar.sort"] = "Sort",
        ["bar.filters"] = "Filters",
        ["bar.refresh"] = "Refresh",
        ["bar.settings"] = "Settings",
        ["card.info"] = "Info",
        ["detail.noSessions"] = "Not live right now",
        ["detail.noDescription"] = "No description",
        ["detail.noTags"] = "No tags",
        ["detail.session"] = "{0}  ·  {1}  ·  {2}",
        ["detail.devices"] = "In sessions now: {0} VR, {1} desktop, {2} camera",
        ["detail.sessions"] = "Sessions",
        ["detail.tags"] = "Tags",
        ["detail.close"] = "Close",
        ["settings.title"] = "Settings",
        ["settings.animations"] = "Animations",
        ["settings.liveOnly"] = "Live lenses only",
        ["settings.sidebar"] = "Sidebar sections and lenses",
        ["settings.general"] = "General",
        ["settings.yourLists"] = "Your lists",
        ["notice.keepOne"] = "At least one lens has to stay on.",
        ["settings.startOn"] = "Start on",
        ["settings.on"] = "On",
        ["settings.off"] = "Off",
        ["settings.data"] = "Your data",
        ["settings.export"] = "Export",
        ["settings.import"] = "Import",
        ["settings.note"] = "Export writes galaxy-sieve-export.json to the exports folder in your Galaxy Sieve data folder. Import reads that file and only adds to what you have.",
        ["notice.exported"] = "Exported your favorites, lists and tags.",
        ["notice.noExport"] = "No export file found in the exports folder.",
        ["notice.imported"] = "Imported {0} favorites, {1} lists, {2} tags.",
        ["notice.signIn"] = "Sign in to use this.",
        ["chips.clear"] = "Clear all",

        // sort menu
        ["sort.title"] = "SORT BY",
        ["sort.LiveUsers"] = "Live users",
        ["sort.FriendsPresent"] = "Friends present",
        ["sort.NewestSession"] = "Newest session",
        ["sort.DateAdded"] = "Date added",
        ["sort.Visits"] = "Visits",
        ["sort.NewestPublished"] = "Newest published",
        ["sort.RecentlyUpdated"] = "Recently updated",
        ["sort.Name"] = "Name",
        ["sort.Random"] = "Random",
        ["sort.LiveUsers.hint"] = "People in the busiest session",
        ["sort.FriendsPresent.hint"] = "Contacts in the world",
        ["sort.NewestSession.hint"] = "Most recently started",
        ["sort.DateAdded.hint"] = "Newest first",
        ["sort.Visits.hint"] = "Total visits",
        ["sort.NewestPublished.hint"] = "First published",
        ["sort.RecentlyUpdated.hint"] = "Last changed",
        ["sort.Name.hint"] = "A to Z",
        ["sort.Random.hint"] = "Shuffle",
        ["sort.unavailable"] = "Not for this lens",
        ["sort.descending"] = "High to low",
        ["sort.ascending"] = "Low to high",

        // filters popover
        ["filters.title"] = "Filters",
        ["filters.reset"] = "Reset",
        ["filters.done"] = "Done",
        ["filters.minUsers"] = "At least {0} people",
        ["filters.minUsers.any"] = "Any number of people",
        ["filters.minVisits"] = "At least {0} visits",
        ["filters.minVisits.any"] = "Any number of visits",
        ["filters.hasRoom"] = "Has room",
        ["filters.mobile"] = "Mobile friendly",
        ["filters.vr"] = "Has VR users",
        ["filters.otherVersions"] = "Show other versions",
        ["filters.host"] = "Hosted by",
        ["filters.host.any"] = "Anyone",
        ["filters.host.person"] = "A person",
        ["filters.host.headless"] = "Headless",
        ["filters.on"] = "On",
        ["filters.off"] = "Off",

        // cards
        ["card.join"] = "Join",
        ["card.goThere"] = "Go there",
        ["card.newSession"] = "New session",
        ["card.full"] = "Full",
        ["card.offline"] = "Offline",
        ["card.otherVersion"] = "Other version",
        ["card.open"] = "You're here",
        ["card.mobile"] = "Mobile",
        ["card.friend"] = "1 friend",
        ["card.friends"] = "{0} friends",
        ["card.sessions"] = "+{0} sessions",
        ["card.users"] = "{0} users",
        ["card.visits"] = "{0} visits",
        ["card.by"] = "by {0}",
        ["card.featured"] = "Featured",
        ["card.unnamed"] = "Unnamed",

        // favorites, lists, tags
        ["card.star"] = "Favorite",
        ["card.organize"] = "Lists",
        ["empty.favorites"] = "No favorites yet. Press the star on a world to add it.",
        ["empty.list"] = "This list is empty. Use Lists on a world's card to add it.",
        ["empty.recent"] = "Worlds you open from here will show up here.",
        ["list.rename"] = "Rename",
        ["list.delete"] = "Delete",
        ["list.moveUp"] = "Move up",
        ["list.moveDown"] = "Move down",
        ["prompt.newListTitle"] = "New list",
        ["prompt.renameTitle"] = "Rename list",
        ["prompt.listPlaceholder"] = "List name",
        ["prompt.tagPlaceholder"] = "Type a tag and press Enter",
        ["organize.suggested"] = "Suggested",
        ["prompt.ok"] = "OK",
        ["prompt.cancel"] = "Cancel",
        ["organize.title"] = "Lists and tags",
        ["organize.lists"] = "LISTS",
        ["organize.tags"] = "YOUR TAGS",
        ["organize.noLists"] = "No lists yet.",
        ["organize.noTags"] = "No tags yet.",
        ["organize.add"] = "Add",
        ["organize.done"] = "Done",
        ["confirm.deleteListTitle"] = "Delete list",
        ["confirm.deleteListBody"] = "Delete the list {0}? The worlds in it are not affected.",
        ["confirm.delete"] = "Delete",
        ["notice.fileReset"] = "A saved Galaxy Sieve file could not be read. It was kept as .bad and reset.",
        ["notice.tagsFull"] = "That world already has the maximum number of tags.",
        ["empty.noAccount"] = "Sign in to save worlds, lists and tags.",

        // join confirmation (the engine's own choice dialog)
        ["confirm.joinTitle"] = "Join session",
        ["confirm.joinBody"] = "Join {0}?",
        ["confirm.newTitle"] = "Start a new session",
        ["confirm.newBody"] = "Start a new session of {0}?",
        ["confirm.cancel"] = "Cancel",
        ["confirm.join"] = "Join",
        ["confirm.start"] = "Start",
        ["confirm.sessionRow"] = "{0} · {1} · {2} users",

        // status bar and states
        ["status.summary"] = "{0} worlds · {1} people",
        ["status.worlds"] = "{0} worlds on this page",
        ["status.worldsOnly"] = "{0} worlds",
        ["status.page"] = "Page {0}",
        ["status.updated"] = "Updated {0} ago",
        ["status.updatedNow"] = "Updated just now",
        ["status.pageOf"] = "Page {0} of {1}",
        ["status.pageUpdating"] = "Page {0} · Updating…",
        ["status.updating"] = "Updating…",
        ["status.paused"] = "Galaxy Sieve paused after repeated errors",
        ["nav.previous"] = "Previous",
        ["nav.next"] = "Next",
        ["banner.searchFailed"] = "Couldn't reach world search ({0}). Showing the last results. Next try in {1} s.",
        ["banner.retry"] = "Retry",
        ["empty.liveNow"] = "Nobody is online right now.",
        ["empty.friends"] = "None of your contacts are in a joinable session.",
        ["empty.noMatch"] = "No worlds match",
        ["empty.noMatchHint"] = "Try fewer filters or a different search.",
        ["empty.clearFilters"] = "Clear filters",
        ["empty.searchDisabled"] = "Search is turned off (live lenses only).",
        ["empty.loading"] = "Loading…",

        // errors
        ["error.joinEnded"] = "That session has ended.",
        ["error.joinFailed"] = "Could not join: {0}",
        ["error.joinFull"] = "That session is full.",
    };

    public static string Get(string id) => Table.TryGetValue(id, out var text) ? text : id;

    public static string Format(string id, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(id), args);

    public static IReadOnlyCollection<string> Ids => Table.Keys;
}
