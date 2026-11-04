using Discover.Core.Model;
using Discover.Core.Presentation;
using Discover.Core.Settings;

namespace Discover.Core.Lenses;

/// <summary>
/// Favorite notifications (spec F26): a message when a favorited world gets a public live session, and (off by default)
/// when contacts join a favorited world. Rate-limited per world and overall so a busy world cannot flood the screen.
/// The first look at a world only records its state, so nothing fires for what was already live at startup.
/// </summary>
public sealed class FavoriteNotifier
{
    private readonly Dictionary<string, (bool Live, int Friends, DateTime LastToast)> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recent = new();

    /// <summary>Records the current state of the entry (null when the world has no entry) without notifying.</summary>
    public void Baseline(string key, DiscoveryEntry? entry) =>
        _state[key] = (IsLive(entry), entry?.FriendsPresent ?? 0, DateTime.MinValue);

    /// <summary>
    /// Compares the entry with what was last seen and returns the messages to show (zero or more). Call for favorited
    /// keys whose entries changed.
    /// </summary>
    public IReadOnlyList<string> Observe(string key, DiscoveryEntry? entry, DateTime nowUtc, DiscoverSettings settings)
    {
        var live = IsLive(entry);
        var friends = entry?.FriendsPresent ?? 0;
        if (!_state.TryGetValue(key, out var before))
        {
            _state[key] = (live, friends, DateTime.MinValue);
            return [];
        }

        var messages = new List<string>(1);
        var name = entry?.DisplayName ?? key;
        var due = nowUtc - before.LastToast >= settings.NotifyMinGapPerWorld;
        var toasted = false;

        if (settings.NotifyFavoriteLive && live && !before.Live && due && TryTake(nowUtc, settings))
        {
            messages.Add(UiStrings.Format("notify.live", name));
            toasted = true;
        }
        else if (settings.NotifyContactsInFavorite && live && friends > 0 && before.Friends == 0 && due && TryTake(nowUtc, settings))
        {
            messages.Add(UiStrings.Format("notify.contacts", name));
            toasted = true;
        }

        _state[key] = (live, friends, toasted ? nowUtc : before.LastToast);
        return messages;
    }

    private static bool IsLive(DiscoveryEntry? entry) => entry is { PublicSessions.Count: > 0 };

    private bool TryTake(DateTime nowUtc, DiscoverSettings settings)
    {
        while (_recent.Count > 0 && nowUtc - _recent.Peek() > settings.NotifyWindow)
        {
            _recent.Dequeue();
        }

        if (_recent.Count >= settings.NotifyMaxPerWindow)
        {
            return false;
        }

        _recent.Enqueue(nowUtc);
        return true;
    }
}
