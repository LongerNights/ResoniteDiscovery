using System.Collections.Concurrent;
using Discover.Core.Sources;

namespace Discover.Core.Model;

/// <summary>
/// The hand-off between <see cref="ISessionSource.Changed"/> (any thread, spec Integration §3) and the controller's tick.
/// Handlers only <see cref="Enqueue"/>; the controller calls <see cref="Drain"/> once per update tick.
/// </summary>
public sealed class SessionChangeQueue
{
    public const int DefaultMaxPerTick = 50;

    private readonly ConcurrentQueue<SessionChange> _queue = new();

    public int Pending => _queue.Count;

    /// <summary>Lock-free and non-blocking; safe from the engine's session-manager lock.</summary>
    public void Enqueue(SessionChange change) => _queue.Enqueue(change);

    /// <summary>Applies at most <paramref name="maxChanges"/> queued changes in arrival order; the rest wait for the next tick.</summary>
    public IReadOnlyCollection<string> Drain(DiscoveryIndex index, int maxChanges = DefaultMaxPerTick)
    {
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var applied = 0; applied < maxChanges && _queue.TryDequeue(out var change); applied++)
        {
            foreach (var key in index.Apply(change))
            {
                touched.Add(key);
            }
        }

        return touched;
    }
}
