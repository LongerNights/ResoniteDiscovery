using Discover.Core.Model;
using Elements.Assets;
using FrooxEngine;
using SkyFrost.Base;

namespace Discover.Resonite.Adapters;

/// <summary>
/// SessionInfo to <see cref="SessionSnapshot"/>. The snapshot has no field for BroadcastKey, HostMachineId,
/// HostUserSessionId or session URLs (spec safety rule 3), so none of them can leak past this class.
/// Update thread only: it calls <c>Contacts.CountPresentContacts</c>, which takes the contact manager's lock.
/// </summary>
internal static class SessionMapper
{
    public static SessionSnapshot Map(SessionInfo info, Engine engine) => new()
    {
        SessionId = info.SessionId,
        Name = Plain(info.Name),
        Description = Plain(info.Description),
        Tags = info.Tags?.ToArray() ?? [],
        HostUserId = info.HostUserId ?? "",
        HostUsername = Plain(info.HostUsername),
        HeadlessHost = info.HeadlessHost,
        ThumbnailUrl = info.ThumbnailUrl,
        WorldKey = WorldKeyOf(info),
        JoinedUsers = info.JoinedUsers,
        ActiveUsers = info.ActiveUsers,
        MaximumUsers = info.MaximumUsers,
        MobileFriendly = info.MobileFriendly,
        SessionBeginTime = info.SessionBeginTime,
        LastUpdate = info.LastUpdate,
        AccessLevel = AccessLevelNames.FromName(info.AccessLevel.ToString()),
        HideFromListing = info.HideFromListing,
        HasEnded = info.HasEnded,
        IsExpired = info.IsExpired,
        PresentContacts = engine.Cloud.Contacts.CountPresentContacts(info),
        Users = info.SessionUsers?
            .Select(u => new SessionUserSnapshot(u.UserID ?? "", Plain(u.Username), u.IsPresent, AccessLevelNames.DeviceFromName(u.OutputDevice?.ToString())))
            .ToArray() ?? [],
    };

    private static string? WorldKeyOf(SessionInfo info)
    {
        var id = info.ActualCorrespondingWorldId?.ToString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>Strips rich-text tags to plain text (verification V11). The UI re-neutralizes markup before display.</summary>
    public static string Plain(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Contains('<') ? new StringRenderTree(value).GetRawString() : value;
    }
}
