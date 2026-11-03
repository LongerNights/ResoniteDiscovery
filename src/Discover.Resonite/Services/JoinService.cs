using Discover.Core.Model;
using Discover.Core.Presentation;
using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Services;

internal enum JoinOutcome
{
    /// <summary>OpenWorld returned a world (a new join, or an existing one focused).</summary>
    Opened,

    /// <summary>The session is gone or has no URLs.</summary>
    Ended,

    /// <summary>Every session of the entry is full.</summary>
    Full,

    /// <summary>OpenWorld returned null, e.g. the mutual-block check refused. The engine already showed its own message.</summary>
    Refused,

    Failed,
}

/// <summary>
/// Joins and opens go through <see cref="Userspace.OpenWorld"/> only (CLAUDE.md rule 3): never JoinSession, which skips
/// the mutual-block check. Update thread entry; the awaited work is the engine's own.
/// </summary>
internal sealed class JoinService
{
    private readonly Engine _engine;

    public JoinService(Engine engine) => _engine = engine;

    /// <summary>True when a world for this session is already open locally (the Open badge, spec F8).</summary>
    public bool IsOpen(string sessionId) =>
        Userspace.GetExistingWorld(new WorldStartSettings { SessionID = sessionId }) is not null;

    public async Task<JoinOutcome> JoinAsync(DiscoveryEntry entry)
    {
        try
        {
            var target = JoinPlanner.PickJoinTarget(entry);
            if (target is null)
            {
                return entry.Sessions.Count > 0 ? JoinOutcome.Full : JoinOutcome.Ended;
            }

            var info = _engine.Cloud.Sessions.TryGetInfo(target.SessionId);
            if (info is null || info.HasEnded)
            {
                return JoinOutcome.Ended;
            }

            var urls = info.GetSessionURLs();
            if (urls.Count == 0)
            {
                return JoinOutcome.Ended;
            }

            var settings = new WorldStartSettings(urls[0], info.HostUserId)
            {
                URIs = urls,
                SessionID = info.SessionId,
                HostUserId = info.HostUserId,   // required for the mutual-block check
                GetExisting = true,             // focus an already-open world instead of joining twice
                AutoFocus = true,
                CreateLoadIndicator = true,
                FetchedWorldName = info.Name,
            };

            var world = await Userspace.OpenWorld(settings);
            return world is null ? JoinOutcome.Refused : JoinOutcome.Opened;
        }
        catch (Exception ex)
        {
            // Type name only: exception text from the engine can include the session URLs.
            UniLog.Warning($"[Discover] Join failed: {ex.GetType().Name}");
            return JoinOutcome.Failed;
        }
    }

    /// <summary>Error toast for a failed join; the engine reports Opened and Refused itself.</summary>
    public static void Report(JoinOutcome outcome)
    {
        var id = outcome switch
        {
            JoinOutcome.Ended => "error.joinEnded",
            JoinOutcome.Full => "error.joinFull",
            JoinOutcome.Failed => "error.joinFailed",
            _ => null,
        };

        if (id is null)
        {
            return;
        }

        var message = outcome == JoinOutcome.Failed ? UiStrings.Format(id, "see the log") : UiStrings.Get(id);
        NotificationPanel.ShowNotification(null!, message, null!, RadiantUI_Constants.Neutrals.DARK, NotificationType.ToastOnly);
    }
}
