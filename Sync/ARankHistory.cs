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
    public bool Uncertain { get; set; }
}

public static class ARankHistory
{
    public static (DateTime? Opens, DateTime? Ends) Window(ARankKill? kill, double minHours, double maxHours, DateTime? restart)
    {
        var lower = kill?.Uncertain == true ? kill.LastAliveAt : kill?.At;
        return lower is { } alive && alive > DateTime.UnixEpoch && alive <= kill!.At && (restart is null || alive > restart)
            ? (alive.AddHours(minHours), kill.At.AddHours(maxHours)) : (null, null);
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
            if (ExpansionData.Lookup(kill.NameId) is null || kill.WorldId == 0 || kill.At > now
                || now - kill.At > TimeSpan.FromDays(14)) continue;
            var old = history.FirstOrDefault(k => k.NameId == kill.NameId && k.WorldId == kill.WorldId && k.Instance == kill.Instance);
            if (old is not null && (old.At > kill.At || (old.At == kill.At && (!old.Uncertain || (kill.Uncertain && (kill.LastAliveAt is null || old.LastAliveAt >= kill.LastAliveAt)))))) continue;
            if (old is not null) history.Remove(old);
            history.Add(new() { NameId = kill.NameId, WorldId = kill.WorldId, Instance = kill.Instance, At = kill.At, LastAliveAt = kill.LastAliveAt is { } alive && alive > DateTime.UnixEpoch && alive <= kill.At ? alive : null, Uncertain = kill.Uncertain });
            changed = true;
        }
        return history.RemoveAll(k => now - k.At > TimeSpan.FromDays(14)) > 0 || changed;
    }
}
