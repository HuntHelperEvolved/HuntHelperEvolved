using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

/// <summary>Health for a relay at the moment it is printed, independent of display grace and alert source.</summary>
public readonly record struct RelayHealth(float HpPercent, bool Stale)
{
    public bool IsDead => HpPercent == 0;
    public static RelayHealth Unknown => new(float.NaN, false);

    public static RelayHealth Resolve(SRankSpawnBroadcast spawn, IEnumerable<SyncSighting> observations,
        BearMark? bear, SyncSRankStatus? status, DateTime serverNow, DateTime? pluginDeath = null)
    {
        if (spawn.NameId == 0 || spawn.WorldId == 0 || spawn.Instance > 9) return Unknown;
        var key = (spawn.NameId, spawn.Instance, spawn.WorldId);
        DateTime? death = null;
        if (status is not null && status.Key == key && !status.Uncertain && !status.Maintenance
            && !string.Equals(status.KillSource, "Faloop", StringComparison.OrdinalIgnoreCase)
            && ValidTime(status.KilledAt, serverNow)) death = status.KilledAt;
        if (ValidTime(pluginDeath, serverNow) && (death is null || pluginDeath > death)) death = pluginDeath;

        SyncSighting? latest = null;
        foreach (var observation in observations)
        {
            if (observation.Key != key || observation.Rank != "S"
                || !float.IsFinite(observation.HpPercent) || observation.HpPercent is < 0 or > 100
                || !ValidTime(observation.SeenAt, serverNow)
                || observation.SeenAt <= serverNow.AddSeconds(-3)
                || observation.HpPercent > 0 && death is { } killed && observation.SeenAt <= killed) continue;
            if (latest is null || observation.SeenAt > latest.SeenAt
                || observation.SeenAt == latest.SeenAt && observation.HpPercent == 0)
                latest = observation;
        }
        if (latest is not null) return new(latest.HpPercent, false);

        if (bear is null || bear.Key != key || bear.Rank != "S") return Unknown;
        // New activity or packet receipt cannot make pre-kill HP a post-kill observation.
        if (bear.Report.HpPercent > 0 && death is { } bearDeath
            && (bear.Report.SeenAt is not { } seen || seen <= bearDeath
                || bear.Report.HealthObservedAt is not { } healthAt || healthAt <= bearDeath)) return Unknown;
        if (bear.HealthFresh(serverNow)) return new(bear.Report.HpPercent!.Value, false);
        if (bear.HealthStale(serverNow)) return new(bear.Report.HpPercent!.Value, true);
        return Unknown;
    }

    private static bool ValidTime(DateTime? time, DateTime now) => time is { } at
        && at > DateTime.UnixEpoch && at <= now.AddSeconds(10);
}
