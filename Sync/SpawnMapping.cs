using System;
using System.Linq;
using System.Numerics;
namespace HuntHelperEvolved.Sync;

public static class SpawnMapping
{
    public static bool ReliableCycle(SyncSpawnZone? zone, SyncSRankStatus? status) =>
        zone?.SinceAt is not null && (zone.ResetBySnipe
            || status?.KilledAt == zone.SinceAt && status?.Uncertain == false);

    // Spawn tables contain rounded positions and idle marks can patrol around their origin.
    // Reject distant or ambiguous matches rather than eliminating a neighbouring point.
    public static int? Match(SpawnPoint[] points, Vector2 position, SpawnRanks rank) => Match(points, position, rank, 2f);

    /// <summary>Match discovery coordinates before any subsequently observed combat movement.</summary>
    public static int? MatchFirstSighting(SpawnPoint[] points, Vector2 position, SpawnRanks rank,
        uint currentHp, uint maxHp, bool inCombat)
    {
        if (maxHp == 0 || currentHp == 0 || currentHp > maxHp) return null;
        if (!inCombat && currentHp == maxHp) return Match(points, position, rank);
        // A/B marks can be discovered fighting after patrolling around their
        // spawn. Apply the same distance and ambiguity checks as idle sightings.
        // An S rank's original spawn is not inferred from a pulled position.
        return rank is SpawnRanks.A or SpawnRanks.B ? Match(points, position, rank) : null;
    }

    private static int? Match(SpawnPoint[] points, Vector2 position, SpawnRanks rank, float maxDistance)
    {
        if (rank == SpawnRanks.None || !float.IsFinite(position.X) || !float.IsFinite(position.Y)) return null;
        var candidates = points.Select((p, i) => (Point:p, Index:i, Distance:Vector2.Distance(position,new(p.X,p.Y))))
            .Where(p => p.Point.Ranks.HasFlag(rank) && float.IsFinite(p.Distance)).OrderBy(p => p.Distance).Take(2).ToArray();
        if (candidates.Length == 0 || candidates[0].Distance > maxDistance) return null;
        if (candidates.Length > 1 && candidates[1].Distance - candidates[0].Distance < 0.35f) return null;
        return candidates[0].Index;
    }
    public static int? ConfirmedPoint(SpawnPoint[] points, SyncSpawnZone zone, bool reliableCycle)
    {
        if (zone.SCurrentIndex is { } observed && observed >= 0 && observed < points.Length
            && points[observed].Ranks.HasFlag(SpawnRanks.S)) return observed;
        if (!reliableCycle || zone.SinceAt is null) return null;
        var possible = points.Select((p,i)=>(p,i)).Where(x=>x.p.Ranks.HasFlag(SpawnRanks.S)&&!zone.IsRuledOut(x.i)).Take(2).ToArray();
        return possible.Length == 1 ? possible[0].i : null;
    }
    // The centroid selects the aetheryte with the lowest mean squared map distance
    // to the candidates. This is a travel estimate, never a reported mark location.
    public static Vector2 TravelEstimate(SpawnPoint[] points, SyncSpawnZone? zone, bool reliableCycle)
    {
        if (zone is not null && ConfirmedPoint(points, zone, reliableCycle) is { } confirmed)
            return new(points[confirmed].X, points[confirmed].Y);

        var all = points.Select((p, i) => (Point: p, Index: i))
            .Where(x => x.Point.Ranks.HasFlag(SpawnRanks.S)).ToArray();
        var candidates = reliableCycle && zone?.SinceAt is not null
            ? all.Where(x => !zone.IsRuledOut(x.Index)).ToArray() : all;
        // An empty/inconsistent mapping must not prevent a spawn attempt.
        if (candidates.Length == 0) candidates = all;
        if (candidates.Length == 0) return new(21.5f, 21.5f);
        return new(candidates.Average(x => x.Point.X), candidates.Average(x => x.Point.Y));
    }

}
