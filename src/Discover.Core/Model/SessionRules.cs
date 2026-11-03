namespace Discover.Core.Model;

/// <summary>How a session may be listed (spec "Session visibility rule").</summary>
public enum SessionVisibility
{
    /// <summary>Never listed anywhere.</summary>
    Hidden,

    /// <summary>Contacts or ContactsPlus with at least one contact present: Friends lens only.</summary>
    FriendsOnly,

    /// <summary>RegisteredUsers or Anyone: every lens.</summary>
    Public,
}

public static class SessionRules
{
    /// <summary>
    /// Applied to every session before any lens. The engine's own feed skips this filtering for
    /// privileged (userspace) callers, so the mod must apply it itself (spec safety rule 1).
    /// Unknown access levels are hidden.
    /// </summary>
    public static SessionVisibility Classify(SessionSnapshot session)
    {
        if (session.HideFromListing || session.HasEnded || session.IsExpired)
        {
            return SessionVisibility.Hidden;
        }

        return session.AccessLevel switch
        {
            AccessLevel.RegisteredUsers or AccessLevel.Anyone => SessionVisibility.Public,
            AccessLevel.Contacts or AccessLevel.ContactsPlus when session.PresentContacts > 0 => SessionVisibility.FriendsOnly,
            _ => SessionVisibility.Hidden,
        };
    }

    /// <summary>JoinedUsers at or above MaximumUsers (spec Integration §4). MaximumUsers of 0 means unknown, not full.</summary>
    public static bool IsFull(SessionSnapshot session) => session.MaximumUsers > 0 && session.JoinedUsers >= session.MaximumUsers;
}
