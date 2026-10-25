using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace HuntHelperEvolved.Sync;

public static class SpawnMapping
{
    // Table indices are shared and persisted. Keep them stable while treating
    // exact duplicate points as one physical point, represented by its first index.
    // Rank flags must also agree so the shared index remains eligible for that rank.
    public static IEnumerable<(SpawnPoint Point, int Index)> PhysicalPoints(SpawnPoint[] points,
        SpawnRanks rank = SpawnRanks.None) => points.Select((point, index) => (Point: point, Index: index))
        .DistinctBy(p => p.Point)
        .Where(p => rank == SpawnRanks.None || p.Point.Ranks.HasFlag(rank));

    public static bool SamePoint(SpawnPoint[] points, int? first, int? second) =>
        first is { } a && second is { } b && a >= 0 && b >= 0 && a < points.Length && b < points.Length
        && points[a] == points[b];

    public static int? CanonicalIndex(SpawnPoint[] points, int? index)
    {
        if (index is not { } value || value < 0 || value >= points.Length) return null;
        for (var i = 0; i < value; i++)
            if (SamePoint(points, i, value)) return i;
        return value;
    }

    public static IEnumerable<SyncEliminatedPoint> EvidenceFor(SpawnPoint[] points, SyncSpawnZone? zone, int index) =>
        zone is null ? [] : zone.Eliminated.Where(e => SamePoint(points, e.Index, index));

    public static bool IsRuledOut(SpawnPoint[] points, SyncSpawnZone? zone, int index) =>
        SamePoint(points, zone?.LastSDeathIndex, index) || EvidenceFor(points, zone, index).Any();

    // Older clients may have excluded either (or both) aliases. One undo clears
    // their manual evidence without removing an automatic sighting.
    public static int[] ManualUpdateIndices(SpawnPoint[] points, SyncSpawnZone? zone, int index, bool exclude) =>
        exclude ? [CanonicalIndex(points, index) ?? index]
        : EvidenceFor(points, zone, index).Where(e => e.Rank == "Manual").Select(e => e.Index).Distinct()
            .DefaultIfEmpty(CanonicalIndex(points, index) ?? index).ToArray();

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
        var candidates = PhysicalPoints(points, rank)
            .Select(p => (p.Index, Distance: Vector2.Distance(position, new(p.Point.X, p.Point.Y))))
            .Where(p => float.IsFinite(p.Distance)).OrderBy(p => p.Distance).Take(2).ToArray();
        if (candidates.Length == 0 || candidates[0].Distance > maxDistance) return null;
        if (candidates.Length > 1 && candidates[1].Distance - candidates[0].Distance < 0.35f) return null;
        return candidates[0].Index;
    }
    public static int? ConfirmedPoint(SpawnPoint[] points, SyncSpawnZone zone, bool reliableCycle)
    {
        if (zone.SCurrentIndex is { } observed && observed >= 0 && observed < points.Length
            && points[observed].Ranks.HasFlag(SpawnRanks.S)) return CanonicalIndex(points, observed);
        if (!reliableCycle || zone.SinceAt is null) return null;
        var possible = PhysicalPoints(points, SpawnRanks.S).Where(p => !IsRuledOut(points, zone, p.Index)).Take(2).ToArray();
        return possible.Length == 1 ? possible[0].Index : null;
    }
    // The centroid selects the aetheryte with the lowest mean squared map distance
    // to the candidates. This is a travel estimate, never a reported mark location.
    public static Vector2 TravelEstimate(SpawnPoint[] points, SyncSpawnZone? zone, bool reliableCycle)
    {
        if (zone is not null && ConfirmedPoint(points, zone, reliableCycle) is { } confirmed)
            return new(points[confirmed].X, points[confirmed].Y);

        var all = PhysicalPoints(points, SpawnRanks.S).ToArray();
        var candidates = reliableCycle && zone?.SinceAt is not null
            ? all.Where(x => !IsRuledOut(points, zone, x.Index)).ToArray() : all;
        // An empty/inconsistent mapping must not prevent a spawn attempt.
        if (candidates.Length == 0) candidates = all;
        if (candidates.Length == 0) return new(21.5f, 21.5f);
        return new(candidates.Average(x => x.Point.X), candidates.Average(x => x.Point.Y));
    }

}
