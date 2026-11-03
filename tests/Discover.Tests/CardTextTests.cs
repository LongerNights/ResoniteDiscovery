using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class CardTextTests
{
    private static readonly DateTime Now = TestData.Now;

    [Theory]
    [InlineData(0, "now")]
    [InlineData(-5, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1 m")]
    [InlineData(59 * 60, "59 m")]
    [InlineData(3600, "1 h")]
    [InlineData(23 * 3600, "23 h")]
    [InlineData(86400, "1 d")]
    [InlineData(3 * 86400, "3 d")]
    [InlineData(59 * 86400, "59 d")]
    [InlineData(60 * 86400, "2 mo")]
    [InlineData(400 * 86400, "13 mo")]
    [InlineData(800 * 86400, "2 y")]
    public void AgeReadsAsTheSpecExample(int secondsAgo, string expected)
    {
        Assert.Equal(expected, CardText.Age(Now.AddSeconds(-secondsAgo), Now));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-3, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(12_400, "12.4k")]
    [InlineData(12_449, "12.4k")]
    [InlineData(999_999, "999.9k")]
    [InlineData(1_000_000, "1M")]
    [InlineData(3_250_000, "3.2M")]
    public void CompactMatchesTheCardFooterExample(long value, string expected)
    {
        Assert.Equal(expected, CardText.Compact(value));
    }

    [Fact]
    public void CompactIsCultureIndependent()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("12.4k", CardText.Compact(12_400));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Theory]
    [InlineData("Short", 10, "Short")]
    [InlineData("  padded  ", 10, "padded")]
    [InlineData("Exactly10!", 10, "Exactly10!")]
    [InlineData("The Grand Oasis Karaoke", 10, "The Grand…")]
    [InlineData("Ends in   spaces", 10, "Ends in…")] // the cut lands on spaces, which are trimmed
    [InlineData("", 10, "")]
    [InlineData(null, 10, "")]
    [InlineData("Anything", 0, "")]
    [InlineData("Ab", 1, "…")]
    public void EllipsizeCutsToTheLimit(string? value, int max, string expected)
    {
        Assert.Equal(expected, CardText.Ellipsize(value, max));
    }

    [Fact]
    public void EllipsizeNeverSplitsASurrogatePair()
    {
        var text = "abc😀def"; // the emoji is two UTF-16 units at index 3 and 4
        var cut = CardText.Ellipsize(text, 5);

        Assert.Equal("abc…", cut);
        Assert.True(cut.Length <= 5);
    }

    [Theory]
    [InlineData(5, 10, "5/10")]
    [InlineData(0, 0, "0")]
    [InlineData(7, 0, "7")]
    public void UsersShowsActiveOverMaximum(int active, int max, string expected)
    {
        Assert.Equal(expected, CardText.Users(active, max));
    }

    [Theory]
    [InlineData("Private", AccessLevel.Private)]
    [InlineData("LAN", AccessLevel.LAN)]
    [InlineData("Contacts", AccessLevel.Contacts)]
    [InlineData("Friends", AccessLevel.Contacts)]
    [InlineData("ContactsPlus", AccessLevel.ContactsPlus)]
    [InlineData("FriendsOfFriends", AccessLevel.ContactsPlus)]
    [InlineData("RegisteredUsers", AccessLevel.RegisteredUsers)]
    [InlineData("Anyone", AccessLevel.Anyone)]
    [InlineData("Brand New Level", AccessLevel.Private)]
    [InlineData(null, AccessLevel.Private)]
    public void AccessLevelNamesMapIncludingAliases(string? name, AccessLevel expected)
    {
        Assert.Equal(expected, AccessLevelNames.FromName(name));
    }

    [Theory]
    [InlineData("VR", OutputDevice.VR)]
    [InlineData("Screen", OutputDevice.Screen)]
    [InlineData("Camera", OutputDevice.Camera)]
    [InlineData("Unknown", OutputDevice.Unknown)]
    [InlineData(null, OutputDevice.Unknown)]
    public void DeviceNamesMap(string? name, OutputDevice expected)
    {
        Assert.Equal(expected, AccessLevelNames.DeviceFromName(name));
    }

    [Fact]
    public void StringTableFormatsAndFallsBackToTheId()
    {
        Assert.Equal("3 friends", UiStrings.Format("card.friends", 3));
        Assert.Equal("missing.id", UiStrings.Get("missing.id"));
        Assert.All(UiStrings.Ids, id => Assert.NotEqual(id, UiStrings.Get(id)));
    }
}
