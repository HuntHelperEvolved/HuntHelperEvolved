using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public static class ARankInstances
{
    // Evidence is collected per world and zone. A report in I2 establishes I1/I2 rows,
    // but never supplies a kill time for the other instance.
    public static List<uint> Resolve(IEnumerable<uint> zoneInstances, IEnumerable<uint> markKills)
    {
        uint highest = 0;
        var hasUninstancedKill = false;
        foreach (var instance in markKills)
        {
            if (instance > 9) continue;
            if (instance == 0) hasUninstancedKill = true;
            if (instance > highest) highest = instance;
        }
        foreach (var instance in zoneInstances)
            if (instance <= 9 && instance > highest) highest = instance;
        if (highest == 0) return new() { 0 };
        var rows = new List<uint>((int)highest + (hasUninstancedKill ? 1 : 0));
        // Legacy kill records remain separate from numbered instances.
        if (hasUninstancedKill) rows.Add(0);
        for (uint instance = 1; instance <= highest; instance++) rows.Add(instance);
        return rows;
    }
}
