using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Tests.Fakes;

/// <summary>In-memory session source for tests. Raise changes explicitly with Add/Update/Remove.</summary>
public sealed class FakeSessionSource : ISessionSource
{
    private readonly Dictionary<string, SessionSnapshot> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public event Action<SessionChange>? Changed;

    public IReadOnlyList<SessionSnapshot> GetSessions() => _sessions.Values.ToList();

    public void Add(SessionSnapshot session)
    {
        _sessions[session.SessionId] = session;
        Changed?.Invoke(new SessionChange(SessionChangeKind.Added, session));
    }

    public void Update(SessionSnapshot session)
    {
        _sessions[session.SessionId] = session;
        Changed?.Invoke(new SessionChange(SessionChangeKind.Updated, session));
    }

    public void Remove(string sessionId)
    {
        if (_sessions.Remove(sessionId, out var session))
        {
            Changed?.Invoke(new SessionChange(SessionChangeKind.Removed, session));
        }
    }
}
