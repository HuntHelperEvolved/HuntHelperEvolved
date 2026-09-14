using System;
using System.Numerics;

namespace HuntHelperEvolved;

internal static class SpawnPointVisibility
{
    // Match the existing mapping tolerance, including its ambiguous-neighbour guard.
    internal static int? OccupiedPoint(SpawnPoint[] points, Vector2 position, SpawnRanks rank, float health)
    {
        if (!float.IsFinite(health) || health <= 0 || health > 100
            || !float.IsFinite(position.X) || !float.IsFinite(position.Y)) return null;
        var nearest = float.PositiveInfinity;
        var second = float.PositiveInfinity;
        var index = -1;
        for (var i = 0; i < points.Length; i++)
        {
            if (rank == SpawnRanks.None || (points[i].Ranks & rank) != rank) continue;
            var distance = Vector2.Distance(position, new(points[i].X, points[i].Y));
            if (distance < nearest) { second = nearest; nearest = distance; index = i; }
            else if (distance < second) second = distance;
        }
        return index < 0 || nearest > 2f || second - nearest < 0.35f ? null : index;
    }
}
