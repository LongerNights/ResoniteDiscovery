using Discover.Core.Sources;

namespace Discover.Core.Model;

/// <summary>
/// Merges sessions and records into <see cref="DiscoveryEntry"/> items (spec "Data model"): records are keyed by
/// <see cref="RecordSnapshot.Key"/>, sessions by their world key when they have one, else <c>S:&lt;session id&gt;</c>.
/// Not thread-safe: the controller owns it and mutates it on the update thread only (spec Integration §3).
/// A session change rebuilds only the entries it touches.
/// </summary>
public sealed class DiscoveryIndex
{
    private readonly Dictionary<string, SessionSnapshot> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecordSnapshot> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _sessionIdsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiscoveryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<DiscoveryEntry> Entries => _entries.Values;
    public int Count => _entries.Count;

    /// <summary>Goes up on every rebuilt entry, so a cache of anything derived from the entries knows when to refresh.</summary>
    public int Version { get; private set; }

    public bool TryGetEntry(string key, out DiscoveryEntry entry) => _entries.TryGetValue(key, out entry!);

    public static string KeyFor(SessionSnapshot session) =>
        string.IsNullOrWhiteSpace(session.WorldKey) ? DiscoveryKeys.ForStandaloneSession(session.SessionId) : session.WorldKey;

    /// <summary>Replaces the session set, e.g. from <see cref="ISessionSource.GetSessions"/> at startup. Returns every key touched.</summary>
    public IReadOnlyCollection<string> LoadSessions(IEnumerable<SessionSnapshot> sessions)
    {
        var touched = new HashSet<string>(_sessionIdsByKey.Keys, StringComparer.OrdinalIgnoreCase);
        _sessions.Clear();
        _sessionIdsByKey.Clear();
        foreach (var session in sessions)
        {
            _sessions[session.SessionId] = session;
            Link(session);
            touched.Add(KeyFor(session));
        }

        foreach (var key in touched)
        {
            Rebuild(key);
        }

        return touched;
    }

    /// <summary>Applies one change. Returns the keys whose entries changed (two when a session moved between worlds).</summary>
    public IReadOnlyCollection<string> Apply(SessionChange change)
    {
        var session = change.Session;
        var touched = new List<string>(2);

        if (_sessions.TryGetValue(session.SessionId, out var previous))
        {
            var oldKey = KeyFor(previous);
            Unlink(previous);
            touched.Add(oldKey);
        }

        if (change.Kind == SessionChangeKind.Removed)
        {
            _sessions.Remove(session.SessionId);
        }
        else
        {
            _sessions[session.SessionId] = session;
            Link(session);
            var key = KeyFor(session);
            if (!touched.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                touched.Add(key);
            }
        }

        foreach (var key in touched)
        {
            Rebuild(key);
        }

        return touched;
    }

    /// <summary>Adds or replaces a record (search result or hydration).</summary>
    public string SetRecord(RecordSnapshot record)
    {
        _records[record.Key] = record;
        Rebuild(record.Key);
        return record.Key;
    }

    public bool RemoveRecord(string key)
    {
        if (!_records.Remove(key))
        {
            return false;
        }

        Rebuild(key);
        return true;
    }

    /// <summary>Replaces the entry's local decoration (lists and personal tags) without touching sessions or records.</summary>
    public void Decorate(string key, IReadOnlyList<string> lists, IReadOnlyList<string> personalTags)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = entry with { Lists = lists, PersonalTags = personalTags };
        }
    }

    private void Link(SessionSnapshot session)
    {
        var key = KeyFor(session);
        if (!_sessionIdsByKey.TryGetValue(key, out var ids))
        {
            ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _sessionIdsByKey[key] = ids;
        }

        ids.Add(session.SessionId);
    }

    private void Unlink(SessionSnapshot session)
    {
        var key = KeyFor(session);
        if (_sessionIdsByKey.TryGetValue(key, out var ids))
        {
            ids.Remove(session.SessionId);
            if (ids.Count == 0)
            {
                _sessionIdsByKey.Remove(key);
            }
        }
    }

    private void Rebuild(string key)
    {
        Version++;
        _records.TryGetValue(key, out var record);

        var listed = new List<SessionSnapshot>();
        var publicSessions = new List<SessionSnapshot>();
        if (_sessionIdsByKey.TryGetValue(key, out var ids))
        {
            foreach (var id in ids)
            {
                var session = _sessions[id];
                switch (SessionRules.Classify(session))
                {
                    case SessionVisibility.Public:
                        listed.Add(session);
                        publicSessions.Add(session);
                        break;
                    case SessionVisibility.FriendsOnly:
                        listed.Add(session);
                        break;
                }
            }
        }

        if (record is null && listed.Count == 0)
        {
            _entries.Remove(key);
            return;
        }

        _entries.TryGetValue(key, out var existing);
        _entries[key] = new DiscoveryEntry
        {
            Key = record?.Key ?? key,
            Record = record,
            Sessions = Order(listed),
            PublicSessions = Order(publicSessions),
            Lists = existing?.Lists ?? [],
            PersonalTags = existing?.PersonalTags ?? [],
        };
    }

    private static SessionSnapshot[] Order(List<SessionSnapshot> sessions) => sessions
        .OrderByDescending(s => s.ActiveUsers)
        .ThenByDescending(s => s.SessionBeginTime)
        .ThenBy(s => s.SessionId, StringComparer.Ordinal)
        .ToArray();
}
