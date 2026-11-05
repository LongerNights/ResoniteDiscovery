using Discover.Core.Model;
using Discover.Core.Sources;
using Discover.Resonite.Ui;
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

    private const int MaxListedUsers = 64;

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
            if (!IsListed(world, out var sessionId))
            {
                continue;
            }

            seen.Add(sessionId);
            if (!_openedAt.TryGetValue(sessionId, out var opened))
            {
                opened = now;
                _openedAt[sessionId] = opened;
            }

            result.Add(Snapshot(world, sessionId, opened, UsersOf(world)));
        }

        foreach (var gone in _openedAt.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _openedAt.Remove(gone);
        }

        return result;
    }

    public int CountOpenWorlds()
    {
        _scratch.Clear();
        _engine.WorldManager.GetWorlds(_scratch);
        var count = 0;
        foreach (var world in _scratch)
        {
            if (IsListed(world, out _))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsListed(World? world, out string sessionId)
    {
        sessionId = world?.SessionId ?? "";
        return world is not null && !world.IsDestroyed && !world.IsDisposed && world != Userspace.UserspaceWorld
            && world.State != World.WorldState.Failed && !string.IsNullOrWhiteSpace(sessionId);
    }

    /// <summary>The people in a world, for the detail panel; a failure to read them leaves the list empty.</summary>
    private IReadOnlyList<SessionUserSnapshot> UsersOf(World world)
    {
        try
        {
            var users = new List<SessionUserSnapshot>();
            foreach (var user in world.AllUsers)
            {
                if (user is null || user.IsDestroyed || string.IsNullOrWhiteSpace(user.UserName))
                {
                    continue;
                }

                var id = user.UserID ?? "";
                var contact = id.Length > 0 && _engine.Cloud.Contacts.IsContact(id, true);
                users.Add(new SessionUserSnapshot(id, RichText.Strip(user.UserName), true, OutputDevice.Unknown, contact));
                if (users.Count >= MaxListedUsers)
                {
                    break;
                }
            }

            return users;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static OpenWorldSnapshot Snapshot(World world, string sessionId, DateTime opened, IReadOnlyList<SessionUserSnapshot> users)
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
            Name = RichText.Strip(world.Name),
            RecordKey = recordKey,
            HostUsername = RichText.Strip(world.HostUser?.UserName),
            IsHost = world.IsAuthority,
            AccessLevel = AccessLevelNames.FromName(world.AccessLevel.ToString()),
            Users = world.UserCount,
            MaxUsers = world.MaxUsers,
            UserList = users,
            IsFocused = focused,
            ThumbnailUrl = thumbnail,
            OpenedAt = opened,
            LastFocusedAt = focusTime,
            CanLeave = world != Userspace.LocalHome,
        };
    }
}
