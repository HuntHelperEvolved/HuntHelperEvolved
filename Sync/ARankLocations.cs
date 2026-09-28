using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

/// <summary>The latest observed position, retained independently of train rows and spawn evidence.</summary>
public sealed class ARankLocation
{
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public uint TerritoryId { get; set; }
    public uint MapId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public DateTime SeenAt { get; set; }
}

public static class ARankLocations
{
    public static bool IsValid(ARankLocation? location, DateTime now) =>
        location is not null && ExpansionData.Lookup(location.NameId) is { } mark
        && ARankZoneInstances.ZoneTerritories.TryGetValue(mark.Location, out var territories)
        && territories.Contains(location.TerritoryId)
        && location.WorldId != 0 && location.MapId != 0 && location.Instance <= 9
        && float.IsFinite(location.X) && float.IsFinite(location.Y)
        && location.X is >= 1 and <= 100 && location.Y is >= 1 and <= 100
        && location.SeenAt > DateTime.UnixEpoch && location.SeenAt <= now;

    public static IEnumerable<ARankLocation> FromMarks(IEnumerable<DetectedMark> marks) => marks
        .Where(mark => !mark.IsCustom)
        .Select(mark => new ARankLocation
        {
            NameId = mark.NameId, WorldId = mark.WorldId, Instance = mark.Instance,
            TerritoryId = mark.TerritoryId, MapId = mark.MapId,
            X = mark.MapPosition.X, Y = mark.MapPosition.Y, SeenAt = mark.LocationSeenAtUtc ?? mark.LastSeenUtc,
        });

    public static IEnumerable<ARankLocation> FromMarks(IEnumerable<SyncMark> marks) => marks
        .Where(mark => !mark.IsCustom)
        .Select(mark => new ARankLocation
        {
            NameId = mark.NameId, WorldId = mark.WorldId, Instance = mark.Instance,
            TerritoryId = mark.TerritoryId, MapId = mark.MapId,
            X = mark.X, Y = mark.Y, SeenAt = mark.LastSeen,
        });

    public static IEnumerable<ARankLocation> FromSightings(IEnumerable<SyncSighting> sightings) => sightings
        .Where(sighting => sighting.Rank == "A")
        .Select(sighting => new ARankLocation
        {
            NameId = sighting.NameId, WorldId = sighting.WorldId, Instance = sighting.Instance,
            TerritoryId = sighting.TerritoryId, MapId = sighting.MapId,
            X = sighting.X, Y = sighting.Y, SeenAt = sighting.SeenAt,
        });

    /// <summary>Compare shared observations with local ones on the local UTC clock.</summary>
    public static IEnumerable<ARankLocation> ToLocalTime(IEnumerable<ARankLocation> locations, Func<DateTime, DateTime> toLocal, DateTime receivedAt) => locations
        .Select(location => new ARankLocation
        {
            NameId = location.NameId, WorldId = location.WorldId, Instance = location.Instance,
            TerritoryId = location.TerritoryId, MapId = location.MapId,
            X = location.X, Y = location.Y,
            SeenAt = LocalSeenAt(location.SeenAt, toLocal, receivedAt),
        });

    public static DateTime LocalSeenAt(DateTime seenAt, Func<DateTime, DateTime> toLocal, DateTime receivedAt)
    {
        if (seenAt <= DateTime.UnixEpoch) return seenAt;
        var local = toLocal(seenAt);
        // Clock samples include network delay. A faster later packet may appear
        // slightly ahead of receipt; preserve invalid far-future values for validation.
        return local > receivedAt && local - receivedAt <= TimeSpan.FromMinutes(1) ? receivedAt : local;
    }

    public static IEnumerable<ARankLocation> FromPersisted(IEnumerable<PersistedMark> marks) => marks
        .Where(mark => !mark.IsCustom)
        .Select(mark => new ARankLocation
        {
            NameId = mark.NameId, WorldId = mark.WorldId, Instance = mark.Instance,
            TerritoryId = mark.TerritoryId, MapId = mark.MapId,
            X = mark.X, Y = mark.Y, SeenAt = mark.LocationSeenAtUtc ?? mark.LastSeenUtc,
        });

    /// <summary>
    /// Keep one valid position per world, mark and instance. An older imported
    /// or restored train cannot replace a more recent observation. Positions
    /// alone never create kill or living-sighting evidence.
    /// </summary>
    public static bool Merge(List<ARankLocation> history, IEnumerable<ARankLocation> incoming, DateTime now)
    {
        var latest = new Dictionary<(uint Name, uint World, uint Instance), ARankLocation>();
        foreach (var location in history)
        {
            if (!IsValid(location, now)) continue;
            var key = (location.NameId, location.WorldId, location.Instance);
            if (!latest.TryGetValue(key, out var old) || location.SeenAt > old.SeenAt)
                latest[key] = location;
        }
        foreach (var location in incoming)
        {
            if (!IsValid(location, now)) continue;
            var key = (location.NameId, location.WorldId, location.Instance);
            if (latest.TryGetValue(key, out var old) && location.SeenAt <= old.SeenAt) continue;
            // Only changed positions need a copy; shared sightings can arrive every second.
            latest[key] = new ARankLocation
            {
                NameId = location.NameId, WorldId = location.WorldId, Instance = location.Instance,
                TerritoryId = location.TerritoryId, MapId = location.MapId,
                X = location.X, Y = location.Y, SeenAt = location.SeenAt,
            };
        }
        var merged = latest.Values.ToList();
        if (history.Count == merged.Count && history.SequenceEqual(merged)) return false;
        history.Clear();
        history.AddRange(merged);
        return true;
    }
}
