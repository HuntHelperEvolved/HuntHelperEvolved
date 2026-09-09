using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

public record TrainReportEntry(
    DateTime KillTimeUtc,
    string Expansion,
    string? Location,
    string Name,
    uint Instance,
    double? MinHours,
    double? MaxHours,
    bool Sniped,
    DateTime LastAliveUtc,
    uint WorldId = 0,
    string WorldName = "")
{
    public string DisplayName => string.IsNullOrEmpty(WorldName) ? $"{Name} — world {WorldId}" : $"{Name} — {WorldName}";

    /// <summary>Whether a respawn window can be worked out for this mark at all.</summary>
    public bool HasWindow => Location != null && MinHours != null && MaxHours != null;

    /// <summary>
    /// The earliest the mark can come back.
    ///
    /// For one we watched die, that is its kill time plus the minimum. For one
    /// found already sniped it is the last time it was seen ALIVE plus the
    /// minimum: it could have died the moment we last looked at it, and a
    /// window measured from when we noticed it was gone would send people to
    /// stand there after it had already respawned.
    /// </summary>
    public DateTime? WindowOpensUtc =>
        MinHours == null ? null : (Sniped ? LastAliveUtc : KillTimeUtc).AddHours(MinHours.Value);

    /// <summary>
    /// The latest it can come back: the kill time plus the maximum. For a
    /// sniped mark that kill time is the moment it was found gone, which is
    /// the latest it can possibly have died — so the same arithmetic gives the
    /// honest far edge without a special case.
    /// </summary>
    public DateTime? WindowCapsUtc =>
        MaxHours == null ? null : KillTimeUtc.AddHours(MaxHours.Value);
}

/// <summary>
/// Builds the kill-ordered entry list and Assumed Sniped groups from a set of
/// tracked marks. Pure data — no Discord formatting and no ImGui — so both the
/// webhook message and the in-game "Marks Slain" preview read from exactly the
/// same computation and can never show different results.
/// </summary>
public static class TrainReport
{
    /// <summary>
    /// The expansion label used for a mark this table has never heard of.
    /// Matches the text such an entry reports, so the two cannot drift.
    /// </summary>
    public const string UnknownExpansion = "No fixed timer";

    /// <summary>
    /// A kill the train actually landed, as opposed to a mark it merely found
    /// gone. This is the same test the posted report's kill count uses, so the
    /// count in the header and the legs in the body always agree about what
    /// counts as a kill.
    /// </summary>
    public static bool IsObservedKill(TrackedMark mark) =>
        mark.Dead && mark.DeathObservedAtUtc != null && mark.SnipedAtUtc == null;

    private static string ExpansionOf(TrackedMark mark) =>
        ExpansionData.Lookup(mark.ModelId)?.Expansion ?? UnknownExpansion;

    /// <summary>
    /// The expansions a report covers: the ones the train genuinely killed
    /// something in.
    ///
    /// A conductor scouting five expansions and running three is ordinary, and
    /// the two they skipped have no business in the report - their marks are
    /// still alive, and listing them as unfinished says only that a train that
    /// was never going there did not go there. Deriving this from the kills
    /// rather than asking means there is nothing to tick before posting and
    /// nothing to forget to tick.
    ///
    /// Being found already dead deliberately does NOT qualify. A leg the train
    /// walked and found stripped is one it did not run, and the marks stay put
    /// for a later train rather than being reported and cleared here.
    /// </summary>
    public static HashSet<string> ReportedExpansions(List<TrackedMark> marks) =>
        marks.Where(IsObservedKill).Select(ExpansionOf).ToHashSet();

    /// <summary>
    /// The subset of the train a report covers - every mark, dead or alive,
    /// belonging to an expansion with a kill in it.
    ///
    /// Alive marks are kept rather than dropped because the report's own
    /// "Unfinished / still alive" section is worth having for a leg that WAS
    /// run: it says which of that leg's marks the train did not get to. The
    /// filter is per expansion, never per mark.
    /// </summary>
    public static List<TrackedMark> ForReport(List<TrackedMark> marks)
    {
        var reported = ReportedExpansions(marks);
        return marks.Where(m => reported.Contains(ExpansionOf(m))).ToList();
    }

    /// <summary>
    /// The rows a posted report consumes, and so the rows that come off the
    /// train once it is sent: the dead ones in the expansions it covered.
    ///
    /// Everything else stays - the live marks of a reported leg included,
    /// since a mark still standing has not been reported as anything and a
    /// later train can still take it.
    /// </summary>
    public static List<TrackedMark> SubmittedMarks(List<TrackedMark> marks) =>
        ForReport(marks).Where(m => m.Dead).ToList();

    /// <summary>
    /// Splits the S-rank watches into the ones this report carries and the
    /// ones that outlive it.
    ///
    /// A watch belongs to the leg its mark is on, so reporting Dawntrail
    /// publishes and consumes the Dawntrail watch while a Shadowbringers watch
    /// stays up for the train that will eventually run Shadowbringers. The
    /// alternative - clearing the lot - throws away a check somebody is still
    /// actively sitting on.
    ///
    /// A label that resolves to no known S rank is treated as belonging to
    /// this report. It is reported and cleared, which is what every watch did
    /// before this split existed; the alternative would strand a watch that no
    /// report could ever clear.
    /// </summary>
    public static (List<FlagEntry> Reported, List<FlagEntry> Kept) SplitWatches(
        IEnumerable<FlagEntry>? watches, HashSet<string> reportedExpansions)
    {
        var reported = new List<FlagEntry>();
        var kept = new List<FlagEntry>();

        foreach (var watch in watches ?? Enumerable.Empty<FlagEntry>())
        {
            var expansion = SRankData.ExpansionOfWatch(watch.Label);
            if (expansion == null || reportedExpansions.Contains(expansion)) reported.Add(watch);
            else kept.Add(watch);
        }

        return (reported, kept);
    }

    public static List<TrainReportEntry> BuildEntries(List<TrackedMark> marks)
    {
        return marks
            .Where(m => m.Dead && (m.SnipedAtUtc.HasValue || m.DeathObservedAtUtc.HasValue))
            .Select(m =>
            {
                var info = ExpansionData.Lookup(m.ModelId);

                // Sniped wins over an observed death: the two should never both
                // be set, and if a conductor has managed it, the one they
                // clicked deliberately is the one they meant.
                var killTime = EnsureUtc(m.SnipedAtUtc ?? m.DeathObservedAtUtc!.Value);
                return new TrainReportEntry(
                    killTime,
                    info?.Expansion ?? UnknownExpansion,
                    info?.Location,
                    m.Name,
                    m.Instance,
                    info?.MinHours,
                    info?.MaxHours,
                    m.SnipedAtUtc != null,
                    EnsureUtc(m.LastSeenUtc), m.WorldId, m.WorldName);
            })
            .OrderBy(e => e.KillTimeUtc)
            .ToList();
    }

    /// <summary>
    /// Named marks belonging to any expansion actually represented in this train
    /// that were never observed at all — most likely killed by someone else
    /// before the train got there.
    ///
    /// Distinct from a mark marked sniped on the list, which WAS seen: that one
    /// has a last-seen-alive time and so has a window worth publishing, while
    /// these have nothing to measure from and are named only so nobody assumes
    /// the train simply forgot them.
    /// </summary>
    public static List<(string Expansion, List<string> Marks)> BuildSniped(List<TrackedMark> marks)
    {
        var seenModelIds = marks.Select(m => m.ModelId).ToHashSet();
        var touchedExpansions = marks
            .Select(m => ExpansionData.Lookup(m.ModelId)?.Expansion)
            .Where(e => e != null)
            .Select(e => e!)
            .Distinct();

        var result = new List<(string, List<string>)>();
        foreach (var expansion in touchedExpansions)
        {
            var sniped = ExpansionData.ModelIdToMark
                .Where(kv => kv.Value.Expansion == expansion && !seenModelIds.Contains(kv.Key))
                .OrderBy(kv => kv.Value.ZoneOrder)
                .Select(kv => $"{kv.Value.Name} ({kv.Value.Location})")
                .ToList();

            if (sniped.Count > 0)
                result.Add((expansion, sniped));
        }

        return result;
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
