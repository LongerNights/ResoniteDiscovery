namespace Discover.Core.Model;

/// <summary>Join-target choice (spec F7). The Join button is disabled when this returns null.</summary>
public static class JoinPlanner
{
    /// <summary>
    /// The session with the most contacts present, else the busiest, among sessions with free capacity;
    /// ties go to the newest, then session id. Null when every listed session is full or there are none.
    /// Differs from <see cref="DiscoveryEntry.BestSession"/>, which ignores contacts and drives card numbers.
    /// </summary>
    public static SessionSnapshot? PickJoinTarget(DiscoveryEntry entry) => entry.Sessions
        .Where(s => !SessionRules.IsFull(s))
        .OrderByDescending(s => s.PresentContacts)
        .ThenByDescending(s => s.ActiveUsers)
        .ThenByDescending(s => s.SessionBeginTime)
        .ThenBy(s => s.SessionId, StringComparer.Ordinal)
        .FirstOrDefault();
}
