using Discover.Core.Persistence;
using Discover.Core.Settings;

namespace Discover.Tests;

public class PersistenceTests
{
    [Fact]
    public void SaveIsAtomicAndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        var store = new JsonFileStore(dir.Path);

        store.Save("f.json", new FavoritesFile { Items = [new FavoriteEntry { Key = "W1", Name = "One" }] });
        store.Save("f.json", new FavoritesFile { Items = [new FavoriteEntry { Key = "W2", Name = "Two" }] });

        Assert.Equal(["f.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
        Assert.Equal("W2", store.Load("f.json", () => new FavoritesFile()).Items.Single().Key);
    }

    [Fact]
    public void MissingFileGivesDefaultsWithoutRecovery()
    {
        using var dir = new TempDir();
        var store = new JsonFileStore(dir.Path);

        var value = store.Load("nope.json", () => new FavoritesFile { Version = 7 });

        Assert.Equal(7, value.Version);
        Assert.Empty(store.Recovered);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    public void CorruptFileIsRenamedToBadAndReplacedWithDefaults(string content)
    {
        using var dir = new TempDir();
        var messages = new List<string>();
        var store = new JsonFileStore(dir.Path, messages.Add);
        File.WriteAllText(store.PathOf("f.json"), content);

        var value = store.Load("f.json", () => new FavoritesFile { Version = 7 });

        Assert.Equal(7, value.Version);
        Assert.False(File.Exists(store.PathOf("f.json")));
        Assert.Equal(content, File.ReadAllText(store.PathOf("f.json.bad")));
        Assert.Equal(["f.json"], store.Recovered);
        Assert.Single(messages);
        Assert.StartsWith("[Discover]", messages[0]);

        store.Save("f.json", value); // the next save works normally
        Assert.True(File.Exists(store.PathOf("f.json")));
    }

    [Fact]
    public void AccountsAreSeparatedOnDisk()
    {
        using var dir = new TempDir();
        var a = new UserLibraryStore(AccountPaths.AccountDirectory(dir.Path, "U-alice"));
        var b = new UserLibraryStore(AccountPaths.AccountDirectory(dir.Path, "U-bob"));

        var libA = a.LoadLibrary();
        libA.Star("W1", "One");
        a.Save(libA, LibraryFile.Favorites);

        Assert.NotEqual(a.AccountDirectory, b.AccountDirectory);
        Assert.True(a.LoadLibrary().IsStarred("W1"));
        Assert.False(b.LoadLibrary().IsStarred("W1"));
        Assert.EndsWith(Path.Combine("GalaxySieve", "U-alice"), a.AccountDirectory);
    }

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("../../evil")]
    [InlineData("U-a/b")]
    [InlineData("C:\\x")]
    public void UserIdsCannotEscapeTheFolder(string userId)
    {
        using var dir = new TempDir();

        var account = AccountPaths.AccountDirectory(dir.Path, userId);

        Assert.Equal(Path.Combine(dir.Path, "GalaxySieve"), Path.GetDirectoryName(account));
    }

    [Fact]
    public void BlankUserIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => AccountPaths.AccountDirectory("root", " "));
    }

    [Fact]
    public void LibrarySurvivesARestart()
    {
        using var dir = new TempDir();
        var clock = new TestClock();
        var store = new UserLibraryStore(dir.Path);
        var lib = store.LoadLibrary(clock);
        var games = lib.CreateList("Games")!;
        lib.Star("W1", "One");
        lib.AddToList(games.Id, "W1", "One");
        lib.AddToList(games.Id, "W2", "Two");
        lib.AddTag("W1", "Chill");
        lib.RecordOpened("W2", "Two");
        foreach (var file in Enum.GetValues<LibraryFile>())
        {
            store.Save(lib, file);
        }

        var reloaded = new UserLibraryStore(dir.Path).LoadLibrary(clock);

        Assert.True(reloaded.IsStarred("W1"));
        Assert.Equal("Games", Assert.Single(reloaded.Lists).Name);
        Assert.Equal(["W1", "W2"], reloaded.Lists[0].Members.Select(m => m.Key));
        Assert.Equal(["chill"], reloaded.TagsFor("W1"));
        Assert.Equal("W2", Assert.Single(reloaded.Recent).Key);
    }

    [Fact]
    public void SettingsRoundTripIncludingTimeSpansAndEnums()
    {
        using var dir = new TempDir();
        var store = new UserLibraryStore(dir.Path);
        var settings = new DiscoverSettings
        {
            DefaultLens = Discover.Core.Lenses.LensKind.Friends,
            LiveLensesOnly = true,
            Formulas = new FormulaSettings { TrendingWindow = TimeSpan.FromHours(3), GemSmoothing = 40 },
        };

        store.SaveSettings(settings);

        Assert.Equal(settings, store.LoadSettings());
        Assert.Contains("\"friends\"", File.ReadAllText(Path.Combine(dir.Path, "settings.json")).ToLowerInvariant());
    }

    [Fact]
    public void SaverWritesAtMostOncePerFilePerInterval()
    {
        using var dir = new TempDir();
        var clock = new TestClock();
        var store = new UserLibraryStore(dir.Path);
        var lib = store.LoadLibrary(clock);
        var saver = new LibrarySaver(store, lib, TimeSpan.FromSeconds(5), clock);

        lib.Star("W1", "One");
        Assert.Equal(1, saver.Tick());          // first write is immediate

        lib.Star("W2", "Two");
        Assert.Equal(0, saver.Tick());          // inside the interval: held back
        Assert.Equal(1, saver.PendingFiles);

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, saver.Tick());
        Assert.Equal(2, store.LoadLibrary(clock).Favorites.Count);

        lib.Star("W3", "Three");
        Assert.Equal(1, saver.Flush());         // shutdown ignores the interval
        Assert.Equal(0, saver.PendingFiles);
    }

    [Fact]
    public void SaverTracksFilesIndependently()
    {
        using var dir = new TempDir();
        var clock = new TestClock();
        var store = new UserLibraryStore(dir.Path);
        var lib = store.LoadLibrary(clock);
        var saver = new LibrarySaver(store, lib, TimeSpan.FromSeconds(5), clock);

        lib.Star("W1", "One");
        saver.Tick();
        lib.RecordOpened("W1", "One");

        Assert.Equal(1, saver.Tick()); // recent.json has not been written yet, so it is not held back
    }

    [Fact]
    public void ExportWritesToTheFixedExportsFolder()
    {
        using var dir = new TempDir();
        var store = new UserLibraryStore(dir.Path);
        var lib = store.LoadLibrary();
        lib.Star("W1", "One");

        var path = store.WriteExport(lib);

        Assert.Equal(Path.Combine(dir.Path, "exports", "galaxy-sieve-export.json"), path);
        Assert.Equal("W1", Assert.Single(store.ReadExport()!.Favorites).Key);
        Assert.Null(new UserLibraryStore(Path.Combine(dir.Path, "elsewhere")).ReadExport());
    }
}
