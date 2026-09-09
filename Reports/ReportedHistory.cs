using System;
using System.Collections.Generic;
using System.Linq;
using HuntHelperEvolved.Sync;
namespace HuntHelperEvolved;

/// <summary>Forget reported deaths without consuming a later cycle of the same mark.</summary>
public static class ReportedHistory
{
    public static List<(uint NameId, uint Instance, uint WorldId)> CompletedKeys(IEnumerable<TrackedMark> history, IEnumerable<ReportedMark> reported)
    {
        var cutoffs = reported.GroupBy(r=>r.Key).ToDictionary(g=>g.Key,g=>g.Max(r=>r.Through));
        return history.Where(m=>m.Dead && cutoffs.TryGetValue(m.Key,out var through)
            && m.LastSeenUtc<=through && (m.DeathObservedAtUtc is null || m.DeathObservedAtUtc<=through)
            && (m.SnipedAtUtc is null || m.SnipedAtUtc<=through)).Select(m=>m.Key).Distinct().ToList();
    }
}
