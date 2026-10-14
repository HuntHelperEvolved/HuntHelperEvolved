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
    public string Source { get; set; } = string.Empty;
    /// <summary>Latest semantic death evidence; server receipt time never extends it.</summary>
    public DateTime? EvidenceThrough { get; set; }
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
    public static string SourceOf(ARankKill kill) => kill.Source is "observed" or "bear" or "snipe"
        ? kill.Source : kill.Uncertain || kill.ReportedAt is not null ? "snipe" : "observed";

    public static DateTime EvidenceTime(ARankKill kill)
    {
        var through = kill.EvidenceThrough is { } evidence && evidence > kill.At ? evidence : kill.At;
        return kill.ReportedAt is { } reported && reported > through ? reported : through;
    }

    /// <summary>Exact reported timing for a sniped train row, without claiming an observed kill.</summary>
    public static DateTime? KnownSnipedKillAt(IEnumerable<ARankKill> history, uint nameId, uint worldId,
        uint instance, DateTime? snipedAt)
    {
        if (snipedAt is null) return null;
        var kill = history.FirstOrDefault(k => k.NameId == nameId && k.WorldId == worldId && k.Instance == instance);
        if (kill is null || kill.Uncertain || kill.At <= DateTime.UnixEpoch) return null;
        if (SourceOf(kill) == "bear") return kill.At <= snipedAt && snipedAt <= EvidenceTime(kill) ? kill.At : null;
        if (kill.ReportedAt is not { } reported || reported < kill.At) return null;
        var through = kill.UpdatedAt is { } accepted && accepted > reported ? accepted : reported;
        // The train echo may still contain the earlier found-missing time. A
        // later snipe describes new evidence and must not reuse this correction.
        return kill.At <= snipedAt && snipedAt <= through ? kill.At : null;
    }

    public static void ApplyKnownSnipeTimes(IEnumerable<TrackedMark> marks, IEnumerable<ARankKill> history)
    {
        var kills = history.ToList();
        foreach (var mark in marks)
        {
            mark.ReportedKillAtUtc = mark.Dead
                ? KnownSnipedKillAt(kills, mark.ModelId, mark.WorldId, mark.Instance, mark.SnipedAtUtc) : null;
            if (mark.ReportedKillAtUtc is not null)
            {
                var kill = kills.First(k => k.NameId == mark.ModelId && k.WorldId == mark.WorldId && k.Instance == mark.Instance);
                mark.DeathSource = SourceOf(kill);
            }
        }
    }

    public static (DateTime? Opens, DateTime? Ends) Window(ARankKill? kill, double minHours, double maxHours, DateTime? restart)
    {
        if (kill is null || kill.At <= DateTime.UnixEpoch || restart is { } ended && kill.At <= ended)
            return (null, null);
        var lower = kill.Uncertain ? kill.LastAliveAt ?? kill.EarliestKilledAt : kill.At;
        var opens = lower is { } earliest && earliest > DateTime.UnixEpoch && earliest <= kill.At
            && (restart is null || earliest > restart) ? earliest.AddHours(minHours) : (DateTime?)null;
        return (opens, kill.At.AddHours(maxHours));
    }

    /// <summary>Keep a newly witnessed local death while its stronger evidence is still being sent.</summary>
    internal static bool PreserveObservedDeath(DetectedMark local, SyncMark incoming)
    {
        if (!local.Dead || local.SnipedAtUtc is not null || local.DeathObservedAtUtc is not { } observed
            || local.DeathSource == "bear" || !incoming.Dead || incoming.DeathSource != "bear"
            || (incoming.SnipedAt ?? incoming.DeathAt) is not { } external) return false;
        if (incoming.LastSeen > observed && incoming.LastSeen <= external) return false;
        return external < observed.AddHours(ExpansionData.Lookup(local.NameId)?.MinHours ?? 0);
    }

    public static IEnumerable<ARankKill> FromMarks(IEnumerable<SyncMark> marks) => marks
        .Where(m => m.Dead && !m.IsCustom && (m.SnipedAt ?? m.DeathAt) is not null)
        .Select(m => new ARankKill { NameId = m.NameId, WorldId = m.WorldId, Instance = m.Instance,
            At = (m.SnipedAt ?? m.DeathAt)!.Value,
            LastAliveAt = m.SnipedAt is not null && m.LastSeen <= m.SnipedAt ? m.LastSeen : null,
            Source = m.DeathSource is "observed" or "bear" or "snipe" ? m.DeathSource : m.SnipedAt is not null ? "snipe" : "observed",
            EvidenceThrough = m.SnipedAt ?? m.DeathAt,
            Uncertain = m.SnipedAt is not null && m.DeathSource != "bear" });

    public static bool Merge(List<ARankKill> history, IEnumerable<ARankKill> incoming, DateTime now, DateTime? serverNow = null,
        IEnumerable<ARankSighting>? sightings = null)
    {
        var changed = false;
        foreach (var kill in incoming)
        {
            var acceptedNow = serverNow ?? now;
            var updatedAt = kill.Revision > 0 ? ValidAcceptedAt(kill.UpdatedAt, kill.At, acceptedNow) : null;
            // Match the server's small clock-drift allowance for acknowledged
            // records, without admitting future raw train observations.
            var referenceNow = updatedAt is not null ? acceptedNow : now;
            var latest = updatedAt is not null ? referenceNow.AddSeconds(10) : referenceNow;
            if (ExpansionData.Lookup(kill.NameId) is null || kill.WorldId == 0 || kill.Instance > 9
                || kill.At <= DateTime.UnixEpoch || kill.At > latest
                || referenceNow - kill.At > TimeSpan.FromDays(14)) continue;
            var old = history.FirstOrDefault(k => k.NameId == kill.NameId && k.WorldId == kill.WorldId && k.Instance == kill.Instance);
            var reportedAt = ValidReceipt(kill.ReportedAt, kill.At, latest);
            var next = new ARankKill { NameId = kill.NameId, WorldId = kill.WorldId, Instance = kill.Instance, At = kill.At,
                LastAliveAt = kill.LastAliveAt is { } alive && alive > DateTime.UnixEpoch && alive <= kill.At ? alive : null,
                EarliestKilledAt = kill.EarliestKilledAt is { } earliest && earliest > DateTime.UnixEpoch && earliest <= kill.At ? earliest : null,
                Uncertain = kill.Uncertain, ReportedAt = reportedAt, UpdatedAt = updatedAt, Source = SourceOf(kill),
                EvidenceThrough = ValidReceipt(kill.EvidenceThrough, kill.At, latest),
                Revision = updatedAt is not null ? Math.Max(0, kill.Revision) : 0,
                ServerId = kill.ServerId ?? string.Empty };
            if (old is not null && !ShouldReplace(old, next, sightings, kill.Source == "observed" && !kill.Uncertain && kill.ReportedAt is null)) continue;
            if (old is not null)
            {
                // Resolving a missing report must retain its semantic cutoff,
                // otherwise the old train echo can revive that same missing report.
                if (SourceOf(old) == "snipe" && next.Source == "bear" && EvidenceTime(old) > EvidenceTime(next))
                    next.EvidenceThrough = EvidenceTime(old);
                history.Remove(old);
            }
            history.Add(next);
            changed = true;
        }
        return history.RemoveAll(k => (k.Revision > 0 && k.UpdatedAt is not null ? serverNow ?? now : now)
            - k.At > TimeSpan.FromDays(14)) > 0 || changed;
    }

    private static DateTime? ValidReceipt(DateTime? value, DateTime at, DateTime now) =>
        value is { } receipt && receipt >= at && receipt <= now ? receipt : null;

    private static DateTime? ValidAcceptedAt(DateTime? value, DateTime at, DateTime now) =>
        value is { } receipt && receipt > DateTime.UnixEpoch && receipt <= now.AddSeconds(10)
        && at - receipt <= TimeSpan.FromSeconds(10) && now - receipt <= TimeSpan.FromDays(14) ? receipt : null;

    private static int Priority(string source) => source == "observed" ? 3 : source == "bear" ? 2 : 1;

    private static bool ShouldReplace(ARankKill old, ARankKill next, IEnumerable<ARankSighting>? sightings, bool observedEvidence)
    {
        // This record already passed validation when received. A subsequent
        // local capture must not revoke its ordering because this computer's
        // clock differs from the server's clock (or was adjusted meanwhile).
        var oldUpdated = old.Revision > 0 && old.UpdatedAt is { } acceptedAt
            && acceptedAt > DateTime.UnixEpoch && old.At - acceptedAt <= TimeSpan.FromSeconds(10)
            ? acceptedAt : (DateTime?)null;
        var oldReported = ValidReceipt(old.ReportedAt, old.At, DateTime.MaxValue);
        var oldCanonical = old.Revision > 0 && oldUpdated is not null;
        var nextCanonical = next.Revision > 0 && next.UpdatedAt is not null;
        var oldEvidence = oldUpdated is { } accepted && accepted > (oldReported ?? old.At)
            ? accepted : oldReported ?? old.At;
        // A late server capture of a train corpse is still old death evidence,
        // even when its receipt or revision is newer than a manual correction.
        var oldSource = SourceOf(old);
        var nextSource = SourceOf(next);
        // A Bear correction can resolve a backdated manual report. Repeated
        // train corpse captures cannot, even if a later server receipt exists.
        if (oldReported is not null && next.ReportedAt is null && nextSource != "bear" && !observedEvidence && next.At <= oldEvidence) return false;
        // A server revision is immutable. Duplicate acknowledgements and older
        // welcome snapshots must not rewrite metadata or loop configuration saves.
        if (oldCanonical && nextCanonical && old.ServerId == next.ServerId) return next.Revision > old.Revision;

        var cutoff = EvidenceTime(old);
        var freshLive = sightings?.Any(s => s.Alive && s.NameId == next.NameId && s.WorldId == next.WorldId
            && s.Instance == next.Instance && s.At > cutoff && s.At <= next.At) == true;
        var minHours = ExpansionData.Lookup(next.NameId)?.MinHours ?? 0;
        var sameCycle = !freshLive && (next.At <= cutoff || next.At < old.At.AddHours(minHours));
        if (sameCycle && !old.Uncertain && Priority(nextSource) < Priority(oldSource)) return false;
        if ((nextSource == "bear" || observedEvidence) && oldSource == "snipe")
        {
            // A factual living observation after the candidate proves that
            // this report belongs to an older lifecycle, rather than resolving it.
            if (old.LastAliveAt > next.At || old.EarliestKilledAt > next.At || sightings?.Any(s => s.Alive && s.NameId == next.NameId
                && s.WorldId == next.WorldId && s.Instance == next.Instance && s.At > next.At) == true) return false;
            return true;
        }
        if (sameCycle && nextSource == "observed" && oldSource == "bear")
        {
            if (next.At <= old.At.AddHours(-minHours) || old.LastAliveAt > next.At || old.EarliestKilledAt > next.At
                || sightings?.Any(s => s.Alive && s.NameId == next.NameId && s.WorldId == next.WorldId
                    && s.Instance == next.Instance && s.At > next.At) == true) return false;
            return true;
        }

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
