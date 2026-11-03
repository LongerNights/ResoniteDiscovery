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

    /// <summary>The first open session of the entry, if any (the "You're here" state).</summary>
    public SessionSnapshot? OpenSession(DiscoveryEntry entry) => entry.Sessions.FirstOrDefault(s => IsOpen(s.SessionId));

    /// <summary>
    /// Joins <paramref name="sessionId"/> when given (the join menu's pick), else the planner's best session. An already
    /// open world is focused, not joined twice.
    /// </summary>
    public async Task<JoinOutcome> JoinAsync(DiscoveryEntry entry, string? sessionId = null)
    {
        try
        {
            var target = sessionId is null
                ? JoinPlanner.PickJoinTarget(entry) ?? OpenSession(entry)
                : entry.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Starts a new session of a published world (spec Integration §4, "Open New Instance"), by its record URI;
    /// equivalent to the stock open path (verification V4, V5).
    /// </summary>
    public async Task<JoinOutcome> StartNewSessionAsync(RecordSnapshot record)
    {
        try
        {
            var uri = _engine.Cloud.Records.GenerateRecordUri(record.OwnerId, record.RecordId);
            var settings = new WorldStartSettings(uri)
            {
                FetchedWorldName = record.Name,
                GetExisting = false,
                AutoFocus = true,
                CreateLoadIndicator = true,
            };

            var world = await Userspace.OpenWorld(settings);
            return world is null ? JoinOutcome.Refused : JoinOutcome.Opened;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] New session failed: {ex.GetType().Name}");
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
