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
            if (ExpansionData.Lookup(kill.NameId) is null || kill.WorldId == 0 || kill.Instance > 9 || kill.At > now
                || now - kill.At > TimeSpan.FromDays(14)) continue;
            var old = history.FirstOrDefault(k => k.NameId == kill.NameId && k.WorldId == kill.WorldId && k.Instance == kill.Instance);
            var reportedAt = kill.ReportedAt is { } reported && reported >= kill.At && reported <= now ? reported : (DateTime?)null;
            var evidenceAt = reportedAt ?? kill.At;
            var oldEvidenceAt = old?.ReportedAt is { } oldReported && oldReported >= old.At && oldReported <= now
                ? oldReported : old?.At;
            if (old is not null && (oldEvidenceAt > evidenceAt || (oldEvidenceAt == evidenceAt
                && (old.ReportedAt is not null && reportedAt is null || !old.Uncertain
                    || kill.Uncertain && (kill.LastAliveAt is null || old.LastAliveAt >= kill.LastAliveAt))))) continue;
            if (old is not null) history.Remove(old);
            history.Add(new() { NameId = kill.NameId, WorldId = kill.WorldId, Instance = kill.Instance, At = kill.At,
                LastAliveAt = kill.LastAliveAt is { } alive && alive > DateTime.UnixEpoch && alive <= kill.At ? alive : null,
                EarliestKilledAt = kill.EarliestKilledAt is { } earliest && earliest > DateTime.UnixEpoch && earliest <= kill.At ? earliest : null,
                Uncertain = kill.Uncertain, ReportedAt = reportedAt });
            changed = true;
        }
        return history.RemoveAll(k => now - k.At > TimeSpan.FromDays(14)) > 0 || changed;
    }
}
