using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed class ARankKill
{
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public DateTime At { get; set; }
    public DateTime? LastAliveAt { get; set; }
    /// <summary>Earliest possible death inferred from the preceding spawn window, without a live sighting.</summary>
    public DateTime? EarliestKilledAt { get; set; }
    public bool Uncertain { get; set; }
    /// <summary>Local receipt of a manual correction, so older shared evidence cannot undo it.</summary>
    public DateTime? ReportedAt { get; set; }
    /// <summary>Server receipt of the accepted history update, separate from the reporter's train timestamp.</summary>
    public DateTime? UpdatedAt { get; set; }
    public long Revision { get; set; }
    /// <summary>Local identity of the server whose revision was accepted.</summary>
    public string ServerId { get; set; } = string.Empty;
}

public static class ARankHistory
{
    public static (DateTime? Opens, DateTime? Ends) Window(ARankKill? kill, double minHours, double maxHours, DateTime? restart)
    {
        if (kill is null || kill.At <= DateTime.UnixEpoch || restart is { } ended && kill.At <= ended)
            return (null, null);
        var lower = kill.Uncertain ? kill.LastAliveAt ?? kill.EarliestKilledAt : kill.At;
        var opens = lower is { } earliest && earliest > DateTime.UnixEpoch && earliest <= kill.At
            && (restart is null || earliest > restart) ? earliest.AddHours(minHours) : (DateTime?)null;
        return (opens, kill.At.AddHours(maxHours));
    }

    public static IEnumerable<ARankKill> FromMarks(IEnumerable<SyncMark> marks) => marks
        .Where(m => m.Dead && !m.IsCustom && (m.SnipedAt ?? m.DeathAt) is not null)
        .Select(m => new ARankKill { NameId = m.NameId, WorldId = m.WorldId, Instance = m.Instance,
            At = (m.SnipedAt ?? m.DeathAt)!.Value, LastAliveAt = m.SnipedAt is not null && m.LastSeen <= m.SnipedAt ? m.LastSeen : null, Uncertain = m.SnipedAt is not null });

    public static bool Merge(List<ARankKill> history, IEnumerable<ARankKill> incoming, DateTime now)
    {
        var changed = false;
        foreach (var kill in incoming)
        {
            var updatedAt = kill.Revision > 0 ? ValidAcceptedAt(kill.UpdatedAt, kill.At, now) : null;
            // Match the server's small clock-drift allowance for acknowledged
            // records, without admitting future raw train observations.
            var latest = updatedAt is not null ? now.AddSeconds(10) : now;
            if (ExpansionData.Lookup(kill.NameId) is null || kill.WorldId == 0 || kill.Instance > 9
                || kill.At <= DateTime.UnixEpoch || kill.At > latest
                || now - kill.At > TimeSpan.FromDays(14)) continue;
            var old = history.FirstOrDefault(k => k.NameId == kill.NameId && k.WorldId == kill.WorldId && k.Instance == kill.Instance);
            var reportedAt = ValidReceipt(kill.ReportedAt, kill.At, latest);
            var next = new ARankKill { NameId = kill.NameId, WorldId = kill.WorldId, Instance = kill.Instance, At = kill.At,
                LastAliveAt = kill.LastAliveAt is { } alive && alive > DateTime.UnixEpoch && alive <= kill.At ? alive : null,
                EarliestKilledAt = kill.EarliestKilledAt is { } earliest && earliest > DateTime.UnixEpoch && earliest <= kill.At ? earliest : null,
                Uncertain = kill.Uncertain, ReportedAt = reportedAt, UpdatedAt = updatedAt,
                Revision = updatedAt is not null ? Math.Max(0, kill.Revision) : 0,
                ServerId = kill.ServerId ?? string.Empty };
            if (old is not null && !ShouldReplace(old, next, now)) continue;
            if (old is not null) history.Remove(old);
            history.Add(next);
            changed = true;
        }
        return history.RemoveAll(k => now - k.At > TimeSpan.FromDays(14)) > 0 || changed;
    }

    private static DateTime? ValidReceipt(DateTime? value, DateTime at, DateTime now) =>
        value is { } receipt && receipt >= at && receipt <= now ? receipt : null;

    private static DateTime? ValidAcceptedAt(DateTime? value, DateTime at, DateTime now) =>
        value is { } receipt && receipt > DateTime.UnixEpoch && receipt <= now.AddSeconds(10)
        && at - receipt <= TimeSpan.FromSeconds(10) && now - receipt <= TimeSpan.FromDays(14) ? receipt : null;

    private static bool ShouldReplace(ARankKill old, ARankKill next, DateTime now)
    {
        var oldUpdated = old.Revision > 0 ? ValidAcceptedAt(old.UpdatedAt, old.At, now) : null;
        var oldReported = ValidReceipt(old.ReportedAt, old.At, oldUpdated is not null ? now.AddSeconds(10) : now);
        var oldCanonical = old.Revision > 0 && oldUpdated is not null;
        var nextCanonical = next.Revision > 0 && next.UpdatedAt is not null;
        var oldEvidence = oldUpdated is { } accepted && accepted > (oldReported ?? old.At)
            ? accepted : oldReported ?? old.At;
        // A late server capture of a train corpse is still old death evidence,
        // even when its receipt or revision is newer than a manual correction.
        if (oldReported is not null && next.ReportedAt is null && next.At <= oldEvidence) return false;
        // A server revision is immutable. Duplicate acknowledgements and older
        // welcome snapshots must not rewrite metadata or loop configuration saves.
        if (oldCanonical && nextCanonical && old.ServerId == next.ServerId) return next.Revision > old.Revision;

        // The previous request can be accepted after the user has already made
        // another local correction. Its newer server receipt is not a newer edit.
        if (!oldCanonical && old.UpdatedAt is null && oldReported is { } pending
            && nextCanonical && (next.ReportedAt ?? next.At) < pending) return false;

        var nextEvidence = next.UpdatedAt is { } receipt && receipt > (next.ReportedAt ?? next.At)
            ? receipt : next.ReportedAt ?? next.At;
        if (nextEvidence != oldEvidence) return nextEvidence > oldEvidence;

        // A canonical acknowledgement adds authoritative ordering to the same
        // local report. Conversely, raw train echoes cannot erase a correction.
        if (nextCanonical != oldCanonical) return nextCanonical;
        if ((next.ReportedAt is not null) != (oldReported is not null)) return next.ReportedAt is not null;
        if (!old.Uncertain) return false;
        if (!next.Uncertain) return true;
        if (next.LastAliveAt is { } alive) return old.LastAliveAt is null || alive > old.LastAliveAt;
        return old.LastAliveAt is null && next.EarliestKilledAt is { } earliest
            && (old.EarliestKilledAt is null || earliest > old.EarliestKilledAt);
    }
}
