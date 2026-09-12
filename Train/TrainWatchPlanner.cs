using System;
using System.Collections.Generic;
using System.Linq;
using HuntHelperEvolved.Sync;

namespace HuntHelperEvolved;

public static class TrainWatchPlanner
{
    public static readonly uint[] Territories = { 813, 960, 961, 1189 };
    public static bool Available(SRankTimer timer, SyncSRankStatus? status, DateTime now, bool seenUp) =>
        SRankTimerData.Compute(timer,status,now,seenUp).Phase is SRankPhase.Window or SRankPhase.Forced;

    public static bool BelongsToTrain(IEnumerable<DetectedMark> marks, uint world, uint zone, uint instance) =>
        world != 0 && marks.Any(m => !m.IsCustom && m.WorldId == world && m.TerritoryId == zone && m.Instance == instance);

    public static bool Reconcile(List<FlagEntry> watches, IEnumerable<FlagEntry> eligible)
    {
        var wanted=eligible.ToList();
        bool Same(FlagEntry a, FlagEntry b) => a.TerritoryId==b.TerritoryId && a.WorldId==b.WorldId && a.Instance==b.Instance;
        var changed=watches.RemoveAll(w=>w.Automatic && w.SpawnStatus==SpawnStatus.Unknown && !wanted.Any(e=>Same(w,e)))>0;
        foreach(var candidate in wanted)
        {
            var existing=watches.FirstOrDefault(w=>Same(w,candidate)
                || !w.Automatic && w.WorldId==0 && w.TerritoryId==candidate.TerritoryId);
            if(existing is null) { watches.Add(candidate); changed=true; }
            else if(existing.Automatic && existing.SpawnStatus==SpawnStatus.Unknown
                && (existing.HasLocation!=candidate.HasLocation || existing.X!=candidate.X || existing.Y!=candidate.Y))
            { existing.HasLocation=candidate.HasLocation; existing.X=candidate.X; existing.Y=candidate.Y; changed=true; }
        }
        return changed;
    }
}
