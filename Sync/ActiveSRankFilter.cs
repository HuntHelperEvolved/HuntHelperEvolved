using System;

namespace HuntHelperEvolved.Sync;

public static class ActiveSRankFilter
{
    public static string? Status(SyncSRankStatus status, bool seenUp, DateTime now)
    {
        if (seenUp) return "Visible to a scout";
        if (status.SpawnedAt is not { } spawn || (status.KilledAt is { } death && spawn <= death)) return null;
        if (status.FaloopActiveAt is { } report && report <= now.AddSeconds(10)
            && status.FaloopActiveUntil is { } expires && expires > now) return "Faloop reports active";
        return null;
    }
}
