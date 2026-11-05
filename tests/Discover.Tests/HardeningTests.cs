using Discover.Core.Persistence;

namespace Discover.Tests;

public class HardeningTests
{
    [Fact]
    public void ASaverWhoseWritesFailStaysPendingAndDoesNotThrow()
    {
        using var dir = new TempDir();
        var blocker = Path.Combine(dir.Path, "not-a-folder");
        File.WriteAllText(blocker, "x"); // the "account folder" is a file, so every save fails
        var clock = new TestClock();
        var store = new UserLibraryStore(blocker);
        var lib = store.LoadLibrary(clock);
        var messages = new List<string>();
        var saver = new LibrarySaver(store, lib, TimeSpan.FromSeconds(5), clock, messages.Add);

        lib.Star("W1", "One");
        Assert.Equal(0, saver.Tick());
        Assert.Equal(1, saver.PendingFiles);
        Assert.Single(messages);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, saver.Tick());          // inside the retry delay: not even attempted
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, saver.Tick());          // attempted, failed again, logged only once
        Assert.Single(messages);
        Assert.Equal(0, saver.Flush());
    }

    [Fact]
    public void NullCollectionsInTheFilesAreTreatedAsEmpty()
    {
        var lib = new UserLibrary(null, new FavoritesFile { Items = null! }, new ListsFile { Lists = null! }, new TagsFile { Tags = null! }, new RecentFile { Items = null!, Visited = null! });
        Assert.Empty(lib.Favorites);
        Assert.Empty(lib.Lists);
        Assert.False(lib.IsVisited("A:b"));
        Assert.True(lib.AddTag("A:b", "x"));
    }

    [Fact]
    public void AnImportWithNullCollectionsDoesNotThrow()
    {
        var lib = new UserLibrary();
        var result = lib.Import(new LibraryExport
        {
            Favorites = null!,
            Tags = null!,
            Lists = [new SavedList { Id = "l1", Name = "L", Members = null! }],
        });
        Assert.Equal(1, result.ListsAdded);
    }

    [Fact]
    public void AnImportIsCappedAndDeduplicated()
    {
        var lib = new UserLibrary();
        var favorites = Enumerable.Range(0, UserLibrary.MaxImportFavorites + 500)
            .Select(i => new FavoriteEntry { Key = $"U:{i}", Name = "n" })
            .Append(new FavoriteEntry { Key = "u:0", Name = "dup" })
            .ToList();
        var result = lib.Import(new LibraryExport { Favorites = favorites });
        Assert.Equal(UserLibrary.MaxImportFavorites, result.FavoritesAdded);
        Assert.Equal(UserLibrary.MaxImportFavorites, lib.Favorites.Count);
    }

    [Fact]
    public void AnImportFromANewerVersionIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => new UserLibrary().Import(new LibraryExport { FormatVersion = 99 }));
    }
}

public class HistoryDownsampleTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string Key = "U-a:R-1";

    [Fact]
    public void OldSamplesKeepTheBusiestPerHourAndRecentOnesAreUntouched()
    {
        var log = new Discover.Core.History.HistoryLog();
        for (var m = 0; m < 120; m += 5) // two old hours, every 5 minutes
        {
            log.RecordLive(Key, Now.AddDays(-3).AddMinutes(m), 1 + (m == 40 ? 9 : 0));
        }

        var recent = Enumerable.Range(0, 6).Select(i => Now.AddMinutes(-i * 10)).ToList();
        foreach (var t in recent)
        {
            log.RecordLive(Key, t, 3);
        }

        log.Downsample(Now, TimeSpan.FromHours(36));

        var samples = log.LiveSamples(Key);
        Assert.Equal(2 + recent.Count, samples.Count);
        Assert.Contains(samples, s => s.LiveUsers == 10);
        Assert.Equal(recent.Count, samples.Count(s => s.Utc >= Now.AddHours(-36)));
        Assert.Equal(samples.OrderBy(s => s.Utc).ToList(), samples.ToList());
    }

    [Fact]
    public void FormulaResultsOnRecentWindowsAreUnchanged()
    {
        var log = new Discover.Core.History.HistoryLog();
        for (var h = 0; h < 40; h++)
        {
            log.RecordLive(Key, Now.AddHours(-h).AddMinutes(-1), 2 + h % 3);
            log.RecordLive(Key, Now.AddHours(-h).AddMinutes(-31), 2 + h % 2);
        }

        var f = new Discover.Core.Settings.FormulaSettings();
        double? Rising() => Discover.Core.History.HistoryFormulas.Rising(log.LiveSamples(Key), Now, f);
        double? Trend() => Discover.Core.History.HistoryFormulas.Trending(log.LiveSamples(Key), Now, f);
        var (r, t) = (Rising(), Trend());

        log.Downsample(Now, f.RisingRecentWindow + f.RisingPriorWindow + TimeSpan.FromHours(6));

        Assert.Equal(r, Rising());
        Assert.Equal(t, Trend());
    }
}

public class LibraryIndexTests
{
    [Fact]
    public void LookupCachesFollowEveryKindOfChange()
    {
        var lib = new UserLibrary();
        lib.Star("U:a", "A");
        Assert.True(lib.IsStarred("u:A"));
        lib.Unstar("U:a");
        Assert.False(lib.IsStarred("U:a"));

        var list = lib.CreateList("L")!;
        lib.AddToList(list.Id, "U:b", "B");
        Assert.Equal([list.Id], lib.ListIdsContaining("u:b"));
        lib.RemoveFromList(list.Id, "U:b");
        Assert.Empty(lib.ListIdsContaining("U:b"));

        lib.AddToList(list.Id, "U:c", "C");
        lib.DeleteList(list.Id);
        Assert.Empty(lib.ListIdsContaining("U:c"));

        var export = new LibraryExport
        {
            Favorites = [new FavoriteEntry { Key = "U:d", Name = "D" }],
            Lists = [new SavedList { Id = "x", Name = "X", Members = [new ListMember { Key = "U:d", Name = "D" }] }],
        };
        lib.Import(export);
        Assert.True(lib.IsStarred("U:d"));
        Assert.Equal(["x"], lib.ListIdsContaining("U:d"));
    }
}
