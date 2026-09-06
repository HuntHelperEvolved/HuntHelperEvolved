using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public static class ARankInstances
{
    // Evidence is collected per world and zone. A report in I2 establishes I1/I2 rows,
    // but never supplies a kill time for the other instance.
    public static List<uint> Resolve(IEnumerable<uint> zoneInstances, IEnumerable<uint> markKills)
    {
        var kills = markKills.Where(i => i <= 9).ToList();
        var highest = zoneInstances.Concat(kills).Where(i => i <= 9).DefaultIfEmpty(0u).Max();
        if (highest == 0) return new() { 0 };
        var rows = Enumerable.Range(1, (int)highest).Select(i => (uint)i).ToList();
        // Keep legacy/uninstanced kill records separate; do not assign them to I1.
        if (kills.Contains(0)) rows.Insert(0, 0);
        return rows;
    }
}
