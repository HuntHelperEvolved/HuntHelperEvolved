using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class GroupingTests
{
    [Fact]
    public void WorldsKeepRouteOrderAndEachWorldKeepsItsOwnExpansionOrder()
    {
        var route = new[]
        {
            (Id: 1, World: 63u, Expansion: "Dawntrail"),
            (Id: 2, World: 37u, Expansion: "Endwalker"),
            (Id: 3, World: 63u, Expansion: "Endwalker"),
            (Id: 4, World: 37u, Expansion: "Dawntrail"),
            (Id: 5, World: 63u, Expansion: "Dawntrail"),
            (Id: 6, World: 37u, Expansion: "Endwalker"),
        };
        var grouped = SharedRouteGrouping.GroupByWorldInRouteOrder(route, m => m.World, m => m.Expansion);
        Assert.Equal(new[] { 1, 5, 3, 2, 6, 4 }, grouped.Select(m => m.Id));
        Assert.Equal(grouped, SharedRouteGrouping.GroupByWorldInRouteOrder(grouped, m => m.World, m => m.Expansion));

        var withNewSightings = grouped.Append((Id: 7, World: 63u, Expansion: "Endwalker"))
            .Append((Id: 8, World: 37u, Expansion: "Dawntrail"));
        Assert.Equal(new[] { 1, 5, 3, 7, 2, 6, 4, 8 },
            SharedRouteGrouping.GroupByWorldInRouteOrder(withNewSightings, m => m.World, m => m.Expansion).Select(m => m.Id));
    }

    [Fact]
    public void DisablingExpansionGroupingPreservesRowOrderWithinEachWorldIncludingUnknownWorld()
    {
        var route = new[]
        {
            (Id: 1, World: 0u, Expansion: "Other"),
            (Id: 2, World: 37u, Expansion: "Dawntrail"),
            (Id: 3, World: 37u, Expansion: "Endwalker"),
            (Id: 4, World: 0u, Expansion: "Dawntrail"),
            (Id: 5, World: 37u, Expansion: "Dawntrail"),
        };
        Assert.Equal(new[] { 1, 4, 2, 3, 5 },
            SharedRouteGrouping.GroupByWorldInRouteOrder(route, m => m.World, m => m.Expansion, false).Select(m => m.Id));
    }

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
