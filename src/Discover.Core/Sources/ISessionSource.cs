using Discover.Core.Model;

namespace Discover.Core.Sources;

public enum SessionChangeKind
{
    Added,
    Updated,
    Removed,
}

public readonly record struct SessionChange(SessionChangeKind Kind, SessionSnapshot Session);

/// <summary>
/// Live sessions, backed in game by Engine.Cloud.Sessions (GetSessions + SessionAdded/Updated/Removed).
/// </summary>
public interface ISessionSource
{
    /// <summary>Current cached sessions (expired ones already skipped by the engine).</summary>
    IReadOnlyList<SessionSnapshot> GetSessions();

    /// <summary>
    /// Raised for each change. May fire on ANY thread: handlers must only enqueue
    /// (spec Integration §3). Applying changes is the controller's job, once per tick.
    /// </summary>
    event Action<SessionChange>? Changed;
}
