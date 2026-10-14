using System;

namespace HuntHelperEvolved.Sync;

/// <summary>Builds a train update without confusing local freshness with shared evidence time.</summary>
internal static class TrainMarkSync
{
    internal static SyncMark FromLocal(DetectedMark m, long baseRevision, Func<DateTime, DateTime> toServer) => new()
    {
        NameId = m.NameId,
        Instance = m.Instance,
        WorldId = m.WorldId,
        Name = m.Name,
        TerritoryId = m.TerritoryId,
        MapId = m.MapId,
        WorldName = m.WorldName,
        X = m.MapPosition.X,
        Y = m.MapPosition.Y,
        Dead = m.Dead,
        FirstSeen = AsUtc(m.FirstSeenUtc),
        LastSeen = AsUtc(m.LastSeenUtc),
        LocationSeenAt = m.LocationSeenAtUtc is { } location ? AsUtc(toServer(location)) : null,
        DeathAt = m.DeathObservedAtUtc is { } d ? AsUtc(d) : null,
        SnipedAt = m.SnipedAtUtc is { } sniped ? AsUtc(sniped) : null,
        DeathSource = m.Dead ? m.DeathSource : string.Empty,
        IsCustom = m.IsCustom,
        ZoneName = m.ZoneName,
        Spiced = m.Spiced,
        BaseRevision = baseRevision,
    };


    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
