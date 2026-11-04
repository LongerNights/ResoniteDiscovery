using Discover.Core.Model;
using Discover.Core.Sources;
using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Adapters;

/// <summary>
/// The worlds the user has open, read from <c>Engine.WorldManager.Worlds</c> on the update thread (V25). Userspace is left
/// out. Costs no requests. Open times are remembered here, in memory only, keyed by session id.
/// </summary>
internal sealed class EngineOpenWorldAdapter : IOpenWorldSource
{
    private readonly Engine _engine;
    private readonly Dictionary<string, DateTime> _openedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<World> _scratch = [];

    public EngineOpenWorldAdapter(Engine engine) => _engine = engine;

    public IReadOnlyList<OpenWorldSnapshot> GetOpenWorlds()
    {
        var now = DateTime.UtcNow;
        _scratch.Clear();
        _engine.WorldManager.GetWorlds(_scratch);

        var result = new List<OpenWorldSnapshot>(_scratch.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var world in _scratch)
        {
            if (world is null || world.IsDestroyed || world.IsDisposed || world == Userspace.UserspaceWorld
                || world.State == World.WorldState.Failed)
            {
                continue;
            }

            var sessionId = world.SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            seen.Add(sessionId);
            if (!_openedAt.TryGetValue(sessionId, out var opened))
            {
                opened = now;
                _openedAt[sessionId] = opened;
            }

            result.Add(Snapshot(world, sessionId, opened));
        }

        foreach (var gone in _openedAt.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _openedAt.Remove(gone);
        }

        return result;
    }

    private static OpenWorldSnapshot Snapshot(World world, string sessionId, DateTime opened)
    {
        string? recordKey = null;
        if (world.CorrespondingRecord is { } record)
        {
            recordKey = record.CombinedRecordId?.ToString();
            if (string.IsNullOrWhiteSpace(recordKey) && !string.IsNullOrWhiteSpace(record.OwnerId) && !string.IsNullOrWhiteSpace(record.RecordId))
            {
                recordKey = record.OwnerId + ":" + record.RecordId;
            }
        }

        // Back-ground and overlay worlds keep their own focus state; only the one the user is looking at is "here".
        var focused = world.Focus == World.WorldFocus.Focused;
        var focusTime = world.LastFocusChange;
        if (focusTime.Kind == DateTimeKind.Local)
        {
            focusTime = focusTime.ToUniversalTime();
        }

        string? thumbnail = null;
        try
        {
            thumbnail = Userspace.GetThumbnailData(world)?.LocalThumbnail?.ToString(); // V26: the stock card uses this first
        }
        catch (Exception)
        {
            // No thumbnail yet.
        }

        return new OpenWorldSnapshot
        {
            SessionId = sessionId,
            Name = SessionMapper.Plain(world.Name),
            RecordKey = recordKey,
            HostUsername = SessionMapper.Plain(world.HostUser?.UserName),
            IsHost = world.IsAuthority,
            AccessLevel = AccessLevelNames.FromName(world.AccessLevel.ToString()),
            Users = world.UserCount,
            MaxUsers = world.MaxUsers,
            IsFocused = focused,
            ThumbnailUrl = thumbnail,
            OpenedAt = opened,
            LastFocusedAt = focusTime,
            CanLeave = world != Userspace.LocalHome,
        };
    }
}
