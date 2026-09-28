using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed partial class SyncCoordinator
{
    private bool _arankSightingsDirty;
    private DateTime _nextARankSightingSave;
    private DateTime _nextARankLocationCapture;

    internal void RememberARankSightings(IEnumerable<ARankSighting> sightings) =>
        _arankSightingsDirty |= ARankSightings.Merge(_config.ARankSightings, sightings, DateTime.UtcNow);

    private void RememberARankLocations(IEnumerable<ARankLocation> locations) =>
        _arankSightingsDirty |= ARankLocations.Merge(_config.ARankLocations, locations, DateTime.UtcNow);

    private void RememberSharedARankLocations(IEnumerable<ARankLocation> locations)
    {
        var receivedAt = DateTime.UtcNow;
        RememberARankLocations(ARankLocations.ToLocalTime(locations, _sightingClock.ToLocal, receivedAt));
    }

    private void CaptureARankLocations()
    {
        var now = DateTime.UtcNow;
        if (now < _nextARankLocationCapture) return;
        _nextARankLocationCapture = now.AddSeconds(1);
        RememberARankLocations(ARankLocations.FromMarks(_detector.Marks.Values));
    }

    private void RememberLocalARankLocations() => RememberARankLocations(_detector.VisibleMarks
        .Where(sighting => !sighting.IsRemote && sighting.Rank == HuntRank.A)
        .Select(sighting => new ARankLocation
        {
            NameId = sighting.NameId, WorldId = sighting.WorldId, Instance = sighting.Instance,
            TerritoryId = sighting.TerritoryId, MapId = sighting.MapId,
            X = sighting.MapPosition.X, Y = sighting.MapPosition.Y, SeenAt = sighting.LastSeenUtc,
        }));

    private void SaveARankSightings(bool force = false)
    {
        if (!_arankSightingsDirty || !force && DateTime.UtcNow < _nextARankSightingSave) return;
        _config.Save();
        _arankSightingsDirty = false;
        _nextARankSightingSave = DateTime.UtcNow.AddSeconds(10);
    }
}
