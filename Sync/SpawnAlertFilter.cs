using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public sealed class SpawnAlertFilter
{
    private const int Capacity = 8192;
    private static readonly TimeSpan History = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DeathGrace = TimeSpan.FromSeconds(15);
    // A source upgrade, Faloop release, and local detection all describe the
    // same spawn. Their first accepted timestamp is never renewed by repeats.
    private readonly Dictionary<(uint Name, uint World, uint Instance), DateTime> _last = new();

    /// <summary>Records local presence even when its notification channel is disabled.</summary>
    public void RecordLocal(uint name, uint world, uint instance, DateTime at)
        => AcceptLocal(name, world, instance, at);

    /// <summary>All arguments use the same clock as remote alert timestamps.</summary>
    public bool AcceptLocal(uint name, uint world, uint instance, DateTime at, DateTime? confirmedDeath = null)
        => AcceptCycle(name, world, instance, at, at, confirmedDeath);

    public bool Accept(SRankSpawnBroadcast spawn, bool allowed, DateTime now, DateTime? confirmedDeath = null)
    {
        if (!allowed || spawn.Event is not ("spawn" or "release")) return false;
        return AcceptCycle(spawn.NameId, spawn.WorldId, spawn.Instance, spawn.SpawnedAt, now, confirmedDeath);
    }

    private bool AcceptCycle(uint name, uint world, uint instance, DateTime at, DateTime now, DateTime? confirmedDeath)
    {
        if (name == 0 || world is 0 or > 65535 || instance > 9 || at <= DateTime.UnixEpoch
            || at - now > FutureTolerance || now - at > History) return false;
        var death = confirmedDeath is { } died && died > DateTime.UnixEpoch && died - now <= FutureTolerance
            ? confirmedDeath : null;
        // Do not let a late release/queued report revive a just-killed mark.
        if (death is { } killed && (at - killed < DeathGrace || now - killed < DeathGrace)) return false;
        var key = (name, world, instance);
        if (_last.TryGetValue(key, out var previous)
            && !(death > previous) && at - previous < NaturalCycle(name)) return false;
        Prune(now);
        if (!_last.ContainsKey(key) && _last.Count >= Capacity)
        {
            (uint Name, uint World, uint Instance) oldestKey = default;
            var oldest = DateTime.MaxValue;
            foreach (var row in _last)
                if (row.Value < oldest) { oldest = row.Value; oldestKey = row.Key; }
            _last.Remove(oldestKey);
        }
        _last[key] = at;
        return true;
    }

    private static TimeSpan NaturalCycle(uint name) => TimeSpan.FromHours(
        SRankTimerData.ByNameId.TryGetValue(name, out var mark) ? mark.MinHours : 1);

    private void Prune(DateTime now)
    {
        foreach (var key in new List<(uint Name, uint World, uint Instance)>(_last.Keys))
            // Keep enough history that an otherwise valid two-minute-old event
            // cannot be mistaken for the next cycle at the pruning boundary.
            if (now - _last[key] > NaturalCycle(key.Name) + History + FutureTolerance) _last.Remove(key);
    }
}
