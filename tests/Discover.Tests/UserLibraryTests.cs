using Discover.Core.Persistence;

namespace Discover.Tests;

public class UserLibraryTests
{
    private static UserLibrary New(TestClock? clock = null) => new(clock ?? new TestClock());

    [Fact]
    public void StarIsIdempotentAndKeepsTheFirstDate()
    {
        var clock = new TestClock();
        var lib = New(clock);

        lib.Star("W1", "One");
        var first = lib.Favorites.Single().StarredAt;
        clock.Advance(TimeSpan.FromDays(2));
        lib.Star("w1", "One again");

        Assert.Equal(first, Assert.Single(lib.Favorites).StarredAt);
        Assert.True(lib.Unstar("W1"));
        Assert.False(lib.IsStarred("W1"));
        Assert.False(lib.Unstar("W1"));
    }

    [Theory]
    [InlineData("S:abc")]
    [InlineData("")]
    [InlineData("  ")]
    public void OnlyWorldsCanBeSaved(string key)
    {
        var lib = New();
        var list = lib.CreateList("L")!;

        Assert.False(lib.Star(key, "x"));
        Assert.False(lib.AddToList(list.Id, key, "x"));
        Assert.False(lib.AddTag(key, "t"));
        lib.RecordOpened(key, "x");

        Assert.Empty(lib.Favorites);
        Assert.Empty(lib.Lists[0].Members);
        Assert.Empty(lib.Recent);
    }

    [Fact]
    public void ListsCanBeCreatedRenamedReorderedAndDeleted()
    {
        var lib = New();
        var a = lib.CreateList("  Games  ")!;
        var b = lib.CreateList("Art")!;
        var c = lib.CreateList("Chill")!;

        Assert.Equal("Games", a.Name);
        Assert.Null(lib.CreateList("   "));
        Assert.Equal([a.Id, b.Id, c.Id], lib.Lists.Select(l => l.Id));

        lib.ReorderLists([c.Id, a.Id]);
        Assert.Equal([c.Id, a.Id, b.Id], lib.Lists.Select(l => l.Id));

        Assert.True(lib.RenameList(b.Id, "Art 2"));
        Assert.False(lib.RenameList(b.Id, " "));
        Assert.False(lib.RenameList("nope", "x"));
        Assert.True(lib.DeleteList(a.Id));
        Assert.Equal(["Chill", "Art 2"], lib.Lists.Select(l => l.Name));
    }

    [Fact]
    public void AWorldCanBeInManyLists()
    {
        var lib = New();
        var a = lib.CreateList("A")!;
        var b = lib.CreateList("B")!;

        lib.AddToList(a.Id, "W1", "One");
        lib.AddToList(b.Id, "W1", "One");
        lib.AddToList(b.Id, "W1", "One"); // duplicate ignored

        Assert.Equal([a.Id, b.Id], lib.ListIdsContaining("w1"));
        Assert.Single(lib.GetList(b.Id)!.Members);
        Assert.True(lib.RemoveFromList(a.Id, "W1"));
        Assert.Equal([b.Id], lib.ListIdsContaining("W1"));
        Assert.False(lib.AddToList("missing", "W1", "One"));
    }

    [Fact]
    public void TagsAreNormalizedAndLimited()
    {
        var lib = New();

        Assert.True(lib.AddTag("W1", "  Chill "));
        Assert.True(lib.AddTag("W1", "CHILL"));
        Assert.False(lib.AddTag("W1", " "));
        Assert.Equal(["chill"], lib.TagsFor("W1"));
        Assert.True(lib.HasTagNamed("Chill"));

        for (var i = 0; i < UserLibrary.MaxTagsPerWorld; i++)
        {
            lib.AddTag("W2", "t" + i);
        }

        Assert.False(lib.AddTag("W2", "one-too-many"));
        Assert.True(lib.RemoveTag("W1", "chill"));
        Assert.False(lib.RemoveTag("W1", "chill"));
        Assert.Empty(lib.TagsFor("W1"));
    }

    [Fact]
    public void RecentIsNewestFirstDeduplicatedAndCapped()
    {
        var clock = new TestClock();
        var lib = New(clock);
        for (var i = 0; i < 60; i++)
        {
            lib.RecordOpened("W" + i, "n");
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        lib.RecordOpened("W10", "n"); // reopening moves it to the front

        Assert.Equal(UserLibrary.MaxRecent, lib.Recent.Count);
        Assert.Equal("W10", lib.Recent[0].Key);
        Assert.Equal(1, lib.Recent.Count(r => r.Key == "W10"));
        Assert.DoesNotContain(lib.Recent, r => r.Key == "W0");
    }

    [Fact]
    public void ChangedReportsTheFileThatNeedsSaving()
    {
        var lib = New();
        var seen = new List<LibraryFile>();
        lib.Changed += seen.Add;

        lib.Star("W1", "One");
        lib.CreateList("L");
        lib.AddTag("W1", "t");
        lib.RecordOpened("W1", "One");
        lib.Star("W1", "One"); // no change

        Assert.Equal([LibraryFile.Favorites, LibraryFile.Lists, LibraryFile.Tags, LibraryFile.Recent], seen);
    }

    [Fact]
    public void ExportThenImportIntoAnEmptyLibraryIsIdentical()
    {
        using var dir = new TempDir();
        var source = New();
        var games = source.CreateList("Games")!;
        source.CreateList("Art");
        source.Star("W1", "One");
        source.Star("W2", "Two");
        source.AddToList(games.Id, "W1", "One");
        source.AddToList(games.Id, "W3", "Three");
        source.AddTag("W1", "chill");
        source.AddTag("W1", "cozy");
        source.AddTag("W3", "loud");

        // through the real file, as F23's acceptance test does
        var store = new UserLibraryStore(dir.Path);
        store.WriteExport(source);
        var target = New();
        target.Import(store.ReadExport()!);

        Assert.Equivalent(source.Export().Favorites, target.Export().Favorites);
        Assert.Equivalent(source.Export().Lists, target.Export().Lists);
        Assert.Equivalent(source.Export().Tags, target.Export().Tags);
    }

    [Fact]
    public void ImportMergesAndNeverDeletes()
    {
        var lib = New();
        var list = lib.CreateList("Mine")!;
        lib.Star("W-local", "Local");
        lib.AddToList(list.Id, "W-local", "Local");
        lib.AddTag("W-local", "keep");

        var incoming = new LibraryExport
        {
            Favorites = [new FavoriteEntry { Key = "W-local", Name = "Local", StarredAt = new DateTime(2020, 1, 1) }, new FavoriteEntry { Key = "W-new", Name = "New" }, new FavoriteEntry { Key = "S:standalone", Name = "skip" }],
            Lists =
            [
                new SavedList { Id = list.Id, Name = "Mine", Members = [new ListMember { Key = "W-more", Name = "More" }] },
                new SavedList { Id = "other", Name = "Other", Members = [new ListMember { Key = "W-new", Name = "New" }] },
            ],
            Tags = new(StringComparer.OrdinalIgnoreCase) { ["W-local"] = ["extra", "KEEP"], ["S:x"] = ["no"] },
        };

        var result = lib.Import(incoming);

        Assert.Equal(new ImportResult(1, 1, 2, 1), result);
        Assert.Equal(2, lib.Favorites.Count);
        Assert.NotEqual(new DateTime(2020, 1, 1), lib.Favorites.First(f => f.Key == "W-local").StarredAt); // existing date kept
        Assert.Equal(["W-local", "W-more"], lib.GetList(list.Id)!.Members.Select(m => m.Key));
        Assert.Equal(["keep", "extra"], lib.TagsFor("W-local"));
        Assert.Equal(2, lib.Lists.Count);
    }

    [Fact]
    public void ImportRejectsANewerFormat()
    {
        var lib = New();

        Assert.Throws<InvalidDataException>(() => lib.Import(new LibraryExport { FormatVersion = LibraryExport.CurrentFormatVersion + 1 }));
        Assert.Throws<InvalidDataException>(() => lib.Import(new LibraryExport { FormatVersion = 0 }));
    }

    [Fact]
    public void ImportingTwiceChangesNothingTheSecondTime()
    {
        var source = New();
        source.Star("W1", "One");
        source.AddTag("W1", "t");
        var export = source.Export();
        var target = New();

        target.Import(export);
        var second = target.Import(export);

        Assert.Equal(new ImportResult(0, 0, 0, 0), second);
    }
}
