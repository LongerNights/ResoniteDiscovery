using Discover.Core.Model;

namespace Discover.Tests;

public class VisibilityTests
{
    public static TheoryData<AccessLevel, bool, bool, bool, int, SessionVisibility> Table()
    {
        var data = new TheoryData<AccessLevel, bool, bool, bool, int, SessionVisibility>();
        foreach (var level in Enum.GetValues<AccessLevel>())
        {
            foreach (var hidden in new[] { false, true })
            {
                foreach (var ended in new[] { false, true })
                {
                    foreach (var expired in new[] { false, true })
                    {
                        foreach (var contacts in new[] { 0, 2 })
                        {
                            data.Add(level, hidden, ended, expired, contacts, Expected(level, hidden, ended, expired, contacts));
                        }
                    }
                }
            }
        }

        return data;
    }

    // Written out independently of SessionRules so the test is a second statement of the spec rule.
    private static SessionVisibility Expected(AccessLevel level, bool hidden, bool ended, bool expired, int contacts)
    {
        if (hidden || ended || expired)
        {
            return SessionVisibility.Hidden;
        }

        return level switch
        {
            AccessLevel.RegisteredUsers or AccessLevel.Anyone => SessionVisibility.Public,
            AccessLevel.Contacts or AccessLevel.ContactsPlus => contacts > 0 ? SessionVisibility.FriendsOnly : SessionVisibility.Hidden,
            _ => SessionVisibility.Hidden,
        };
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void ClassifyMatchesSpecTable(AccessLevel level, bool hidden, bool ended, bool expired, int contacts, SessionVisibility expected)
    {
        var s = TestData.Session("s", access: level, hidden: hidden, ended: ended, expired: expired, contacts: contacts);
        Assert.Equal(expected, SessionRules.Classify(s));
    }

    [Fact]
    public void UndefinedAccessLevelIsHidden()
    {
        var s = TestData.Session("s", access: (AccessLevel)99);
        Assert.Equal(SessionVisibility.Hidden, SessionRules.Classify(s));
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    [InlineData(9, 10, false)]
    [InlineData(5, 0, false)]
    public void FullMeansJoinedReachesMaximum(int joined, int max, bool full)
    {
        Assert.Equal(full, SessionRules.IsFull(TestData.Session("s", joined: joined, max: max)));
    }
}
