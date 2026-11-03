namespace Discover.Core.Model;

/// <summary>
/// Mirror of SkyFrost.Base.SessionAccessLevel. The adapter maps by name, not by value.
/// Names and order confirmed (docs/verification.md V17).
/// </summary>
public enum AccessLevel
{
    Private,
    LAN,
    Contacts,
    ContactsPlus,
    RegisteredUsers,
    Anyone,
}

/// <summary>Mirror of SkyFrost.Base.OutputDevice (confirmed in dumper batch 1).</summary>
public enum OutputDevice
{
    Unknown,
    Screen,
    VR,
    Camera,
}

/// <summary>Mirror of SkyFrost.Base.SearchSortParameter (confirmed in dumper batch 1).</summary>
public enum RecordSort
{
    CreationDate,
    LastUpdateDate,
    FirstPublishTime,
    TotalVisits,
    Name,
    Random,
}

/// <summary>Mirror of SkyFrost.Base.SearchSortDirection (confirmed in dumper batch 1).</summary>
public enum SortDirection
{
    Ascending,
    Descending,
}
