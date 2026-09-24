using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

namespace HuntHelperEvolved;

public static class DiscordRelay
{
    // Discord embed side-bar colour (a calm green). Decimal form of hex 2ECC71.
    private const int EmbedColor = 3066993;

    public static Task<(bool Success, string Message)> PostTestAsync(List<WebhookEntry> webhooks, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title = "🚂 Hunt Helper Evolved — test message",
                    description = $"If you can see this, your webhook is working.\nPosted <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>",
                    color = EmbedColor,
                },
            },
        };

        return DiscordWebhookSender.SendAsync(webhooks, new[] { payload }, cancellationToken);
    }

    public static Task<(bool Success, string Message)> PostScoutingReportAsync(List<WebhookEntry> webhooks, List<TrainMobRecord> marks, List<string> scoutNames, string exportCode, CancellationToken cancellationToken = default) =>
        PostPreparedReportAsync(webhooks, PrepareScoutingReport(ScoutingReport.ToNative(marks), scoutNames,
            exportCode, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), cancellationToken);

    internal static IReadOnlyList<object> BuildScoutingMessages(List<TrainMobRecord> marks, List<string> scoutNames, string exportCode, long nowUnix) =>
        PrepareScoutingReport(ScoutingReport.ToNative(marks), scoutNames, exportCode, nowUnix).Messages;

    internal static PreparedDiscordReport PrepareScoutingReport(IReadOnlyList<NativeTrainRecord> marks,
        IEnumerable<string> scoutNames, string exportCode, long nowUnix, string? notes = null)
    {
        if (marks.Count == 0)
            return new PreparedDiscordReport(Array.Empty<DiscordEmbedPreview>(), packEmbeds: true);

        var codeBlock = $"```\n{exportCode}\n```";
        if (codeBlock.Length > DescriptionLimit)
            return new PreparedDiscordReport(Array.Empty<DiscordEmbedPreview>(), packEmbeds: true,
                emptyMessage: "Scouting report not sent: the intact import code exceeds Discord's 4,096-character embed limit. Copy Export Code in Setup or report a smaller train. Nothing was posted.");

        var details = new StringBuilder($"From the train list • Sent <t:{nowUnix}:F>\n\n");
        details.Append(ScoutingReport.BuildSummary(marks));
        details.Append("\n\nUp is the last recorded state.");
        var normalizedNotes = ScoutingReport.NormalizeNotes(notes);
        if (normalizedNotes.Length > 0)
            details.Append($"\n\n**Scout notes**\n{ScoutingReport.EscapeText(normalizedNotes)}");
        var names = scoutNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(ScoutingReport.EscapeText).ToList();
        if (names.Count > 0) details.Append($"\n\nScouts: {string.Join(", ", names)}");

        var reportEmbeds = ChunkByLength(details.ToString(), DescriptionLimit)
            .Select((description, i) => new DiscordEmbedPreview(
                i == 0 ? "🔭 Scouting Report" : "🔭 Scouting Report (continued)", description)).ToList();
        if (!PreparedDiscordReport.FitsOneMessage(reportEmbeds))
            return new PreparedDiscordReport(Array.Empty<DiscordEmbedPreview>(), packEmbeds: true,
                emptyMessage: "Scouting report not sent: the report details exceed Discord's single-message limit. Report a smaller train or shorten the scout credits. Nothing was posted.");

        var codeEmbed = new DiscordEmbedPreview("Import code", codeBlock);
        var combined = new[] { codeEmbed }.Concat(reportEmbeds).ToList();
        // Prefer one message. If it does not fit, the complete code stands alone
        // in the first message and every report detail remains in the second.
        return PreparedDiscordReport.FitsOneMessage(combined)
            ? PreparedDiscordReport.FromMessages(combined)
            : PreparedDiscordReport.FromMessages(new[] { codeEmbed }, reportEmbeds);
    }

    internal static Task<(bool Success, string Message)> PostPreparedReportAsync(List<WebhookEntry> webhooks,
        PreparedDiscordReport report, CancellationToken cancellationToken = default) =>
        report.MessageCount == 0 ? Task.FromResult((false, report.EmptyMessage))
            : DiscordWebhookSender.SendAsync(webhooks, report.Messages, cancellationToken);

    /// <summary>
    /// Posts the train report over exactly the marks and watches it is handed.
    ///
    /// Deciding WHICH marks those are is the caller's job, not this one's: a
    /// report normally covers only the expansions the train killed something
    /// in, but against a sync server too old to clear part of a shared train it
    /// has to cover all of them, because the whole train is about to be wiped
    /// either way. That choice needs the server's capabilities, which belong to
    /// the plugin. Both callers narrow the list through TrainReport, which the
    /// in-game preview reads from as well. Discord summarizes the same observed
    /// kills and preserves the individual exceptions below their summary.
    /// </summary>
    public static Task<(bool Success, string Message)> PostTrainCompleteAsync(List<WebhookEntry> webhooks, List<TrackedMark> marks, string? endedBy, List<FlagEntry>? flags = null, CancellationToken cancellationToken = default) =>
        PostPreparedReportAsync(webhooks,
            PrepareTrainReport(marks, endedBy, flags, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), cancellationToken);

    internal static IReadOnlyList<object> BuildTrainMessages(List<TrackedMark> marks, string? endedBy, List<FlagEntry>? flags, long nowUnix) =>
        PrepareTrainReport(marks, endedBy, flags, nowUnix).Messages;

    internal static PreparedDiscordReport PrepareTrainReport(List<TrackedMark> marks, string? endedBy, List<FlagEntry>? flags, long nowUnix)
    {
        if (marks.Count == 0)
            return new PreparedDiscordReport(Array.Empty<DiscordEmbedPreview>(), packEmbeds: false,
                emptyMessage: "Nothing to report — no marks were killed on this train.");
        var endedByLine = string.IsNullOrWhiteSpace(endedBy) ? "" : $"\nEnded by {endedBy}";
        var description = $"Finished <t:{nowUnix}:F> — {marks.Count(TrainReport.IsObservedKill)} observed kills{endedByLine}\n\n" +
            BuildTrainBody(marks) + BuildFlagFooter(flags);
        var embeds = ChunkByLength(description, DescriptionLimit).Select((chunk, i) =>
            new DiscordEmbedPreview(i == 0 ? "🚂 Train Complete" : "🚂 Train Complete (continued)", chunk));
        return new PreparedDiscordReport(embeds, packEmbeds: false);
    }

    private const int DescriptionLimit = 4096;

    /// <summary>
    /// S-rank watch results for the train (Spawned / Didn't Spawn / never checked).
    /// </summary>
    private static string BuildFlagFooter(List<FlagEntry>? flags)
    {
        var watches = flags ?? new List<FlagEntry>();
        if (watches.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.Append("\n**S-Rank Checks**\n");
        foreach (var f in watches)
        {
            var status = f.SpawnStatus switch
            {
                SpawnStatus.Spawned => "Spawned",
                SpawnStatus.NotSpawned => "Did not spawn",
                _ => "Not checked",
            };
            sb.Append($"{f.Label} — {status}\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Summarizes observed-kill windows by world and expansion. Sniped marks,
    /// unknown timers, unfinished marks and missing roster entries retain their
    /// own details, so none can be mistaken for a kill included in the range.
    /// </summary>
    private static string BuildTrainBody(List<TrackedMark> marks)
    {
        var entries = TrainReport.BuildEntries(marks);
        var sb = new StringBuilder();

        var summaries = TrainReport.BuildWindowSummaries(marks);
        if (summaries.Count > 0)
        {
            sb.Append("**Respawn windows**\nOverall ranges for observed kills only; each mark has its own window.\n");
            foreach (var summary in summaries)
            {
                var openUnix = new DateTimeOffset(summary.EarliestWindowOpensUtc).ToUnixTimeSeconds();
                var capUnix = new DateTimeOffset(summary.LatestWindowCapsUtc).ToUnixTimeSeconds();
                var instances = string.Concat(summary.Instances.Select(ExpansionData.InstanceGlyph));
                if (instances.Length > 0) instances = " — instances" + instances;
                sb.Append($"\n**{summary.WorldName} / {summary.Expansion}** — {summary.ObservedKills} observed kills{instances}\n");
                sb.Append($"Overall respawn range <t:{openUnix}:t> → <t:{capUnix}:t>\n");
            }
        }

        var unknownTimers = entries.Where(e => !e.Sniped && !e.HasWindow).ToList();
        if (unknownTimers.Count > 0)
        {
            sb.Append("\n**Observed kills without fixed timers**\n");
            AppendEntries(sb, unknownTimers);
        }

        // Marks found already gone get their own section rather than a note on
        // an ordinary line. Their leading time is when the train arrived to
        // find them missing, not a kill, and their window is wider because of
        // it — mixed into the chronological list they read as kills, which is
        // the confusion this whole thing exists to remove.
        var sniped = entries.Where(e => e.Sniped).ToList();
        if (sniped.Count > 0)
        {
            sb.Append("\n**Sniped** (found gone — time is when the train got there, window spans last seen alive to then)\n");
            AppendEntries(sb, sniped);
        }

        var unfinished = marks.Where(m => !m.Dead).ToList();
        var unknown = marks.Where(m => m.Dead && m.DeathObservedAtUtc is null && m.SnipedAtUtc is null).ToList();
        foreach (var (title, rows) in new[] { ("Unfinished / still alive", unfinished), ("Found dead — kill time unknown", unknown) })
            if (rows.Count > 0)
                sb.Append($"\n**{title}**\n" + string.Join("\n", rows.Select(m => $"{m.Name}{ExpansionData.InstanceGlyph(m.Instance)} — {m.WorldName} (world {m.WorldId})")) + "\n");
        var neverSeen = TrainReport.BuildSniped(marks);
        if (neverSeen.Count > 0)
        {
            sb.Append("\n**Missing / not seen this train** (respawn time unknown)\n");
            sb.Append(string.Join("\n", neverSeen.Select(s => $"**{s.WorldName} / {s.Expansion}**: {string.Join(", ", s.Marks)}")));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// One run of entries, split into expansion blocks.
    ///
    /// Shared by the killed and sniped sections so the two are formatted by the
    /// same code and cannot drift apart — the only difference between them
    /// should be which marks are in them.
    /// </summary>
    private static void AppendEntries(StringBuilder sb, List<TrainReportEntry> entries)
    {
        string? lastExpansion = null;

        foreach (var entry in entries)
        {
            if (entry.Expansion != lastExpansion)
            {
                if (lastExpansion != null) sb.Append('\n');
                sb.Append($"**{entry.Expansion}**\n");
                lastExpansion = entry.Expansion;
            }

            var killUnix = new DateTimeOffset(entry.KillTimeUtc).ToUnixTimeSeconds();

            if (!entry.HasWindow)
            {
                sb.Append($"<t:{killUnix}:t> — {entry.DisplayName} — no fixed respawn timer\n");
                continue;
            }

            var openUnix = new DateTimeOffset(entry.WindowOpensUtc!.Value).ToUnixTimeSeconds();
            var capUnix = new DateTimeOffset(entry.WindowCapsUtc!.Value).ToUnixTimeSeconds();
            var instanceGlyph = ExpansionData.InstanceGlyph(entry.Instance);
            sb.Append($"<t:{killUnix}:t> — {entry.Location} — {entry.DisplayName}{instanceGlyph} — window <t:{openUnix}:t> → <t:{capUnix}:t>\n");
        }
    }

    /// <summary>
    /// Preserves every character, preferring line boundaries. Oversized individual
    /// lines are split too, without separating a UTF-16 surrogate pair.
    /// </summary>
    internal static List<string> ChunkByLength(string body, int limit)
    {
        if (limit < 2) throw new ArgumentOutOfRangeException(nameof(limit));
        var chunks = new List<string>();
        for (var start = 0; start < body.Length;)
        {
            var length = Math.Min(limit, body.Length - start);
            if (start + length < body.Length)
            {
                var newline = body.LastIndexOf('\n', start + length - 1, length);
                if (newline >= start) length = newline - start + 1;
                else if (char.IsHighSurrogate(body[start + length - 1]) && char.IsLowSurrogate(body[start + length])) length--;
            }
            chunks.Add(body.Substring(start, length));
            start += length;
        }
        if (chunks.Count == 0) chunks.Add(string.Empty);
        return chunks;
    }
}
