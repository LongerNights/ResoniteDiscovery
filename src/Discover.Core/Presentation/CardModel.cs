using Discover.Core.Settings;
using Discover.Core.Lenses;
using Discover.Core.Model;

namespace Discover.Core.Presentation;

public enum CardAction
{
    /// <summary>Join a live session (asks for confirmation first).</summary>
    Join,

    /// <summary>A world the user already has open: focus it.</summary>
    GoThere,

    /// <summary>A published world with no live session: start a new one (asks for confirmation first).</summary>
    NewSession,

    /// <summary>Every joinable session is full.</summary>
    Full,

    /// <summary>A saved session that is not running and has no world to start a new one from.</summary>
    Offline,

    /// <summary>The only sessions are on another build.</summary>
    OtherVersion,
}

public enum BadgeKind
{
    /// <summary>"You are here": the focused open world.</summary>
    Here,

    /// <summary>Host or guest, and the access level, on Current Worlds cards.</summary>
    Info,

    Friends,
    Mobile,
    Full,
    OtherVersion,
}

public sealed record Badge(string Text, BadgeKind Kind);

/// <summary>A tag button on a card: the user's own tags (orange) come before the world's.</summary>
public sealed record CardTag(string Text, bool Personal);

/// <summary>Everything a card shows, as plain data (spec "Card anatomy"). The UI only binds it.</summary>
public sealed record CardModel(
    string Key,
    string Title,
    string Meta,
    string? Users,
    IReadOnlyList<Badge> Badges,
    IReadOnlyList<CardTag> Tags,
    string Footer,
    CardAction Action,
    string? ThumbnailUrl,
    bool Featured,
    bool IsOpen,
    bool Dimmed,
    bool IsFavorite = false,
    bool Visited = false,
    bool CanLeave = false)
{
    public const int MaxTitleChars = 34;
    public const int MaxMetaChars = 40;
    public const int MaxTags = 3;
    public const int MaxTagChars = 14;
}

public static class CardBuilder
{
    /// <summary>
    /// Builds a card. <paramref name="openSession"/> is the session of this entry the user already has open, if any
    /// (the engine knows; Core does not).
    /// </summary>
    public static CardModel Build(DiscoveryEntry entry, SessionSnapshot? openSession, DateTime nowUtc, bool isFavorite = false)
    {
        var best = entry.BestSession ?? entry.Sessions.FirstOrDefault();
        var isOpen = openSession is not null;
        var hasSessions = entry.Sessions.Count > 0;

        var canStart = entry.Record is { OwnerId.Length: > 0, RecordId.Length: > 0 };
        var action = isOpen ? CardAction.GoThere
            : !hasSessions && !canStart ? CardAction.Offline
            : !hasSessions ? CardAction.NewSession
            : entry.IsOtherVersionOnly ? CardAction.OtherVersion
            : entry.IsFull ? CardAction.Full
            : CardAction.Join;

        var badges = new List<Badge>(3);
        if (entry.FriendsPresent > 0)
        {
            badges.Add(new Badge(entry.FriendsPresent == 1 ? UiStrings.Get("card.friend") : UiStrings.Format("card.friends", entry.FriendsPresent), BadgeKind.Friends));
        }

        if (entry.IsMobileFriendly)
        {
            badges.Add(new Badge(UiStrings.Get("card.mobile"), BadgeKind.Mobile));
        }

        if (action == CardAction.Full)
        {
            badges.Add(new Badge(UiStrings.Get("card.full"), BadgeKind.Full));
        }

        if (action == CardAction.OtherVersion)
        {
            badges.Add(new Badge(UiStrings.Get("card.otherVersion"), BadgeKind.OtherVersion));
        }

        var name = entry.DisplayName.Length > 0 ? entry.DisplayName : UiStrings.Get("card.unnamed");
        var personal = entry.PersonalTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => new CardTag(CardText.Ellipsize(t, CardModel.MaxTagChars), true));
        var world = (entry.Record?.Tags ?? best?.Tags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new CardTag(CardText.Ellipsize(t, CardModel.MaxTagChars), false));
        var tags = personal.Concat(world)
            .DistinctBy(t => t.Text, StringComparer.OrdinalIgnoreCase)
            .Take(CardModel.MaxTags)
            .ToList();

        return new CardModel(
            entry.Key,
            CardText.Ellipsize(name, CardModel.MaxTitleChars),
            Meta(entry, best, nowUtc),
            hasSessions && best is not null && entry.LiveUsers + entry.Sessions.Where(s => !entry.PublicSessions.Contains(s)).Sum(s => s.ActiveUsers) > 0
                ? CardText.Users(best.ActiveUsers, best.MaximumUsers)
                : null,
            badges,
            tags,
            entry.Record is { Visits: > 0 } r ? UiStrings.Format("card.visits", CardText.Compact(r.Visits)) : "",
            action,
            best?.ThumbnailUrl ?? entry.ThumbnailUrl,
            entry.IsFeatured,
            isOpen,
            action == CardAction.OtherVersion,
            isFavorite);
    }

    /// <summary>
    /// A Current Worlds card: the normal card for the world's entry (its one session is the open one, so the action is Go
    /// there), with "You are here" for the focused world and the user's role and the access level as badges.
    /// </summary>
    public static CardModel BuildOpenWorld(OpenWorldItem item, DateTime nowUtc, bool isFavorite = false)
    {
        var card = Build(item.Entry, item.Entry.Sessions[0], nowUtc, isFavorite);
        var badges = new List<Badge>(card.Badges.Count + 3);
        if (item.World.IsFocused)
        {
            badges.Add(new Badge(UiStrings.Get("card.here"), BadgeKind.Here));
        }

        badges.Add(new Badge(UiStrings.Get(item.World.IsHost ? "card.host" : "card.guest"), BadgeKind.Info));
        badges.Add(new Badge(UiStrings.Get("access." + item.World.AccessLevel), BadgeKind.Info));
        badges.AddRange(card.Badges);
        return card with
        {
            Users = item.World.MaxUsers > 0 || item.World.Users > 0 ? CardText.Users(item.World.Users, item.World.MaxUsers) : null,
            Badges = badges,
            IsOpen = !item.World.IsFocused, // the focused world shows "You are here" instead of the Open pill
        };
    }

    private static string Meta(DiscoveryEntry entry, SessionSnapshot? best, DateTime nowUtc)
    {
        string meta;
        if (best is not null)
        {
            meta = $"{CardText.Ellipsize(best.HostUsername, 22)} · {CardText.Age(best.SessionBeginTime, nowUtc)}";
        }
        else if (entry.Record is { } record)
        {
            var by = string.IsNullOrWhiteSpace(record.OwnerName) ? "" : UiStrings.Format("card.by", CardText.Ellipsize(record.OwnerName, 22));
            var age = record.FirstPublishTime is { } published ? CardText.Age(published, nowUtc) : "";
            meta = string.Join(" · ", new[] { by, age }.Where(s => s.Length > 0));
        }
        else
        {
            meta = "";
        }

        if (entry.Sessions.Count > 1)
        {
            meta += " · " + UiStrings.Format("card.sessions", entry.Sessions.Count);
        }

        return meta;
    }
}

/// <summary>One row of the lens rail. <paramref name="ListId"/> is set for a named list.</summary>
public sealed record RailItem(LensKind Lens, string Label, string? Count, bool Selected, string? ListId = null);

/// <summary>A named list as the rail sees it.</summary>
public sealed record ListInfo(string Id, string Name, int Count);

public sealed record RailSection(string Label, IReadOnlyList<RailItem> Items, RailSectionId Id = RailSectionId.Now);

/// <summary>One row of the sort menu: enabled only when the lens can honour it (spec F14).</summary>
public sealed record SortItem(SortKey Key, string Label, string Hint, bool Enabled, bool Selected);

public static class RailBuilder
{
    /// <summary>
    /// The rail: Now, Discover and Yours (Favorites, each named list, Recent). The "+ New list" button is the UI's.
    /// <paramref name="selectedListId"/> picks which list is lit when <paramref name="selected"/> is a named list.
    /// </summary>
    public static IReadOnlyList<RailSection> Build(
        LensKind selected,
        int liveNowCount,
        int friendsCount,
        int favoritesCount = 0,
        IReadOnlyList<ListInfo>? lists = null,
        int recentCount = 0,
        string? selectedListId = null,
        DiscoverSettings? visibility = null,
        int currentWorldsCount = 0) => Filter(
    [
        new RailSection(UiStrings.Get("rail.now"), Id: RailSectionId.Now, Items:
        [
            new RailItem(LensKind.CurrentWorlds, UiStrings.Get("lens.currentWorlds"), currentWorldsCount > 0 ? currentWorldsCount.ToString() : null, selected == LensKind.CurrentWorlds),
            new RailItem(LensKind.LiveNow, UiStrings.Get("lens.liveNow"), liveNowCount > 0 ? liveNowCount.ToString() : null, selected == LensKind.LiveNow),
            new RailItem(LensKind.Friends, UiStrings.Get("lens.friends"), friendsCount > 0 ? friendsCount.ToString() : null, selected == LensKind.Friends),
        ]),
        new RailSection(UiStrings.Get("rail.discover"), Id: RailSectionId.Discover, Items:
        [
            new RailItem(LensKind.Browse, UiStrings.Get("lens.browse"), null, selected == LensKind.Browse),
            new RailItem(LensKind.Featured, UiStrings.Get("lens.featured"), null, selected == LensKind.Featured),
            new RailItem(LensKind.New, UiStrings.Get("lens.new"), null, selected == LensKind.New),
            new RailItem(LensKind.Random, UiStrings.Get("lens.random"), null, selected == LensKind.Random),
        ]),
        new RailSection(UiStrings.Get("rail.yours"), Id: RailSectionId.Yours, Items:
        [
            new RailItem(LensKind.Favorites, UiStrings.Get("lens.favorites"), favoritesCount > 0 ? favoritesCount.ToString() : null, selected == LensKind.Favorites),
            .. (lists ?? []).Select(l => new RailItem(LensKind.NamedList, l.Name, l.Count > 0 ? l.Count.ToString() : null, selected == LensKind.NamedList && l.Id == selectedListId, l.Id)),
            new RailItem(LensKind.Recent, UiStrings.Get("lens.recent"), recentCount > 0 ? recentCount.ToString() : null, selected == LensKind.Recent),
            new RailItem(LensKind.MyWorlds, UiStrings.Get("lens.myWorlds"), null, selected == LensKind.MyWorlds),
            new RailItem(LensKind.GroupWorlds, UiStrings.Get("lens.groupWorlds"), null, selected == LensKind.GroupWorlds),
        ]),
        new RailSection(UiStrings.Get("rail.insights"), Id: RailSectionId.Insights, Items:
        [
            new RailItem(LensKind.Trending, UiStrings.Get("lens.trending"), null, selected == LensKind.Trending),
            new RailItem(LensKind.Rising, UiStrings.Get("lens.rising"), null, selected == LensKind.Rising),
            new RailItem(LensKind.HiddenGems, UiStrings.Get("lens.hiddenGems"), null, selected == LensKind.HiddenGems),
        ]),
    ], visibility);

    /// <summary>Settings (F28): sections and lenses that are switched off are left off the rail.</summary>
    private static IReadOnlyList<RailSection> Filter(RailSection[] sections, DiscoverSettings? visibility) => visibility is null
        ? sections
        : sections
            .Where(s => visibility.IsSectionOn(s.Id))
            .Select(s => s with { Items = s.Items.Where(i => visibility.IsVisible(i.Lens)).ToList() })
            .Where(s => LensCatalog.LensesOf(s.Id).Any(visibility.IsVisible))
            .ToList();

    public static string Title(LensKind lens) => lens switch
    {
        LensKind.CurrentWorlds => UiStrings.Get("lens.currentWorlds"),
        LensKind.LiveNow => UiStrings.Get("lens.liveNow"),
        LensKind.Friends => UiStrings.Get("lens.friends"),
        LensKind.New => UiStrings.Get("lens.new"),
        LensKind.Browse => UiStrings.Get("lens.browse"),
        LensKind.Featured => UiStrings.Get("lens.featured"),
        LensKind.Random => UiStrings.Get("lens.random"),
        LensKind.Favorites => UiStrings.Get("lens.favorites"),
        LensKind.Recent => UiStrings.Get("lens.recent"),
        LensKind.NamedList => UiStrings.Get("lens.list"),
        LensKind.MyWorlds => UiStrings.Get("lens.myWorlds"),
        LensKind.GroupWorlds => UiStrings.Get("lens.groupWorlds"),
        LensKind.Trending => UiStrings.Get("lens.trending"),
        LensKind.Rising => UiStrings.Get("lens.rising"),
        LensKind.HiddenGems => UiStrings.Get("lens.hiddenGems"),
        _ => lens.ToString(),
    };

    public static string Subtitle(LensKind lens) => lens switch
    {
        LensKind.CurrentWorlds => UiStrings.Get("lens.currentWorlds.sub"),
        LensKind.LiveNow => UiStrings.Get("lens.liveNow.sub"),
        LensKind.Friends => UiStrings.Get("lens.friends.sub"),
        LensKind.New => UiStrings.Get("lens.new.sub"),
        LensKind.Browse => UiStrings.Get("lens.browse.sub"),
        LensKind.Featured => UiStrings.Get("lens.featured.sub"),
        LensKind.Random => UiStrings.Get("lens.random.sub"),
        LensKind.Favorites => UiStrings.Get("lens.favorites.sub"),
        LensKind.Recent => UiStrings.Get("lens.recent.sub"),
        LensKind.NamedList => UiStrings.Get("lens.list.sub"),
        LensKind.MyWorlds => UiStrings.Get("lens.myWorlds.sub"),
        LensKind.GroupWorlds => UiStrings.Get("lens.groupWorlds.sub"),
        LensKind.Trending => UiStrings.Get("lens.trending.sub"),
        LensKind.Rising => UiStrings.Get("lens.rising.sub"),
        LensKind.HiddenGems => UiStrings.Get("lens.hiddenGems.sub"),
        _ => "",
    };

    public static IReadOnlyList<SortItem> SortMenu(LensKind lens, SortKey selected) => Sorting.All
        .Select(key => new SortItem(
            key,
            UiStrings.Get("sort." + key),
            UiStrings.Get(Sorting.IsSupported(lens, key) ? "sort." + key + ".hint" : "sort.unavailable"),
            Sorting.IsSupported(lens, key),
            key == selected))
        .ToList();

    public static string SortLabel(SortKey key) => UiStrings.Get("sort." + key);

    /// <summary>"12 worlds · 56 people" or, for search lenses, "50 worlds on this page".</summary>
    public static string Count(LensKind lens, int worlds, int people) => LensEngine.SourceOf(lens) switch
    {
        LensSource.Search => UiStrings.Format("status.worlds", worlds),
        LensSource.Local => UiStrings.Format("status.worldsOnly", worlds),
        LensSource.OpenWorlds => UiStrings.Format("status.openWorlds", worlds),
        _ => UiStrings.Format("status.summary", worlds, people),
    };
}
