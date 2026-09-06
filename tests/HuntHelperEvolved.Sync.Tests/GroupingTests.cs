using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class GroupingTests
{
    [Fact]
    public void RemoteBlockOrderIsValidRegardlessOfLocalPreference()
    {
        Assert.True(SharedRouteGrouping.HasContiguousBlocks(new[]{"Dawntrail","Dawntrail","Endwalker"}));
        Assert.True(SharedRouteGrouping.HasContiguousBlocks(new[]{"Endwalker","Dawntrail","Dawntrail"}));
        Assert.False(SharedRouteGrouping.HasContiguousBlocks(new[]{"Dawntrail","Endwalker","Dawntrail"}));
    }
}
