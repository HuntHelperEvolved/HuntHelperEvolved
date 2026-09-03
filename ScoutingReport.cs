using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace HuntHelperEvolved;

public static class ScoutingReport
{
    /// <summary>
    /// Produces a code string in the exact same format Hunt Helper's own
    /// train Export/Import feature uses — gzip-compressed JSON, base64-encoded —
    /// so it can be pasted straight into another player's Hunt Helper import box.
    /// Mirrors HuntHelper/Utilities/ExportImport.cs (img02/HuntHelper).
    /// </summary>
    public static string BuildExportCode(List<HuntHelperMobRecord> marks)
    {
        var json = JsonConvert.SerializeObject(marks);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            input.CopyTo(gzip);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    /// One block per expansion with at least one mark in the current scout (ARR ->
    /// Dawntrail order): a bolded "N marks up" count, a "Down:" line for any
    /// scouted marks that were already dead when found (sniped by someone else
    /// before the scout got there), and a "Not yet scouted:" line naming any marks
    /// from that expansion's known named roster that weren't encountered at all
    /// this scout. The roster check is by mark name/ModelID only, not instance
    /// count — it answers "was this mark seen anywhere," not "were every one of
    /// its concurrent zone instances found," since the latter isn't knowable
    /// without having actually walked all of them. Deliberately no "N/total"
    /// fraction on the up-count itself: a fixed total would go stale the moment a
    /// zone splits across extra instances, and a scout-only total reads as
    /// misleadingly "complete."
    /// </summary>
    public static string BuildSummary(List<HuntHelperMobRecord> marks)
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
