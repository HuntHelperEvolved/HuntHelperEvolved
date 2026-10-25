using System;
using System.Numerics;

namespace HuntHelperEvolved;

internal static class SpawnPointVisibility
{
    // Train rows are managed data and remain available after leaving sight.
    // Custom flags are not hunt marks, even when placed on a spawn point.
    internal static int? TrainPoint(SpawnPoint[] points, DetectedMark mark,
        uint territory, uint world, uint instance)
    {
        if (world == 0 || mark.WorldId != world || mark.TerritoryId != territory
            || mark.Instance != instance || mark.IsCustom || mark.Dead || mark.SnipedAtUtc is not null
            || ExpansionData.Lookup(mark.NameId) is null) return null;
        return OccupiedPoint(points, mark.MapPosition, SpawnRanks.A, 100);
    }

    // Preserve S-rank information around the train-coloured fill.
    internal static string TextureKey(string normal, bool inTrain) => !inTrain ? normal
        : normal is "scand" or "sconfirmed" ? "train-scand" : "train";

    // Match the existing mapping tolerance, including its ambiguous-neighbour guard.
    internal static int? OccupiedPoint(SpawnPoint[] points, Vector2 position, SpawnRanks rank, float health)
    {
        if (!float.IsFinite(health) || health <= 0 || health > 100) return null;
        return Sync.SpawnMapping.Match(points, position, rank);
    }
}
