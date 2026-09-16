using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.TrainPresets;

public static class PresetEditing
{
    public static void AddZone(TrainPreset preset, uint territory)
    {
        if (preset.Zones.Any(z => z.TerritoryId == territory)) return;
        var info = RouteCatalog.ByTerritory[territory];
        var last = preset.Zones.FindLastIndex(z => RouteCatalog.ByTerritory[z.TerritoryId].Expansion == info.Expansion);
        var zone = new PresetZone { TerritoryId = territory, MarkOrder = info.Marks.Select(m => m.NameId).ToList() };
        preset.Zones.Insert(last < 0 ? preset.Zones.Count : last + 1, zone);
    }

    public static void MoveExpansion(TrainPreset preset, string expansion, int direction)
    {
        var groups = preset.Zones.GroupBy(z => RouteCatalog.ByTerritory[z.TerritoryId].Expansion).ToList();
        var index = groups.FindIndex(g => g.Key == expansion);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= groups.Count) return;
        (groups[index], groups[target]) = (groups[target], groups[index]);
        preset.Zones = groups.SelectMany(g => g).ToList();
    }

    public static void MoveZone(TrainPreset preset, uint territory, int direction)
    {
        var index = preset.Zones.FindIndex(z => z.TerritoryId == territory);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= preset.Zones.Count) return;
        if (RouteCatalog.ByTerritory[territory].Expansion != RouteCatalog.ByTerritory[preset.Zones[target].TerritoryId].Expansion) return;
        (preset.Zones[index], preset.Zones[target]) = (preset.Zones[target], preset.Zones[index]);
    }
}
