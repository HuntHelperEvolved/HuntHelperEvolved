using HuntHelperEvolved.Sync;
using Xunit;

namespace HuntHelperEvolved.Tests;

public sealed class ARankZoneInstancesTests
{
    [Fact]
    public void IndexedInstancesMatchZoneScansWithoutMixingWorlds()
    {
        var observations = ExpansionData.ModelIdToMark.Keys.Select((id, i) =>
            (NameId: id, TerritoryId: 0u, World: (uint)(1 + i % 2), Instance: (uint)(i % 3)))
            .Concat(SRankTimerData.All.Select((t, i) =>
                (NameId: 0u, TerritoryId: t.TerritoryId, World: (uint)(1 + i % 2), Instance: (uint)(1 + i % 3))))
            .ToList();
        var index = new ARankZoneInstances();
        foreach (var row in observations)
        {
            index.Add(row.NameId, row.TerritoryId, row.World, row.Instance);
            index.Add(row.NameId, row.TerritoryId, row.World, row.Instance);
        }
        foreach (var zone in ExpansionData.ModelIdToMark.Values.Select(m => m.Location).Distinct())
        foreach (var world in new uint[] { 1, 2, 3 })
        {
            var marks = ExpansionData.ModelIdToMark.Where(m => m.Value.Location == zone).Select(m => m.Key).ToHashSet();
            var territories = SRankTimerData.All.Where(t => t.Zone == zone).Select(t => t.TerritoryId).ToHashSet();
            var expected = observations.Where(r => r.World == world && (marks.Contains(r.NameId) || territories.Contains(r.TerritoryId)))
                .Select(r => r.Instance).Distinct().Order().ToArray();
            Assert.Equal(expected, index.Get(world, zone).Order().ToArray());
        }
    }
}
