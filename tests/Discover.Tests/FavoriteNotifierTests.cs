using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Settings;

namespace Discover.Tests;

public class FavoriteNotifierTests
{
    private static readonly DateTime T0 = TestData.Now;

    private static DiscoveryEntry Live(string key, int contacts = 0)
    {
        var index = TestData.IndexOf(TestData.Session("s-" + key, world: key, contacts: contacts, name: "Plaza"));
        index.TryGetEntry(key, out var entry);
        return entry;
    }

    [Fact]
    public void FirstSightAndStartupStateDoNotNotify()
    {
        var notifier = new FavoriteNotifier();
        var settings = new DiscoverSettings();

        Assert.Empty(notifier.Observe("U-a:R-1", Live("U-a:R-1"), T0, settings)); // never seen: just recorded
        notifier.Baseline("U-a:R-2", Live("U-a:R-2")); // already live at startup
        Assert.Empty(notifier.Observe("U-a:R-2", Live("U-a:R-2"), T0, settings));
    }

    [Fact]
    public void ToastsOnceWhenAFavoriteGoesLiveThenRespectsTheGap()
    {
        var notifier = new FavoriteNotifier();
        var settings = new DiscoverSettings();
        notifier.Baseline("U-a:R-1", null);

        Assert.Single(notifier.Observe("U-a:R-1", Live("U-a:R-1"), T0, settings));
        Assert.Empty(notifier.Observe("U-a:R-1", Live("U-a:R-1"), T0.AddMinutes(1), settings)); // still live

        Assert.Empty(notifier.Observe("U-a:R-1", null, T0.AddMinutes(2), settings)); // ended
        Assert.Empty(notifier.Observe("U-a:R-1", Live("U-a:R-1"), T0.AddMinutes(3), settings)); // back within the gap
        Assert.Empty(notifier.Observe("U-a:R-1", null, T0.AddMinutes(4), settings));
        Assert.Single(notifier.Observe("U-a:R-1", Live("U-a:R-1"), T0.AddMinutes(40), settings)); // gap passed
    }

    [Fact]
    public void CapsMessagesPerWindowAndHonoursTheSwitch()
    {
        var notifier = new FavoriteNotifier();
        var settings = new DiscoverSettings { NotifyMaxPerWindow = 2 };
        var shown = 0;
        for (var i = 0; i < 5; i++)
        {
            var key = $"U-a:R-{i}";
            notifier.Baseline(key, null);
            shown += notifier.Observe(key, Live(key), T0.AddSeconds(i), settings).Count;
        }

        Assert.Equal(2, shown);

        var off = new FavoriteNotifier();
        off.Baseline("U-a:R-9", null);
        Assert.Empty(off.Observe("U-a:R-9", Live("U-a:R-9"), T0, new DiscoverSettings { NotifyFavoriteLive = false }));
    }

    [Fact]
    public void ContactsJoiningNotifiesOnlyWhenEnabled()
    {
        var settings = new DiscoverSettings { NotifyFavoriteLive = false, NotifyContactsInFavorite = true };
        var notifier = new FavoriteNotifier();
        notifier.Baseline("U-a:R-1", Live("U-a:R-1", contacts: 0));

        Assert.Single(notifier.Observe("U-a:R-1", Live("U-a:R-1", contacts: 2), T0, settings));
    }
}
