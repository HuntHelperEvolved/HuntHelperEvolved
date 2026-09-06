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
    DateTime LastAliveUtc)
{
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
    public static List<TrainReportEntry> BuildEntries(List<TrackedMark> marks)
    {
        return marks
            .Select(m =>
            {
                var info = ExpansionData.Lookup(m.ModelId);

                // Sniped wins over an observed death: the two should never both
                // be set, and if a conductor has managed it, the one they
                // clicked deliberately is the one they meant.
                var killTime = EnsureUtc(m.SnipedAtUtc ?? m.DeathObservedAtUtc ?? m.LastSeenUtc);
                return new TrainReportEntry(
                    killTime,
                    info?.Expansion ?? "No fixed timer",
                    info?.Location,
                    m.Name,
                    m.Instance,
                    info?.MinHours,
                    info?.MaxHours,
                    m.SnipedAtUtc != null,
                    EnsureUtc(m.LastSeenUtc));
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
