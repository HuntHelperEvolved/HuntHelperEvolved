using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed partial class SyncCoordinator
{
    private bool _arankSightingsDirty;
    private DateTime _nextARankSightingSave;
    private DateTime _nextARankLocationCapture;
    private readonly ARankReportSharing _arankReports = new();
    private string _arankHistoryServerId = string.Empty;
    public bool SupportsARankReports { get; private set; }

    internal string ARankSnipeStatus(uint nameId, uint worldId, uint instance) =>
        _arankReports.Status(nameId, worldId, instance);

    internal void RememberARankSightings(IEnumerable<ARankSighting> sightings) =>
        _arankSightingsDirty |= ARankSightings.Merge(_config.ARankSightings, sightings, DateTime.UtcNow);

    internal void ReportARankSnipe(uint nameId, uint worldId, uint instance, DateTime? killedAt)
    {
        _detector.Marks.TryGetValue((nameId, instance, worldId), out var mark);
        var restart = _sranks.Values.Where(s => s.WorldId == worldId && s.Maintenance)
            .Select(s => s.KilledAt).DefaultIfEmpty().Max();
        var report = ARankManualReports.Record(_config.ARankKills, _config.ARankSightings, mark,
            nameId, worldId, instance, killedAt, DateTime.UtcNow, restart);
        _detector.RemoveSighting(nameId, instance, worldId);
        _config.Save();
        var message = _arankReports.Begin(report, _config.SyncEnabled && IsConnected, SupportsARankReports,
            _config.SyncShareTrain, DateTime.UtcNow);
        if (message is not null) _client.Send(message);
    }

    private void ApplyARankUpdates(ARankUpdatesBroadcast update)
    {
        _arankReports.Apply(update);
        RememberSharedARankKills(update.Kills);
        Bump();
    }

    private void RememberSharedARankKills(IEnumerable<ARankKill> incoming)
    {
        var kills = incoming.ToList();
        foreach (var kill in kills) kill.ServerId = _arankHistoryServerId;
        var now = DateTime.UtcNow;
        var changed = ARankHistory.Merge(_config.ARankKills, kills, now);
        var keys = kills.Select(k => (k.NameId, k.WorldId, k.Instance)).ToHashSet();
        // A backdated correction also ends earlier live confirmations. A real
        // sighting after the report can still establish the following spawn.
        // Use accepted history and its clock-drift allowance so an acknowledged
        // report a few seconds ahead is not lost from sighting history forever.
        _arankSightingsDirty |= ARankSightings.Merge(_config.ARankSightings,
            _config.ARankKills.Where(k => k.ReportedAt is not null && keys.Contains((k.NameId, k.WorldId, k.Instance)))
                .Select(k => new ARankSighting
                {
                    NameId = k.NameId, WorldId = k.WorldId, Instance = k.Instance,
                    At = k.ReportedAt!.Value, Alive = false,
                }), now.AddSeconds(10));
        if (changed) _config.Save();
    }

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
