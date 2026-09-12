using System;
using System.Linq;
using HuntHelperEvolved.Sync;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private DateTime _nextWatchRefresh;
    private bool AutomaticWatchAvailable(FlagEntry watch)
    {
        if (!_sync.IsConnected || _sync.Faloop.IsOffline(_worldData.NameOf(watch.WorldId))) return false;
        var timer = SRankTimerData.ForTerritory(watch.TerritoryId);
        return timer is not null && TrainWatchPlanner.Available(timer,
            _sync.StatusFor(timer.NameId,watch.WorldId,watch.Instance),DateTime.UtcNow,
            _sync.IsSeenUp(timer.NameId,watch.WorldId,watch.Instance));
    }
    private void UpdateAutomaticTrainWatches()
    {
        var now=DateTime.UtcNow;
        if (now<_nextWatchRefresh) return;
        _nextWatchRefresh=now.AddSeconds(1);
        // Do not rewrite the shared list from a stale/disconnected timer snapshot.
        if (_completion.IsBusy || !_config.AutoTrainWatches || !_clientState.IsLoggedIn || !_sync.IsConnected
            || _config.SyncShareTrain && !_sync.SupportsScopedTrainWatches) return;
        var eligible=_detector.Marks.Values.Where(m=>!m.IsCustom && TrainWatchPlanner.Territories.Contains(m.TerritoryId))
            .Select(m=>(m.WorldId,m.TerritoryId,m.Instance)).Distinct()
            .Where(scope=>scope.WorldId!=0 && !_sync.Faloop.IsOffline(_worldData.NameOf(scope.WorldId)))
            .Select(scope=>
            {
                var timer=SRankTimerData.ForTerritory(scope.TerritoryId)!;
                var status=_sync.StatusFor(timer.NameId,scope.WorldId,scope.Instance);
                if (!TrainWatchPlanner.Available(timer,status,now,_sync.IsSeenUp(timer.NameId,scope.WorldId,scope.Instance))) return null;
                var zone=_sync.ZoneFor(scope.TerritoryId,scope.WorldId,scope.Instance);
                var points=SpawnPointData.For(scope.TerritoryId);
                var confirmed=zone is null ? null : SpawnMapping.ConfirmedPoint(points,zone,SpawnMapping.ReliableCycle(zone,status));
                return new FlagEntry { Label=$"{timer.Name} — {_worldData.NameOf(scope.WorldId)}{ExpansionData.InstanceGlyph(scope.Instance)}",
                    WorldId=scope.WorldId,TerritoryId=scope.TerritoryId,Instance=scope.Instance,Automatic=true,
                    HasLocation=confirmed.HasValue,X=confirmed is { } index ? points[index].X : 0,
                    Y=confirmed is { } other ? points[other].Y : 0 };
            }).OfType<FlagEntry>().ToList();
        if (TrainWatchPlanner.Reconcile(_config.Flags,eligible)) _config.Save();
    }
}
