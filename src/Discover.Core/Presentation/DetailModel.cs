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
    bool IsFavorite = false);

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
    public const int MaxTags = 16;

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
            .Take(MaxTags);

        var sessions = entry.Sessions.Count == 0
            ? UiStrings.Get("detail.noSessions")
            : string.Join("\n", entry.Sessions.Take(MaxSessions).Select(s =>
                UiStrings.Format("detail.session", CardText.Ellipsize(s.HostUsername, 22), CardText.Users(s.ActiveUsers, s.MaximumUsers), CardText.Age(s.SessionBeginTime, nowUtc))));

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
            card.IsFavorite);
    }
}
