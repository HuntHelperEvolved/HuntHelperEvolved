using System;

namespace HuntHelperEvolved.Sync;

public static class ActiveSRankFilter
{
    // All observations and death evidence use server time; the display grace
    // still expires against local receipt time. A historical death received now
    // must also supersede living rows retained from before that receipt.
    public static DateTime? DeathEvidenceAt(SyncSRankStatus? status)
    {
        if (status?.KilledAt is not { } killed) return null;
        return status.KillReceivedAt is { } received && received > killed ? received : killed;
    }

    public static bool LivingObservation(float hp, DateTime seenAt, DateTime expiresAt, DateTime? killedAt, DateTime now,
        DateTime? observationNow = null) =>
        float.IsFinite(hp) && hp > 0 && hp <= 100 && expiresAt > now
        && seenAt <= (observationNow ?? now).AddSeconds(10) && (killedAt is null || seenAt > killedAt);

    public static string? Status(SyncSRankStatus status, bool seenUp, DateTime now)
    {
        if (seenUp) return "Visible to a scout";
        if (status.SpawnedAt is not { } spawn || (status.KilledAt is { } death && spawn <= death)) return null;
        if (status.FaloopActiveAt is { } report && report <= now.AddSeconds(10)
            && status.FaloopActiveUntil is { } expires && expires > now) return "Faloop reports active";
        return null;
    }
}
