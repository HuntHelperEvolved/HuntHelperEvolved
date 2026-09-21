using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.TrainPresets;

public readonly record struct RallyKey(uint WorldId, uint TerritoryId, uint Instance);
public readonly record struct RallyVisit(RallyKey Key, string Expansion);
public readonly record struct RallyRow(RallyVisit Visit, uint NameId, bool Completed = false);
public sealed record RallyStop(RallyKey Key, RouteAetheryte Aetheryte, string Expansion, bool Completed);
public sealed class RallyProgress
{
    public long Revision { get; set; }
    public List<RallyVisit> Completed { get; set; } = new();
    public List<RallyRow> Rows { get; set; } = new();
    // Null distinguishes progress saved before visit tracking from an empty train.
    public List<RallyKey>? LiveVisits { get; set; }
    public RallyProgress Copy() => new() { Revision = Revision, Completed = new(Completed), Rows = new(Rows),
        LiveVisits = LiveVisits is null ? null : new(LiveVisits) };
}

public sealed record RallyRoute<T>(List<T> Rows, bool ProgressChanged);

public static class PresetRallies
{
    public static RallyRoute<T> Reconcile<T>(IReadOnlyList<T> input, TrainPreset? preset, RallyProgress progress,
        Func<T, RoutePoint> point, Func<RallyStop, uint, T?, T> flag, bool orderingPaused = false,
        bool restartRallies = false) where T : class
    {
        static (uint, uint, uint) Identity(RoutePoint p) => (p.NameId, p.Instance, p.WorldId);
        static (uint, uint, uint) RowIdentity(RallyRow r) => (r.NameId, r.Visit.Key.Instance, r.Visit.Key.WorldId);
        var before = progress.Copy();
        var owned = progress.Rows.ToDictionary(RowIdentity);
        var existing = input.Where(m => owned.ContainsKey(Identity(point(m)))).ToDictionary(m => Identity(point(m)));
        foreach (var row in progress.Rows)
        {
            existing.TryGetValue(RowIdentity(row), out var current);
            if (current is not null && row.Completed && !point(current).Dead)
                progress.Completed.RemoveAll(v => v.Key == row.Visit.Key);
            else if (current is null || point(current).Dead)
                Remember(progress.Completed, row.Visit);
        }

        var ordinary = input.Where(m => !owned.ContainsKey(Identity(point(m)))).ToList();
        var live = ordinary.Select(point).Where(p => !p.IsCustom && !p.Dead)
            .Select(p => new RallyKey(p.WorldId, p.TerritoryId, p.Instance)).ToHashSet();
        if (preset is not null) RefreshVisits(progress, live);
        if (preset is not null && restartRallies) progress.Completed.Clear();
        bool ProgressChanged() => !before.Rows.SequenceEqual(progress.Rows)
            || !before.Completed.SequenceEqual(progress.Completed)
            || (before.LiveVisits is null ? progress.LiveVisits is not null
                : progress.LiveVisits is null || !before.LiveVisits.SequenceEqual(progress.LiveVisits));
        if (preset is not null && orderingPaused)
        {
            // Manual adjustments freeze pending rally positions too. Continue
            // tracking completed flags and removing flags whose live marks are gone.
            progress.Rows = progress.Rows.Where(r => live.Contains(r.Visit.Key) && existing.ContainsKey(RowIdentity(r)))
                .Select(r => r with { Completed = point(existing[RowIdentity(r)]).Dead }).ToList();
            var retained = progress.Rows.Select(RowIdentity).ToHashSet();
            var frozen = input.Where(m => !owned.ContainsKey(Identity(point(m))) || retained.Contains(Identity(point(m)))).ToList();
            return new(frozen, ProgressChanged());
        }
        var ordered = preset is null ? ordinary : PresetRouter.Order(ordinary, preset, point);
        if (preset is not null) RememberCompletedEntries(ordered.Select(point), preset, progress.Completed);
        var stops = preset is null ? new List<RallyStop>() : Plan(ordered.Select(point), preset, progress.Completed);
        var pending = new Dictionary<RallyKey, T>();
        var rows = new List<RallyRow>();
        var usedIds = input.Select(m => point(m).NameId).ToHashSet();
        foreach (var stop in stops)
        {
            var previous = progress.Rows.FirstOrDefault(r => r.Visit.Key == stop.Key);
            existing.TryGetValue(RowIdentity(previous), out var old);
            // Removed flags stay removed. A checked flag remains visible until
            // the conductor clears it, just like a manually placed custom flag.
            if (stop.Completed && (old is null || !point(old).Dead)) continue;
            if (!stop.Completed && old is not null && point(old).Dead) old = null;
            var id = previous.NameId;
            if (old is null)
            {
                do { id = uint.MaxValue - (uint)Random.Shared.Next(0, 8_000_000) * 16; } while (!usedIds.Add(id));
            }
            var value = stop.Completed ? old! : flag(stop, id, old);
            pending[stop.Key] = value;
            rows.Add(new(new(stop.Key, stop.Expansion), id, stop.Completed));
        }
        var result = new List<T>();
        foreach (var row in ordered)
        {
            var p = point(row);
            if (pending.Remove(new(p.WorldId, p.TerritoryId, p.Instance), out var rally)) result.Add(rally);
            result.Add(row);
        }
        progress.Rows = rows;
        return new(result, ProgressChanged());
    }

    private static void RefreshVisits(RallyProgress progress, HashSet<RallyKey> live)
    {
        if (progress.LiveVisits is { } previous)
        {
            var returned = live.Except(previous).ToHashSet();
            var previousExpansions = previous.Where(k => RouteCatalog.ByTerritory.ContainsKey(k.TerritoryId))
                .Select(k => (k.WorldId, RouteCatalog.ByTerritory[k.TerritoryId].Expansion)).ToHashSet();
            var returnedExpansions = returned.Where(k => RouteCatalog.ByTerritory.ContainsKey(k.TerritoryId))
                .Select(k => (k.WorldId, RouteCatalog.ByTerritory[k.TerritoryId].Expansion))
                .Where(e => !previousExpansions.Contains(e)).ToHashSet();
            // Keep completed entries while clearing a run, so expansion rallies
            // do not move to the next zone. Reopen them when live marks return.
            progress.Completed.RemoveAll(v => returned.Contains(v.Key)
                || returnedExpansions.Contains((v.Key.WorldId, v.Expansion)));
        }
        progress.LiveVisits = live.OrderBy(k => k.WorldId).ThenBy(k => k.TerritoryId).ThenBy(k => k.Instance).ToList();
    }

    private static void Remember(List<RallyVisit> completed, RallyVisit visit)
    {
        var index = completed.FindIndex(v => v.Key == visit.Key);
        if (index < 0) completed.Add(visit);
        else if (visit.Expansion.Length > 0) completed[index] = visit;
    }

    public static List<RallyStop> Plan(IEnumerable<RoutePoint> ordered, TrainPreset preset, IReadOnlyList<RallyVisit> completed)
    {
        var result = new List<RallyStop>();
        var zones = preset.Zones.ToDictionary(z => z.TerritoryId);
        var firstExpansion = preset.Zones.Select(z => RouteCatalog.ByTerritory[z.TerritoryId].Expansion).FirstOrDefault();
        foreach (var world in ordered.Where(p => !p.IsCustom && !p.Dead).GroupBy(p => p.WorldId))
        {
            var entered = new HashSet<string>();
            foreach (var group in world.GroupBy(p => new RallyKey(p.WorldId, p.TerritoryId, p.Instance)))
            {
                if (!RouteCatalog.ByTerritory.TryGetValue(group.Key.TerritoryId, out var zone)) continue;
                var firstInExpansion = entered.Add(zone.Expansion);
                var prior = completed.FirstOrDefault(v => v.Key == group.Key);
                var expansionEnabled = zone.Expansion != firstExpansion && preset.RallyBeforeExpansions.Contains(zone.Expansion);
                var expansionEntry = expansionEnabled && firstInExpansion
                    && !completed.Any(v => v.Key.WorldId == world.Key && v.Expansion == zone.Expansion);
                var instanceEntry = preset.RallyInInstancedZones && group.Key.Instance > 0;
                var completedExpansionEntry = expansionEnabled && prior.Expansion == zone.Expansion;
                if (!instanceEntry && !expansionEntry && !completedExpansionEntry) continue;
                var first = group.First();
                var entryId = zones.GetValueOrDefault(group.Key.TerritoryId)?.EntryAetheryteId ?? 0;
                if (entryId == 0 && (!float.IsFinite(first.X) || !float.IsFinite(first.Y) || first.X <= 0 || first.Y <= 0)) continue;
                var entry = zone.Aetherytes.Where(a => !preset.ExcludedAetheryteIds.Contains(a.Id)
                        && (entryId == 0 || a.Id == entryId))
                    .OrderBy(a => RouteDistance.FromAetheryte(a, first.X, first.Y))
                    .ThenBy(a => a.Id).FirstOrDefault();
                if (entry is null) continue;
                result.Add(new(group.Key, entry, expansionEntry || completedExpansionEntry ? zone.Expansion : "",
                    completed.Any(v => v.Key == group.Key)));
            }
        }
        return result;
    }

    private static bool RememberCompletedEntries(IEnumerable<RoutePoint> ordered, TrainPreset preset, List<RallyVisit> completed)
    {
        var changed = false;
        foreach (var stop in Plan(ordered, preset, completed).Where(s => s.Completed))
        {
            var visit = new RallyVisit(stop.Key, stop.Expansion);
            var index = completed.FindIndex(v => v.Key == stop.Key);
            if (index >= 0 && (completed[index].Expansion == visit.Expansion || visit.Expansion.Length == 0)) continue;
            Remember(completed, visit);
            changed = true;
        }
        return changed;
    }
}
