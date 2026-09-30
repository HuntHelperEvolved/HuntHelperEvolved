using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

/// <summary>Apply an observer batch without losing corpse evidence to a later removal in the same inbox drain.</summary>
internal static class VisibleObservationBatch
{
    internal static void Apply(IEnumerable<VisibleMark> reports, IEnumerable<SyncKey> removed,
        IDictionary<(uint NameId, uint Instance, uint WorldId, uint TerritoryId, uint EntityId), VisibleMark> current,
        ActiveMarkGrace grace, DateTime receivedAt,
        Action<(uint NameId, uint Instance, uint WorldId), DateTime>? rememberDeath)
    {
        foreach (var report in reports)
        {
            if (report.Mark.HpPercent == 0) rememberDeath?.Invoke(report.Mark.Key,report.Mark.SeenAt);
            current[report.Mark.LiveKey] = report;
            grace.Update(report,receivedAt);
        }
        foreach (var key in removed) current.Remove(key.ToLiveKey());
    }
}
