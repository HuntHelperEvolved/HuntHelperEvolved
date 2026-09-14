using System.Numerics;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public sealed class SpawnPointVisibilityTests
{
    [Fact]
    public void HidesOnlyLivingNearbyMatchingPointAndRejectsAmbiguousNeighbours()
    {
        var points = new[] { new SpawnPoint(10, 10, SpawnRanks.A), new SpawnPoint(20, 20, SpawnRanks.B) };
        Assert.Equal(0, SpawnPointVisibility.OccupiedPoint(points, new(12, 10), SpawnRanks.A, 100));
        Assert.Null(SpawnPointVisibility.OccupiedPoint(points, new(12.01f, 10), SpawnRanks.A, 100));
        Assert.Null(SpawnPointVisibility.OccupiedPoint(points, new(10, 10), SpawnRanks.B, 100));
        foreach (var health in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Assert.Null(SpawnPointVisibility.OccupiedPoint(points, new(10, 10), SpawnRanks.A, health));
        Assert.Null(SpawnPointVisibility.OccupiedPoint(points, new(float.NaN, 10), SpawnRanks.A, 100));
        var closePoints = new[] { points[0], new SpawnPoint(10.2f, 10, SpawnRanks.A) };
        Assert.Null(SpawnPointVisibility.OccupiedPoint(closePoints, new(10, 10), SpawnRanks.A, 100));
    }

    [Fact]
    public void VisibilityMatchingAgreesWithExistingSpawnMappingTolerance()
    {
        var random = new Random(18);
        var points = Enumerable.Range(0, 80).Select(i => new SpawnPoint(
            (float)random.NextDouble() * 40, (float)random.NextDouble() * 40,
            i % 2 == 0 ? SpawnRanks.A | SpawnRanks.S : SpawnRanks.B)).ToArray();
        foreach (var rank in new[] { SpawnRanks.A, SpawnRanks.B, SpawnRanks.S })
        for (var i = 0; i < 100; i++)
        {
            var position = new Vector2((float)random.NextDouble() * 40, (float)random.NextDouble() * 40);
            Assert.Equal(SpawnMapping.Match(points, position, rank), SpawnPointVisibility.OccupiedPoint(points, position, rank, 100));
        }
    }
}
