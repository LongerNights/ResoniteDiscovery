using Discover.Core.Model;
using Discover.Core.Presentation;
using Discover.Resonite.Ui;
using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Services;

/// <summary>The save choices a world offers on close: Save needs a record the user can modify, Save as only the right to save worlds.</summary>
internal readonly record struct SavePrompt(bool CanSave, bool CanSaveAs);

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
    /// open world is focused, not joined twice. With <paramref name="focus"/> false the world is joined in the background:
    /// it opens (and shows its load indicator) without taking the focus, and an already open one is left as it is.
    /// </summary>
    public async Task<JoinOutcome> JoinAsync(DiscoveryEntry entry, string? sessionId = null, bool focus = true)
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
                AutoFocus = focus,
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
    public async Task<JoinOutcome> StartNewSessionAsync(RecordSnapshot record, SkyFrost.Base.SessionAccessLevel? accessLevel = null, bool mobileFriendly = false)
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
                MobileFriendly = mobileFriendly ? true : null,
                DefaultAccessLevel = accessLevel, // null keeps the engine's own default (verification V28)
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

    /// <summary>
    /// The stock "Get Session Orb": a world orb for the live session, placed in front of the user in the focused world
    /// (<c>WorldOrb.SpawnSessionOrb</c>, as <c>LegacyWorldDetail.OnGetSessionOrb</c> calls it). The orb is local to the
    /// user's own space and the engine fills it from the session info, so the mod never reads the session URLs. Update thread.
    /// </summary>
    public bool SpawnSessionOrb(string sessionId)
    {
        try
        {
            var info = _engine.Cloud.Sessions.TryGetInfo(sessionId);
            if (info is null || info.HasEnded || _engine.WorldManager.FocusedWorld is not { } world)
            {
                return false;
            }

            WorldOrb.SpawnSessionOrb(world, info);
            return true;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Session orb failed: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// The stock "Get World Orb": an orb for the published world, from its record (<c>WorldOrb.SpawnWorldOrb</c>). The
    /// record comes from the engine's record cache; the spawn itself is queued onto the focused world by the engine, so
    /// continuing on any thread is safe.
    /// </summary>
    public async Task<bool> SpawnWorldOrbAsync(RecordSnapshot record)
    {
        try
        {
            var world = _engine.WorldManager.FocusedWorld;
            if (world is null)
            {
                return false;
            }

            var full = await _engine.Cloud.Records.RecordCache<SkyFrost.Base.Record>().Get(record.OwnerId, record.RecordId).ConfigureAwait(false);
            if (full is null || full.IsDeleted)
            {
                return false;
            }

            WorldOrb.SpawnWorldOrb(world, full);
            return true;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] World orb failed: {ex.GetType().Name}");
            return false;
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
        Toasts.Show(message);
    }

    /// <summary>Brings an open world to the front, as the stock Worlds tab does for a world already open (V4). Update thread.</summary>
    public bool Focus(string sessionId)
    {
        try
        {
            if (Userspace.GetExistingWorld(new WorldStartSettings { SessionID = sessionId }) is not { } world || world.IsDestroyed)
            {
                return false;
            }

            _engine.WorldManager.FocusWorld(world);
            return true;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Focus failed: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// What closing this world offers, as the stock close dialog decides it (<c>WorldCloseAction</c>, V34): the save menu
    /// when <c>Userspace.ShouldSave</c>, with Save only if the world has a record the user may modify and Save as only if
    /// they may save worlds at all. Null when the world is gone or closes without the menu. Update thread.
    /// </summary>
    public SavePrompt? SavePromptFor(string sessionId)
    {
        try
        {
            if (Userspace.GetExistingWorld(new WorldStartSettings { SessionID = sessionId }) is not { } world || world.IsDestroyed
                || world == Userspace.LocalHome || !Userspace.ShouldSave(world))
            {
                return null;
            }

            var allowed = world.IsAllowedToSaveWorld();
            return new SavePrompt(allowed && Userspace.CanSave(world), allowed);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Save check failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>Saves the world through the engine's own path and closes it afterwards (<c>exitOnSave</c>), as the stock Save and Save as buttons do.</summary>
    public async Task<bool> SaveAndCloseAsync(string sessionId, SaveType saveType)
    {
        try
        {
            if (Userspace.GetExistingWorld(new WorldStartSettings { SessionID = sessionId }) is not { } world || world.IsDestroyed)
            {
                return false;
            }

            await Userspace.SaveWorldAuto(world, saveType, exitOnSave: true);
            return true;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Save failed: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// Leaves a world you are a guest in, or ends one you host, through the engine's own path (V27:
    /// <c>Userspace.ExitWorld</c> picks <c>EndSession</c> or <c>LeaveSession</c> by authority and refuses the local home).
    /// Update thread entry; returns false when the world is gone, refused or threw.
    /// </summary>
    public async Task<bool> LeaveAsync(string sessionId)
    {
        try
        {
            if (Userspace.GetExistingWorld(new WorldStartSettings { SessionID = sessionId }) is not { } world || world.IsDestroyed)
            {
                return false;
            }

            await Userspace.ExitWorld(world);
            return true;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Leave failed: {ex.GetType().Name}");
            return false;
        }
    }
}

