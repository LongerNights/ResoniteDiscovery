using Discover.Core.Model;

namespace Discover.Tests;

public class ThumbnailPolicyTests
{
    [Theory]
    [InlineData("resdb:///abc.png")]
    [InlineData("https://example.com/a.png")]
    [InlineData("local://abc/def.png")]
    public void AcceptsEngineAndHttpsSchemes(string raw) => Assert.True(ThumbnailPolicy.TryAccept(raw, out _));

    [Theory]
    [InlineData("file:///C:/x.png")]
    [InlineData("file://host/share/x.png")]
    [InlineData("\\\\host\\share\\x.png")]
    [InlineData("http://example.com/a.png")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("not a uri")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsEverythingElse(string? raw) => Assert.False(ThumbnailPolicy.TryAccept(raw, out _));
}

public class SettingsSanitizeTests
{
    [Fact]
    public void ZeroAndNegativeValuesAreRaisedToTheFloors()
    {
        var s = (new Discover.Core.Settings.DiscoverSettings
        {
            AutoRefreshInterval = TimeSpan.Zero,
            ManualRefreshCooldown = TimeSpan.Zero,
            SessionChangesPerTick = 0,
            SnapshotIntervalScreenClosed = TimeSpan.Zero,
            MinWriteInterval = TimeSpan.FromSeconds(-1),
            RowsPerPage = 0,
        }).Sanitize();

        Assert.Equal(Discover.Core.Settings.DiscoverSettings.MinAutoRefresh, s.AutoRefreshInterval);
        Assert.Equal(Discover.Core.Settings.DiscoverSettings.MinManualCooldown, s.ManualRefreshCooldown);
        Assert.Equal(1, s.SessionChangesPerTick);
        Assert.Equal(TimeSpan.FromMinutes(1), s.SnapshotIntervalScreenClosed);
        Assert.Equal(TimeSpan.FromSeconds(1), s.MinWriteInterval);
        Assert.Equal(1, s.RowsPerPage);
    }

    [Fact]
    public void HugeValuesAreCappedAndDefaultsAreUnchanged()
    {
        var huge = new Discover.Core.Settings.DiscoverSettings { SessionChangesPerTick = int.MaxValue, HistoryRetention = TimeSpan.FromDays(9999) }.Sanitize();
        Assert.Equal(500, huge.SessionChangesPerTick);
        Assert.Equal(TimeSpan.FromDays(60), huge.HistoryRetention);

        var defaults = new Discover.Core.Settings.DiscoverSettings();
        Assert.Equal(defaults, defaults.Sanitize());
    }
}
