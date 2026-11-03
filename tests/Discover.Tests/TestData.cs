using Discover.Core.Model;

namespace Discover.Tests;

/// <summary>Builders for snapshots. Defaults describe a healthy public session.</summary>
public static class TestData
{
    public static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    public static SessionSnapshot Session(
        string id,
        string? world = null,
        int active = 1,
        int joined = -1,
        int max = 10,
        AccessLevel access = AccessLevel.Anyone,
        int contacts = 0,
        string name = "",
        bool hidden = false,
        bool ended = false,
        bool expired = false,
        int ageMinutes = 30,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<SessionUserSnapshot>? users = null) => new()
    {
        SessionId = id,
        WorldKey = world,
        Name = name.Length > 0 ? name : id,
        ActiveUsers = active,
        JoinedUsers = joined < 0 ? active : joined,
        MaximumUsers = max,
        AccessLevel = access,
        PresentContacts = contacts,
        HideFromListing = hidden,
        HasEnded = ended,
        IsExpired = expired,
        SessionBeginTime = Now.AddMinutes(-ageMinutes),
        LastUpdate = Now,
        Tags = tags ?? [],
        Users = users ?? [],
    };

    public static RecordSnapshot Record(
        string key,
        string name = "",
        int visits = 0,
        IReadOnlyList<string>? tags = null,
        string description = "",
        string owner = "",
        bool featured = false,
        DateTime? published = null) => new()
    {
        Key = key,
        OwnerId = "U-owner",
        RecordId = key,
        Name = name.Length > 0 ? name : key,
        Visits = visits,
        Tags = tags ?? [],
        Description = description,
        OwnerName = owner,
        IsFeatured = featured,
        FirstPublishTime = published,
    };

    public static DiscoveryIndex IndexOf(params SessionSnapshot[] sessions)
    {
        var index = new DiscoveryIndex();
        index.LoadSessions(sessions);
        return index;
    }
}

/// <summary>A clock tests can move.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;

    public TestClock(DateTime? start = null) => _now = new DateTimeOffset(start ?? TestData.Now, TimeSpan.Zero);

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>A temp folder deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "galaxysieve-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
