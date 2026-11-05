using Discover.Core.Model;
using Discover.Resonite.Ui;
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
        Name = RichText.Strip(info.Name),
        Description = RichText.Strip(info.Description),
        Tags = info.Tags?.Select(RichText.Strip).Where(t => t.Length > 0).ToArray() ?? [],
        HostUserId = info.HostUserId ?? "",
        HostUsername = RichText.Strip(info.HostUsername),
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
        IsCompatible = info.IsCompatible(),
        Users = info.SessionUsers?
            .Select(u => new SessionUserSnapshot(u.UserID ?? "", RichText.Strip(u.Username), u.IsPresent, AccessLevelNames.DeviceFromName(u.OutputDevice?.ToString()),
                !string.IsNullOrEmpty(u.UserID) && engine.Cloud.Contacts.IsContact(u.UserID, true)))
            .ToArray() ?? [],
    };

    private static string? WorldKeyOf(SessionInfo info)
    {
        var id = info.ActualCorrespondingWorldId?.ToString();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
