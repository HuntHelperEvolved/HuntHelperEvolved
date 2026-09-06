using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public sealed class SpawnAlertFilter
{
    private readonly Dictionary<(uint, uint, uint), DateTime> _last = new();
    public bool Accept(SRankSpawnBroadcast spawn, bool allowed, DateTime now)
    {
        if (!allowed || spawn.WorldId == 0 || spawn.NameId == 0 || spawn.Instance > 9
            || spawn.SpawnedAt > now.AddSeconds(10) || now - spawn.SpawnedAt > TimeSpan.FromMinutes(2)) return false;
        var key = (spawn.NameId, spawn.WorldId, spawn.Instance);
        if (_last.TryGetValue(key, out var previous) && spawn.SpawnedAt - previous < TimeSpan.FromHours(1)) return false;
        _last[key] = spawn.SpawnedAt;
        return true;
    }
}
