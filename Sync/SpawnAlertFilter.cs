using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public sealed class SpawnAlertFilter
{
    private readonly Dictionary<(uint, uint, uint, string), DateTime> _last = new();
    private readonly Dictionary<(uint,uint,uint), DateTime> _local = new();
    public void RecordLocal(uint name, uint world, uint instance, DateTime at)
    {
        foreach (var key in new List<(uint,uint,uint)>(_local.Keys))
            if (at - _local[key] > TimeSpan.FromMinutes(2)) _local.Remove(key);
        _local[(name,world,instance)]=at;
    }
    public bool Accept(SRankSpawnBroadcast spawn, bool allowed, DateTime now)
    {
        if (spawn.Event is not ("spawn" or "release") || !allowed || spawn.WorldId == 0 || spawn.NameId == 0 || spawn.Instance > 9
            || spawn.SpawnedAt > now.AddSeconds(10) || now - spawn.SpawnedAt > TimeSpan.FromMinutes(2)) return false;
        if (_local.TryGetValue((spawn.NameId,spawn.WorldId,spawn.Instance),out var found)
            && now >= found && now-found <= TimeSpan.FromMinutes(2)) return false;
        var key = (spawn.NameId, spawn.WorldId, spawn.Instance, spawn.Event);
        if (_last.TryGetValue(key, out var previous) && spawn.SpawnedAt - previous < TimeSpan.FromHours(1)) return false;
        _last[key] = spawn.SpawnedAt;
        return true;
    }
}
