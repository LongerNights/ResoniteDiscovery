namespace Discover.Core.Model;

/// <summary>Join-target choice (spec F7). The Join button is disabled when this returns null.</summary>
public static class JoinPlanner
{
    /// <summary>
    /// The session with the most contacts present, else the busiest, among sessions with free capacity;
    /// ties go to the newest, then session id. Null when every listed session is full or there are none.
    /// Differs from <see cref="DiscoveryEntry.BestSession"/>, which ignores contacts and drives card numbers.
    /// </summary>
    public static SessionSnapshot? PickJoinTarget(DiscoveryEntry entry) => JoinableSessions(entry).FirstOrDefault();

    /// <summary>
    /// Every session the user could join, best first (the order <see cref="PickJoinTarget"/> uses). The join menu lists
    /// these. Sessions on another build are left out.
    /// </summary>
    public static IReadOnlyList<SessionSnapshot> JoinableSessions(DiscoveryEntry entry) => entry.Sessions
        .Where(s => s.IsCompatible && !SessionRules.IsFull(s))
        .OrderByDescending(s => s.PresentContacts)
        .ThenByDescending(s => s.ActiveUsers)
        .ThenByDescending(s => s.SessionBeginTime)
        .ThenBy(s => s.SessionId, StringComparer.Ordinal)
        .ToList();
}
