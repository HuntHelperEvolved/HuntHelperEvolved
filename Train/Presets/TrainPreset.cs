using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HuntHelperEvolved.TrainPresets;

public sealed class TrainPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<PresetZone> Zones { get; set; } = new();
    public List<uint> ExcludedAetheryteIds { get; set; } = new();
    public bool RallyInInstancedZones { get; set; }
    public List<string> RallyBeforeExpansions { get; set; } = new();

    public TrainPreset Copy() => new()
    {
        Id = Id, Name = Name, Zones = Zones.Select(z => z.Copy()).ToList(),
        ExcludedAetheryteIds = new(ExcludedAetheryteIds),
        RallyInInstancedZones = RallyInInstancedZones, RallyBeforeExpansions = new(RallyBeforeExpansions),
    };
}

public sealed class PresetZone
{
    public uint TerritoryId { get; set; }
    public bool Strict { get; set; }
    public uint EntryAetheryteId { get; set; }
    public List<uint> MarkOrder { get; set; } = new();
    public PresetZone Copy() => new() { TerritoryId = TerritoryId, Strict = Strict,
        EntryAetheryteId = EntryAetheryteId, MarkOrder = new(MarkOrder) };
}

public sealed class PresetState
{
    public long Revision { get; set; }
    public List<TrainPreset> Presets { get; set; } = new();
    public string? ActivePresetId { get; set; }
    public bool OrderingPaused { get; set; }
    public RallyProgress Rallies { get; set; } = new();
    public PresetState Copy() => new()
    {
        Revision = Revision, ActivePresetId = ActivePresetId, OrderingPaused = OrderingPaused,
        Presets = Presets.Select(p => p.Copy()).ToList(),
        Rallies = Rallies.Copy(),
    };
}

public sealed class RouteZone
{
    public uint TerritoryId { get; set; }
    public string Name { get; set; } = "";
    public string Expansion { get; set; } = "";
    public int ExpansionOrder { get; set; }
    public int ZoneOrder { get; set; }
    public List<RouteMark> Marks { get; set; } = new();
    public List<RouteAetheryte> Aetherytes { get; set; } = new();
}
public sealed class RouteMark
{
    public uint NameId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class RouteAetheryte
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public RouteWaypoint? DepartureWaypoint { get; set; }
}
public sealed class RouteWaypoint
{
    public float X { get; set; }
    public float Y { get; set; }
}

public static class RouteDistance
{
    public static double FromAetheryte(RouteAetheryte entry, float x, float y) =>
        entry.DepartureWaypoint is { } exit
            ? Between(entry.X, entry.Y, exit.X, exit.Y) + Between(exit.X, exit.Y, x, y)
            : Between(entry.X, entry.Y, x, y);

    public static double Between(float x1, float y1, float x2, float y2) =>
        Math.Sqrt((double)(x1 - x2) * (x1 - x2) + (double)(y1 - y2) * (y1 - y2));
}

public static class RouteCatalog
{
    // Data copied from ExpansionData, SRankTimerData and TeleportHelper.
    // Zero-coordinate aetherytes are placeholders, not usable route entrances.
    public static readonly IReadOnlyList<RouteZone> Zones = Read();
    public static readonly IReadOnlyDictionary<uint, RouteZone> ByTerritory = Zones.ToDictionary(z => z.TerritoryId);

    private static List<RouteZone> Read()
    {
        using var stream = typeof(RouteCatalog).Assembly.GetManifestResourceStream("TrainPresets.RouteCatalog.json")
            ?? throw new InvalidOperationException("Missing train route catalog.");
        return JsonSerializer.Deserialize<List<RouteZone>>(stream, new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Invalid train route catalog.");
    }

    public static string? Validate(TrainPreset? preset)
    {
        if (preset is null || !Guid.TryParseExact(preset.Id, "N", out _)) return "Invalid preset ID.";
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 || preset.Name.Any(char.IsControl))
            return "Give the preset a name of 1 to 80 characters.";
        if (preset.Zones is null || preset.Zones.Count is < 1 or > 64
            || preset.Zones.Any(z => z is null) || preset.Zones.Select(z => z.TerritoryId).Distinct().Count() != preset.Zones.Count)
            return "Choose at least one zone, with each zone listed once.";
        if (preset.ExcludedAetheryteIds is null || preset.ExcludedAetheryteIds.Count > 128)
            return "Too many excluded aetherytes.";
        if (preset.RallyBeforeExpansions is null || preset.RallyBeforeExpansions.Count > 7
            || preset.RallyBeforeExpansions.Distinct().Count() != preset.RallyBeforeExpansions.Count
            || preset.RallyBeforeExpansions.Any(e => !Zones.Any(z => z.Expansion == e)))
            return "Choose known expansions for rally stops.";
        var expansions = new HashSet<string>();
        string? previous = null;
        foreach (var zone in preset.Zones)
        {
            if (!ByTerritory.TryGetValue(zone.TerritoryId, out var info)) return "The preset contains an unknown hunt zone.";
            if (zone.EntryAetheryteId != 0 && (!info.Aetherytes.Any(a => a.Id == zone.EntryAetheryteId)
                || preset.ExcludedAetheryteIds.Contains(zone.EntryAetheryteId)))
                return $"Choose an available entry aetheryte in {info.Name}.";
            if (previous != info.Expansion && !expansions.Add(info.Expansion))
                return "Keep each expansion's zones together.";
            previous = info.Expansion;
            if (zone.MarkOrder is null || zone.MarkOrder.Count > info.Marks.Count
                || zone.MarkOrder.Distinct().Count() != zone.MarkOrder.Count
                || zone.MarkOrder.Any(id => !info.Marks.Any(m => m.NameId == id))
                || zone.Strict && zone.MarkOrder.Count != info.Marks.Count)
                return $"Choose an order for every mark in {info.Name}.";
        }
        return null;
    }
}

public readonly record struct RoutePoint(uint NameId, uint WorldId, uint TerritoryId, uint Instance,
    float X, float Y, bool Dead = false, bool IsCustom = false);

public static class PresetRouter
{
    public static List<T> Order<T>(IEnumerable<T> marks, TrainPreset preset, Func<T, RoutePoint> point)
    {
        var zones = preset.Zones.ToDictionary(z => z.TerritoryId);
        var ranks = preset.Zones.Select((z, i) => (z.TerritoryId, i)).ToDictionary(p => p.TerritoryId, p => p.i);
        var result = new List<T>();
        foreach (var world in marks.GroupBy(m => point(m).WorldId))
        {
            string Expansion(T m) => RouteCatalog.ByTerritory.TryGetValue(point(m).TerritoryId, out var info) ? info.Expansion : "";
            foreach (var expansion in world.GroupBy(Expansion)
                         .OrderBy(g => g.Select(m => ranks.GetValueOrDefault(point(m).TerritoryId, int.MaxValue)).Min()))
            foreach (var zone in expansion.GroupBy(m => point(m).TerritoryId)
                         .OrderBy(g => ranks.GetValueOrDefault(g.Key, int.MaxValue)))
            {
                if (!zones.TryGetValue(zone.Key, out var rule))
                {
                    result.AddRange(zone);
                    continue;
                }
                foreach (var instance in zone.GroupBy(m => point(m).Instance).OrderBy(g => g.Key))
                    result.AddRange(OrderZone(instance.ToList(), rule, preset, point));
            }
        }
        return result;
    }

    private static List<T> OrderZone<T>(List<T> rows, PresetZone rule, TrainPreset preset, Func<T, RoutePoint> point)
    {
        if (rule.Strict)
        {
            var ordered = new Queue<T>(OrderMarks(rows.Where(m => !point(m).IsCustom).ToList(), rule, preset, point));
            return rows.Select(m => point(m).IsCustom ? m : ordered.Dequeue()).ToList();
        }
        // Flags are conductor-placed waypoints. Keep them as barriers and only
        // reorder the ordinary marks between them.
        var result = new List<T>();
        var run = new List<T>();
        foreach (var row in rows)
        {
            if (!point(row).IsCustom) { run.Add(row); continue; }
            result.AddRange(OrderMarks(run, rule, preset, point));
            run.Clear();
            result.Add(row);
        }
        result.AddRange(OrderMarks(run, rule, preset, point));
        return result;
    }

    private static List<T> OrderMarks<T>(List<T> rows, PresetZone rule, TrainPreset preset, Func<T, RoutePoint> point)
    {
        if (rule.Strict)
            return rows.OrderBy(m => { var rank = rule.MarkOrder.IndexOf(point(m).NameId); return rank < 0 ? -1 : rank; }).ToList();
        var dead = rows.Where(m => point(m).Dead).ToList();
        var alive = rows.Where(m => !point(m).Dead).ToList();
        if (alive.Count < 2 || !RouteCatalog.ByTerritory.TryGetValue(rule.TerritoryId, out var zone))
            return dead.Concat(alive).ToList();

        // Timed A-ranks have at most two marks per zone/instance. Unknown rows
        // and missing positions retain scout order instead of guessing a route.
        if (alive.Count > 8 || alive.Any(m => !zone.Marks.Any(a => a.NameId == point(m).NameId)
            || !ValidPosition(point(m))))
            return dead.Concat(alive).ToList();
        var starts = zone.Aetherytes.Where(a => !preset.ExcludedAetheryteIds.Contains(a.Id)
            && (rule.EntryAetheryteId == 0 || a.Id == rule.EntryAetheryteId)).ToList();
        if (starts.Count == 0) return dead.Concat(alive).ToList();

        var points = alive.Select(point).ToArray();
        var n = points.Length;
        var costs = new double[1 << n, n];
        var parents = new int[1 << n, n];
        for (var mask = 0; mask < 1 << n; mask++)
            for (var last = 0; last < n; last++) { costs[mask, last] = double.PositiveInfinity; parents[mask, last] = -1; }
        for (var i = 0; i < n; i++)
            costs[1 << i, i] = starts.Min(a => RouteDistance.FromAetheryte(a, points[i].X, points[i].Y));
        for (var mask = 1; mask < 1 << n; mask++)
            for (var last = 0; last < n; last++)
                if ((mask & (1 << last)) != 0)
                    for (var next = 0; next < n; next++)
                    {
                        if ((mask & (1 << next)) != 0) continue;
                        var nextMask = mask | (1 << next);
                        var cost = costs[mask, last] + RouteDistance.Between(points[last].X, points[last].Y, points[next].X, points[next].Y);
                        if (cost + 0.000001 >= costs[nextMask, next]) continue;
                        costs[nextMask, next] = cost;
                        parents[nextMask, next] = last;
                    }
        var full = (1 << n) - 1;
        var end = Enumerable.Range(0, n).OrderBy(i => costs[full, i]).ThenByDescending(i => i).First();
        var path = new List<T>();
        while (end >= 0)
        {
            path.Add(alive[end]);
            var previous = parents[full, end];
            full ^= 1 << end;
            end = previous;
        }
        path.Reverse();
        return dead.Concat(path).ToList();
    }

    private static bool ValidPosition(RoutePoint p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X > 0 && p.Y > 0;
}
