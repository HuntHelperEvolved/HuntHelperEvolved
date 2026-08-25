using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HuntTrainRelay;

public static class DiscordRelay
{
    private static readonly HttpClient Http = new();

    // Discord embed side-bar colour (a calm green). Decimal form of hex 2ECC71.
    private const int EmbedColor = 3066993;

    // Discord embed field value hard limit is 1024 characters; stay safely under it.
    private const int FieldCharLimit = 1000;

    public static Task<(bool Success, string Message)> PostTestAsync(List<string> webhookUrls)
    {
        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title = "🚂 Hunt Train Relay — test message",
                    description = $"If you can see this, your webhook is working.\nPosted <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>",
                    color = EmbedColor,
                },
            },
        };

        return SendToAllAsync(webhookUrls, payload);
    }

    public static Task<(bool Success, string Message)> PostScoutingReportAsync(List<string> webhookUrls, List<HuntHelperMobRecord> marks)
    {
        if (marks.Count == 0)
            return Task.FromResult((false, "Nothing to report — Hunt Helper's train list is empty."));

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exportCode = ScoutingReport.BuildExportCode(marks);
        var summary = ScoutingReport.BuildSummary(marks);

        string description;
        if (exportCode.Length > 3800)
        {
            // Discord embed descriptions cap at 4096 characters. Rather than ever
            // post a code block that's been cut off mid-string (unusable to import),
            // drop the code and say so plainly if a scout is genuinely too big.
            description =
                $"Scouting done at <t:{nowUnix}:F>\n\n{summary}\n\n" +
                "(Export code omitted — this scout covers too many marks to fit in one " +
                "Discord message. Try sending separate reports per zone instead.)";
        }
        else
        {
            description = $"```\n{exportCode}\n```\nScouting done at <t:{nowUnix}:F>\n\n{summary}";
        }

        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title = "🔭 Scouting Report",
                    description,
                    color = EmbedColor,
                },
            },
        };

        return SendToAllAsync(webhookUrls, payload);
    }

    public static Task<(bool Success, string Message)> PostTrainCompleteAsync(List<string> webhookUrls, List<TrackedMark> marks)
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var fields = BuildFields(marks);

        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title = "🚂 Train Complete",
                    description = $"Finished <t:{nowUnix}:F> — {marks.Count} marks",
                    color = EmbedColor,
                    fields,
                },
            },
        };

        return SendToAllAsync(webhookUrls, payload);
    }

    /// <summary>
    /// One or more Discord embed fields per expansion (in ARR -> Dawntrail order),
    /// each field's value listing marks grouped by home zone (in MSQ order) so
    /// same-location marks — including duplicate marks across multiple zone
    /// instances — sit next to each other. Grouped strictly by expansion name —
    /// never by the whole per-mark record, since each mark's zone differs and
    /// would otherwise split one expansion into several same-named fields.
    /// </summary>
    private static List<object> BuildFields(List<TrackedMark> marks)
    {
        var withInfo = marks.Select(m => (Mark: m, Info: ExpansionData.Lookup(m.ModelId))).ToList();

        var groups = withInfo
            .GroupBy(x => x.Info?.Expansion ?? "No fixed timer")
            .OrderBy(g => g.Min(x => x.Info?.Order ?? int.MaxValue));

        var fields = new List<object>();
        foreach (var group in groups)
        {
            var zoneBlocks = BuildZoneBlocks(group.Select(x => x.Mark).ToList());
            var chunks = ChunkZoneBlocks(zoneBlocks, FieldCharLimit);

            for (var i = 0; i < chunks.Count; i++)
            {
                var name = chunks.Count > 1 ? $"{group.Key} ({i + 1}/{chunks.Count})" : group.Key;
                fields.Add(new { name, value = chunks[i], inline = false });
            }
        }

        return fields;
    }

    /// <summary>
    /// One block of text per zone (in MSQ order), each block holding every mark
    /// from that zone as its own line. Kept as whole blocks so chunking below can
    /// guarantee a zone's marks are never split across two different fields.
    /// </summary>
    private static List<string> BuildZoneBlocks(List<TrackedMark> marksInGroup)
    {
        var withInfo = marksInGroup.Select(m => (Mark: m, Info: ExpansionData.Lookup(m.ModelId))).ToList();

        var blocks = withInfo
            .Where(x => x.Info != null)
            .GroupBy(x => x.Info!.Location)
            .OrderBy(g => g.First().Info!.ZoneOrder)
            .Select(g => string.Join("\n", g.OrderBy(x => x.Mark.Name).Select(x => BuildLine(x.Mark, x.Info!))))
            .ToList();

        var unknown = withInfo.Where(x => x.Info == null)
            .Select(x => x.Mark.Name).Distinct().OrderBy(n => n).ToList();
        if (unknown.Count > 0)
            blocks.Add($"{string.Join(", ", unknown)} — no fixed respawn timer");

        return blocks;
    }

    private static string BuildLine(TrackedMark m, MarkInfo mark)
    {
        var deathTime = EnsureUtc(m.DeathObservedAtUtc ?? m.LastSeenUtc);
        var openUnix = new DateTimeOffset(deathTime.AddHours(mark.MinHours)).ToUnixTimeSeconds();
        var capUnix = new DateTimeOffset(deathTime.AddHours(mark.MaxHours)).ToUnixTimeSeconds();
        var instanceGlyph = ExpansionData.InstanceGlyph(m.Instance);
        return $"{mark.Location} — {m.Name}{instanceGlyph} — window <t:{openUnix}:t> → <t:{capUnix}:t>";
    }

    /// <summary>
    /// Packs whole zone-blocks into chunks under the character limit — a zone's
    /// marks are never split across two fields, even if that means starting a new
    /// field earlier than the character count strictly requires (e.g. a 6-zone
    /// expansion that needs 2 fields will end up as "first 5 zones" + "last zone"
    /// rather than cutting a zone's own marks in half). Only in the unlikely case
    /// one zone alone exceeds the whole limit does it get split by line as a
    /// last resort.
    /// </summary>
    private static List<string> ChunkZoneBlocks(List<string> blocks, int limit)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var block in blocks)
        {
            if (block.Length > limit)
            {
                if (current.Length > 0) { chunks.Add(current.ToString()); current.Clear(); }
                foreach (var line in block.Split('\n'))
                    chunks.Add(line);
                continue;
            }

            if (current.Length > 0 && current.Length + block.Length + 1 > limit)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(block);
        }

        if (current.Length > 0) chunks.Add(current.ToString());
        if (chunks.Count == 0) chunks.Add(string.Empty);

        return chunks;
    }

    /// <summary>
    /// DateTimeOffset treats DateTimeKind.Unspecified as local time, not UTC.
    /// Hunt Helper's timestamps are always UTC by convention (DateTime.UtcNow),
    /// but deserializing over IPC can lose the Kind flag, so force it back to UTC.
    /// </summary>
    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    /// <summary>
    /// Posts the same payload to every configured, non-empty webhook URL (e.g. one
    /// per Discord server). Reports full success only if every target succeeded;
    /// otherwise names which ones failed and why.
    /// </summary>
    private static async Task<(bool Success, string Message)> SendToAllAsync(List<string>? webhookUrls, object payload)
    {
        var targets = (webhookUrls ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct()
            .ToList();

        if (targets.Count == 0)
            return (false, "No webhook URL configured.");

        var json = JsonConvert.SerializeObject(payload);
        var successCount = 0;
        var failures = new List<string>();

        foreach (var url in targets)
        {
            var (success, message) = await SendRawAsync(url, json);
            if (success) successCount++;
            else failures.Add(message);
        }

        if (failures.Count == 0)
            return (true, $"Posted to {successCount} webhook{(successCount == 1 ? "" : "s")} at {DateTime.Now:T}.");

        return (false, $"Posted to {successCount}/{targets.Count} webhooks. {string.Join(" | ", failures)}");
    }

    private static async Task<(bool Success, string Message)> SendRawAsync(string webhookUrl, string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(webhookUrl, content);

            if (response.IsSuccessStatusCode)
                return (true, "OK");

            var body = await response.Content.ReadAsStringAsync();
            return (false, $"Discord returned {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return (false, $"Request failed: {ex.Message}");
        }
    }
}
