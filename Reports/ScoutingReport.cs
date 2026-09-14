using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HuntHelperEvolved;

public static class ScoutingReport
{
    /// <summary>
    /// Groups up, down and unscouted marks by expansion, checking the roster by model ID.
    /// Omits a completion fraction because the total number of zone instances is unknown.
    /// </summary>
    public static string BuildSummary(List<TrainMobRecord> marks)
    {
        var withInfo = marks
            .Select(m => (Mark: m, Info: ExpansionData.Lookup(m.MobID)))
            .Where(x => x.Info != null)
            .ToList();

        var scoutedModelIds = withInfo.Select(x => x.Mark.MobID).ToHashSet();

        var blocks = withInfo
            .GroupBy(x => x.Info!.Expansion)
            .OrderBy(g => g.Min(x => x.Info!.Order))
            .Select(g =>
            {
                var expansionName = g.Key;

                var upCount = g.Where(x => !x.Mark.Dead)
                    .Select(x => (x.Mark.MobID, x.Mark.Instance)).Distinct().Count();

                var sb = new StringBuilder();
                sb.Append($"**{expansionName}**: {upCount} mark{(upCount == 1 ? "" : "s")} up");

                var down = g.Where(x => x.Mark.Dead)
                    .OrderBy(x => x.Info!.ZoneOrder)
                    .ThenBy(x => x.Mark.Name)
                    .Select(x => $"{x.Mark.Name} ({x.Info!.Location}{ExpansionData.InstanceGlyph(x.Mark.Instance)})")
                    .Distinct()
                    .ToList();

                if (down.Count > 0)
                    sb.Append($"\nDown: {string.Join(", ", down)}");

                var notScouted = ExpansionData.ModelIdToMark
                    .Where(kv => kv.Value.Expansion == expansionName && !scoutedModelIds.Contains(kv.Key))
                    .OrderBy(kv => kv.Value.ZoneOrder)
                    .Select(kv => $"{kv.Value.Name} ({kv.Value.Location})")
                    .ToList();

                if (notScouted.Count > 0)
                    sb.Append($"\nNot yet scouted: {string.Join(", ", notScouted)}");

                return sb.ToString();
            });

        var joined = string.Join("\n\n", blocks);
        return string.IsNullOrEmpty(joined) ? "No known A-rank marks in the current scout." : joined;
    }
}
