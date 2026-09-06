using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class GroupingTests
{
    [Fact]
    public void AppendedScoutMarksJoinTheirBlockAndRepeatedGroupingDoesNotMoveThem()
    {
        var route = new[] { (Id:1, Expansion:"Dawntrail"), (Id:2, Expansion:"Endwalker"),
            (Id:3, Expansion:"Dawntrail"), (Id:4, Expansion:"Shadowbringers"), (Id:5, Expansion:"Endwalker") };
        var grouped = SharedRouteGrouping.GroupInRouteOrder(route, m => m.Expansion);
        Assert.Equal(new[] {1,3,2,5,4}, grouped.Select(m => m.Id));
        Assert.Equal(grouped, SharedRouteGrouping.GroupInRouteOrder(grouped, m => m.Expansion));
        Assert.True(SharedRouteGrouping.HasContiguousBlocks(grouped.Select(m => m.Expansion)));
        var afterAnotherScout = grouped.Append((Id:6, Expansion:"Dawntrail"));
        Assert.Equal(new[] {1,3,6,2,5,4}, SharedRouteGrouping.GroupInRouteOrder(afterAnotherScout, m => m.Expansion).Select(m => m.Id));
    }

    [Fact]
    public void RemoteBlockOrderIsValidRegardlessOfLocalPreference()
    {
        Assert.True(SharedRouteGrouping.HasContiguousBlocks(new[]{"Dawntrail","Dawntrail","Endwalker"}));
        Assert.True(SharedRouteGrouping.HasContiguousBlocks(new[]{"Endwalker","Dawntrail","Dawntrail"}));
        Assert.False(SharedRouteGrouping.HasContiguousBlocks(new[]{"Dawntrail","Endwalker","Dawntrail"}));
    }
}
