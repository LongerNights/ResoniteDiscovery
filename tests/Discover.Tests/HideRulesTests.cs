using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Settings;

namespace Discover.Tests;

public class HideRulesTests
{
    private static DiscoveryEntry World(string key, string name, string owner = "U-owner") => new()
    {
        Key = key,
        Record = TestData.Record(key, name: name) with { OwnerId = owner },
    };

    [Fact]
    public void NothingHiddenByDefault()
    {
        Assert.False(HideRules.IsHidden(World("K-1", "Plaza"), new DiscoverSettings()));
    }

    [Fact]
    public void HidesByWorldAuthorAndWord()
    {
        var settings = HideRules.HideWord(HideRules.HideAuthor(HideRules.HideWorld(new DiscoverSettings(), "K-1"), "U-bad"), "Spam");

        Assert.True(HideRules.IsHidden(World("K-1", "Plaza"), settings));
        Assert.True(HideRules.IsHidden(World("K-2", "Fine", "U-bad"), settings));
        Assert.True(HideRules.IsHidden(World("K-3", "Free SPAM here"), settings));
        Assert.False(HideRules.IsHidden(World("K-4", "Plaza 2"), settings));
    }

    [Fact]
    public void RestoreAllClearsEverythingAndDuplicatesAreIgnored()
    {
        var once = HideRules.HideWorld(new DiscoverSettings(), "K-1");
        Assert.Same(once, HideRules.HideWorld(once, "k-1"));
        Assert.Same(once, HideRules.HideWord(once, "   "));

        var cleared = HideRules.RestoreAll(HideRules.HideWord(once, "x"));
        Assert.True(HideRules.IsEmpty(cleared));
    }
}
