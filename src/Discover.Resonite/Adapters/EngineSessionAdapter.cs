using System.Collections.Concurrent;
using Discover.Core.Model;
using Discover.Core.Sources;
using Elements.Core;
using FrooxEngine;
using SkyFrost.Base;

namespace Discover.Resonite.Adapters;

/// <summary>
/// <see cref="ISessionSource"/> over <c>Engine.Cloud.Sessions</c>. The engine raises SessionAdded/Updated/Removed
/// synchronously while holding its own lock, on whatever thread delivered the update (verification V9). The handlers
/// here therefore only enqueue the raw <see cref="SessionInfo"/>: mapping calls into the contact manager, which has a
/// second lock, and that must not happen inside the sessions lock. <see cref="Pump"/> maps and raises
/// <see cref="Changed"/> from the update thread, so in this adapter <see cref="Changed"/> always fires there.
/// </summary>
internal sealed class EngineSessionAdapter : ISessionSource, IDisposable
{
    private readonly Engine _engine;
    private readonly ConcurrentQueue<(SessionChangeKind Kind, SessionInfo Info)> _raw = new();
    private bool _disposed;

    public EngineSessionAdapter(Engine engine)
    {
        _engine = engine;
        var sessions = engine.Cloud.Sessions;
        sessions.SessionAdded += OnAdded;
        sessions.SessionUpdated += OnUpdated;
        sessions.SessionRemoved += OnRemoved;
    }

    public event Action<SessionChange>? Changed;

    /// <summary>The stock list hides sessions the client cannot join (decisions Q2). Default true.</summary>
    public bool HideIncompatible { get; set; } = true;

    public int Pending => _raw.Count;

    /// <summary>Update thread only. Maps the engine's cached sessions (expired ones already skipped by the engine).</summary>
    public IReadOnlyList<SessionSnapshot> GetSessions()
    {
        var infos = new List<SessionInfo>();
        _engine.Cloud.Sessions.GetSessions(infos);
        var result = new List<SessionSnapshot>(infos.Count);
        foreach (var info in infos)
        {
            if (Accepts(info))
            {
                result.Add(SessionMapper.Map(info, _engine));
            }
        }

        return result;
    }

    /// <summary>Update thread only. Raises <see cref="Changed"/> for at most <paramref name="max"/> queued changes.</summary>
    public int Pump(int max)
    {
        var count = 0;
        while (count < max && _raw.TryDequeue(out var item))
        {
            count++;
            try
            {
                // An update that makes a session unacceptable is reported as a removal so the index drops it.
                var kind = item.Kind != SessionChangeKind.Removed && !Accepts(item.Info) ? SessionChangeKind.Removed : item.Kind;
                Changed?.Invoke(new SessionChange(kind, SessionMapper.Map(item.Info, _engine)));
            }
            catch (Exception ex)
            {
                // One bad session must not stall the queue. Log the id only, never the SessionInfo (it contains URLs).
                UniLog.Warning($"[Discover] Skipped a session change: {ex.GetType().Name}");
            }
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var sessions = _engine.Cloud.Sessions;
        sessions.SessionAdded -= OnAdded;
        sessions.SessionUpdated -= OnUpdated;
        sessions.SessionRemoved -= OnRemoved;
    }

    private bool Accepts(SessionInfo info) =>
        !string.IsNullOrEmpty(info.SessionId) && (!HideIncompatible || info.IsCompatible());

    private void OnAdded(SessionInfo info) => _raw.Enqueue((SessionChangeKind.Added, info));

    private void OnUpdated(SessionInfo info) => _raw.Enqueue((SessionChangeKind.Updated, info));

    private void OnRemoved(SessionInfo info) => _raw.Enqueue((SessionChangeKind.Removed, info));
}
