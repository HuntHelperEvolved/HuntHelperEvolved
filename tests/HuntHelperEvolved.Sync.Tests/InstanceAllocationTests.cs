using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public sealed class InstanceAllocationTests
{
    [Fact]
    public void InstanceResolutionMatchesPreviousRulesIncludingInvalidAndLegacyInstances()
    {
        var random = new Random(180);
        for (var n = 0; n < 200; n++)
        {
            var zones = Enumerable.Range(0, random.Next(20)).Select(_ => (uint)random.Next(15)).ToArray();
            var kills = Enumerable.Range(0, random.Next(20)).Select(_ => (uint)random.Next(15)).ToArray();
            var validKills = kills.Where(i => i <= 9).ToList();
            var highest = zones.Concat(validKills).Where(i => i <= 9).DefaultIfEmpty(0u).Max();
            var expected = highest == 0 ? new List<uint> { 0 } : Enumerable.Range(1, (int)highest).Select(i => (uint)i).ToList();
            if (highest > 0 && validKills.Contains(0)) expected.Insert(0, 0);
            Assert.Equal(expected, ARankInstances.Resolve(zones, kills));
        }
    }

    [Fact]
    public void OwnedInstanceListsMatchMetadataRulesWithoutReplacingTheList()
    {
        foreach (var metadata in new[] { false, true })
        foreach (var count in Enumerable.Range(0, 11))
        {
            var status = new SyncFaloopStatus { MetadataAt = metadata ? DateTime.UtcNow : null };
            status.ZoneInstances[1] = count;
            var input = new List<uint> { 3, 0, 2, 3, 2, 10 };
            var expected = status.CurrentInstances(1, input);
            Assert.Same(input, status.CurrentInstancesInPlace(1, input));
            Assert.Equal(expected, input);
        }
    }
}
