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

public sealed record AssumedSnipedGroup(uint WorldId, string WorldName, string Expansion, List<string> Marks);

/// <summary>
/// The outer bounds of the individual respawn windows for observed kills on
/// one world and expansion. This is not a shared window for every mark.
/// </summary>
public sealed record TrainWindowSummary(uint WorldId, string WorldName, string Expansion,
    int ObservedKills, DateTime EarliestWindowOpensUtc, DateTime LatestWindowCapsUtc, List<uint> Instances);

/// <summary>Shared report selection and kill-window calculations for Discord and the preview.</summary>
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
        ReportedLegs(marks).Select(leg => leg.Expansion).ToHashSet();

    public static HashSet<(uint WorldId, string Expansion)> ReportedLegs(IEnumerable<TrackedMark> marks) =>
        marks.Where(IsObservedKill).Select(m => (m.WorldId, ExpansionOf(m))).ToHashSet();

    /// <summary>
    /// The subset of the train a report covers - every mark, dead or alive,
    /// belonging to an expansion with a kill in it.
    ///
    /// Alive marks are kept rather than dropped because the report's own
    /// "Unfinished / still alive" section is worth having for a leg that WAS
    /// run: it says which of that leg's marks the train did not get to. The
    /// filter is per world and expansion, so an unrun world stays out of the report.
    /// </summary>
    public static List<TrackedMark> ForReport(List<TrackedMark> marks)
    {
        var reported = ReportedLegs(marks);
        return marks.Where(m => reported.Contains((m.WorldId, ExpansionOf(m)))).ToList();
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
    /// Unknown labels follow their world's reported leg. Legacy watches with no
    /// world can be assigned only when the train contains a single world.
    /// </summary>
    public static (List<FlagEntry> Reported, List<FlagEntry> Kept) SplitWatches(
        IEnumerable<FlagEntry>? watches, HashSet<(uint WorldId, string Expansion)> reportedLegs,
        IEnumerable<uint>? trainWorlds = null)
    {
        var reported = new List<FlagEntry>();
        var kept = new List<FlagEntry>();

        var worlds = (trainWorlds ?? reportedLegs.Select(leg => leg.WorldId)).Distinct().ToList();
        foreach (var watch in watches ?? Enumerable.Empty<FlagEntry>())
        {
            var expansion = SRankData.ExpansionOfWatch(watch.Label);
            var world = watch.WorldId == 0 && worlds.Count == 1 ? worlds[0] : watch.WorldId;
            if (expansion == null ? reportedLegs.Any(leg => leg.WorldId == world)
                : reportedLegs.Contains((world, expansion))) reported.Add(watch);
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

    public static List<TrainWindowSummary> BuildWindowSummaries(List<TrackedMark> marks) =>
        BuildEntries(marks).Where(e => !e.Sniped && e.HasWindow)
            .GroupBy(e => (e.WorldId, e.Expansion))
            .Select(group => new TrainWindowSummary(group.Key.WorldId,
                group.Select(e => e.WorldName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                    ?? $"World {group.Key.WorldId}",
                group.Key.Expansion, group.Count(),
                group.Min(e => e.WindowOpensUtc!.Value), group.Max(e => e.WindowCapsUtc!.Value),
                group.Select(e => e.Instance).Distinct().OrderBy(instance => instance).ToList()))
            .ToList();

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
    public static List<AssumedSnipedGroup> BuildSniped(List<TrackedMark> marks)
    {
        var result = new List<AssumedSnipedGroup>();
        foreach (var world in marks.GroupBy(m => m.WorldId))
        {
            var seen = world.Select(m => m.ModelId).ToHashSet();
            var expansions = world.Select(m => ExpansionData.Lookup(m.ModelId)?.Expansion)
                .OfType<string>().Distinct();
            var worldName = world.Select(m => m.WorldName).FirstOrDefault(name => !string.IsNullOrEmpty(name))
                            ?? $"World {world.Key}";
            foreach (var expansion in expansions)
            {
                var missing = ExpansionData.ModelIdToMark
                    .Where(kv => kv.Value.Expansion == expansion && !seen.Contains(kv.Key))
                    .OrderBy(kv => kv.Value.ZoneOrder)
                    .Select(kv => $"{kv.Value.Name} ({kv.Value.Location})").ToList();
                if (missing.Count > 0) result.Add(new(world.Key, worldName, expansion, missing));
            }
        }
        return result;
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
