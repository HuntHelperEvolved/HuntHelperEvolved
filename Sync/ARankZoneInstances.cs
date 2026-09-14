using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

internal sealed class ARankZoneInstances
{
    private static readonly Dictionary<uint, string[]> TerritoryZones = SRankTimerData.All
        .GroupBy(t => t.TerritoryId).ToDictionary(g => g.Key, g => g.Select(t => t.Zone).Distinct().ToArray());
    internal static readonly Dictionary<string, uint[]> ZoneTerritories = SRankTimerData.All
        .GroupBy(t => t.Zone).ToDictionary(g => g.Key, g => g.Select(t => t.TerritoryId).Distinct().ToArray());
    private readonly Dictionary<(uint World, string Zone), HashSet<uint>> _instances = new();

    internal void Add(uint nameId, uint territoryId, uint world, uint instance)
    {
        if (ExpansionData.Lookup(nameId) is { } mark) AddZone(world, mark.Location, instance);
        if (TerritoryZones.TryGetValue(territoryId, out var zones))
            foreach (var zone in zones) AddZone(world, zone, instance);
    }

    private void AddZone(uint world, string zone, uint instance)
    {
        var key = (world, zone);
        if (!_instances.TryGetValue(key, out var values)) _instances.Add(key, values = new());
        values.Add(instance);
    }

    internal IEnumerable<uint> Get(uint world, string zone) =>
        _instances.TryGetValue((world, zone), out var values) ? values : System.Array.Empty<uint>();
}
