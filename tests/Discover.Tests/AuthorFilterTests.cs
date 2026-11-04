using Discover.Core.Lenses;
using Discover.Core.Sources;
using Xunit;

namespace Discover.Tests;

public class AuthorFilterTests
{
    [Fact]
    public void CreatedSortIsASearchSortSentToTheServer()
    {
        Assert.Equal(Discover.Core.Model.RecordSort.CreationDate, Discover.Core.Lenses.Sorting.ToRecordSort(Discover.Core.Lenses.SortKey.Created));
        Assert.True(Discover.Core.Lenses.Sorting.IsSupported(Discover.Core.Lenses.LensKind.Browse, Discover.Core.Lenses.SortKey.Created));
        Assert.False(Discover.Core.Lenses.Sorting.IsSupported(Discover.Core.Lenses.LensKind.Friends, Discover.Core.Lenses.SortKey.Created));
    }

    [Fact]
    public void UserIdsSearchByUser()
    {
        var query = RecordLenses.ByAuthor(RecordLenses.Browse(), "U-winter");
        Assert.Equal("U-winter", query.ByOwner);
        Assert.Equal(OwnerKind.User, query.OwnerType);
    }

    [Fact]
    public void GroupIdsSearchByGroup()
    {
        var query = RecordLenses.ByAuthor(RecordLenses.Browse(), "G-crew");
        Assert.Equal("G-crew", query.ByOwner);
        Assert.Equal(OwnerKind.Group, query.OwnerType);
    }
}
