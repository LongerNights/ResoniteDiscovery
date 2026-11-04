using Discover.Core.Model;

namespace Discover.Core.Presentation;

/// <summary>The detail panel's content for one world or session (spec F18). Plain text: the UI escapes it.</summary>
public sealed record DetailModel(
    string Key,
    string Title,
    string ByLine,
    string Description,
    string Tags,
    string Sessions,
    string Devices,
    CardAction Action,
    bool CanSave,
    bool CanCopyLink,
    LeaveKind Leave = LeaveKind.None,
    bool CanMoreByAuthor = false,
    bool IsFavorite = false,
    string? ThumbnailUrl = null,
    IReadOnlyList<string>? TagList = null,
    IReadOnlyList<DetailSession>? SessionRows = null,
    string Dates = "",
    bool CanStartNew = false);

/// <summary>One live session in the detail panel: its line of text and whether it can be joined (not full, same build).</summary>
public sealed record DetailSession(
    string SessionId,
    string ListText,
    bool CanJoin,
    string Uptime,
    string Visibility,
    string UsersLine,
    IReadOnlyList<DetailUser> Users);

/// <summary>A person in a session, for the detail panel's user list; contacts are marked, as on the stock tab.</summary>
public sealed record DetailUser(string Name, bool IsContact);

/// <summary>What the detail panel's leave button does: nothing, leave a world you joined, or end one you host.</summary>
public enum LeaveKind
{
    None,
    Leave,
    End,
}

public static class DetailBuilder
{
    public const int MaxSessions = 4;
    public const int MaxSessionRows = 3;
    public const int MaxTags = 16;

    /// <summary>
    /// One session as the stock info panel shows it: its entry in the list (host and how many are in), then uptime,
    /// who can join, how many are in (and away, out of the maximum), and who they are, contacts first.
    /// </summary>
    private static DetailSession SessionRow(SessionSnapshot s, bool canJoin, DateTime nowUtc)
    {
        var away = Math.Max(0, s.JoinedUsers - s.ActiveUsers);
        var of = s.MaximumUsers > 0 ? s.MaximumUsers.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        var users = (away, of.Length) switch
        {
            (> 0, > 0) => UiStrings.Format("detail.usersLine.awayOf", s.ActiveUsers, away, of),
            (> 0, _) => UiStrings.Format("detail.usersLine.away", s.ActiveUsers, away),
            (_, > 0) => UiStrings.Format("detail.usersLine.of", s.ActiveUsers, of),
            _ => UiStrings.Format("detail.usersLine.plain", s.ActiveUsers),
        };

        return new DetailSession(
            s.SessionId,
            UiStrings.Format("detail.session.list", CardText.Ellipsize(s.HostUsername.Length > 0 ? s.HostUsername : s.Name, 16), CardText.Users(s.ActiveUsers, s.MaximumUsers)),
            canJoin,
            UiStrings.Format("detail.uptime", CardText.Age(s.SessionBeginTime, nowUtc)),
            UiStrings.Format("detail.visibility", UiStrings.Get("stock.access." + s.AccessLevel)),
            users,
            s.Users
                .Where(u => u.Username.Length > 0)
                .OrderByDescending(u => u.IsContact)
                .ThenBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
                .Select(u => new DetailUser(u.Username, u.IsContact))
                .ToList());
    }

    public static DetailModel Build(DiscoveryEntry entry, CardModel card, DateTime nowUtc, OpenWorldSnapshot? openWorld = null)
    {
        var record = entry.Record;
        var by = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(record?.OwnerName))
        {
            by.Add(UiStrings.Format("card.by", CardText.Ellipsize(record.OwnerName, 30)));
        }

        if (record?.FirstPublishTime is { } published)
        {
            by.Add(CardText.Age(published, nowUtc));
        }

        if (record is { Visits: > 0 })
        {
            by.Add(UiStrings.Format("card.visits", CardText.Compact(record.Visits)));
        }

        var description = !string.IsNullOrWhiteSpace(record?.Description)
            ? record.Description
            : entry.Sessions.Select(s => s.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? "";

        var tags = (record?.Tags ?? [])
            .Concat(entry.Sessions.SelectMany(s => s.Tags))
            .Concat(entry.PersonalTags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToList();

        var sessions = entry.Sessions.Count == 0
            ? UiStrings.Get("detail.noSessions")
            : string.Join("\n", entry.Sessions.Take(MaxSessions).Select(s =>
                UiStrings.Format("detail.session", CardText.Ellipsize(s.HostUsername, 22), CardText.Users(s.ActiveUsers, s.MaximumUsers), CardText.Age(s.SessionBeginTime, nowUtc))));

        var joinable = JoinPlanner.JoinableSessions(entry).Select(j => j.SessionId).ToHashSet(StringComparer.Ordinal);
        var rows = entry.Sessions.Take(MaxSessionRows).Select(s => SessionRow(s, joinable.Contains(s.SessionId), nowUtc)).ToList();

        var dates = record?.FirstPublishTime is { } first && record.LastUpdate is { } last
            ? UiStrings.Format("detail.dates", CardText.Age(first, nowUtc), CardText.Age(last, nowUtc))
            : record?.FirstPublishTime is { } only ? UiStrings.Format("detail.datesPublished", CardText.Age(only, nowUtc)) : "";

        var mix = entry.DeviceMix;
        var devices = mix.Total == 0 ? "" : UiStrings.Format("detail.devices", mix.Vr, mix.Screen, mix.Camera);

        return new DetailModel(
            entry.Key,
            CardText.Ellipsize(entry.DisplayName, 60),
            string.Join(" · ", by),
            description.Length == 0 ? UiStrings.Get("detail.noDescription") : CardText.Ellipsize(description, 360),
            string.Join(", ", tags) is { Length: > 0 } joined ? joined : UiStrings.Get("detail.noTags"),
            sessions,
            devices,
            card.Action,
            entry.CanSave,
            RecordUrls.For(entry) is not null,
            openWorld is not { CanLeave: true } ? LeaveKind.None : openWorld.IsHost ? LeaveKind.End : LeaveKind.Leave,
            !string.IsNullOrEmpty(record?.OwnerId),
            card.IsFavorite,
            card.ThumbnailUrl,
            tags,
            rows,
            dates,
            entry.IsWorld && record is not null);
    }
}
