using HuntHelperEvolved.Ipc;
using HuntHelperEvolved.Sync;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

internal readonly record struct TrainStatusInstanceEvidence(uint NameId, uint TerritoryId, uint WorldId, uint Instance);

internal sealed class TrainStatusInput
{
    public bool LoggedIn { get; init; }
    public bool SyncConnected { get; init; }
    public uint WorldId { get; init; }
    public string WorldName { get; init; } = string.Empty;
    public DateTime NowUtc { get; init; }
    public IReadOnlyList<DetectedMark> Marks { get; init; } = Array.Empty<DetectedMark>();
    public IReadOnlyList<ARankKill> Kills { get; init; } = Array.Empty<ARankKill>();
    public IReadOnlyList<ARankSighting> Sightings { get; init; } = Array.Empty<ARankSighting>();
    public IReadOnlyList<TrainStatusInstanceEvidence> InstanceEvidence { get; init; } = Array.Empty<TrainStatusInstanceEvidence>();
    public IReadOnlyList<SyncSRankStatus> SRankStatuses { get; init; } = Array.Empty<SyncSRankStatus>();
    public SyncFaloopStatus Faloop { get; init; } = new();
}

internal static class TrainStatusBuilder
{
    private static readonly (string Code, string Name)[] Expansions =
        { ("DT", "Dawntrail"), ("EW", "Endwalker"), ("ShB", "Shadowbringers") };

    internal static TrainStatusSnapshot Build(TrainStatusInput input)
    {
        if (!input.LoggedIn || input.WorldId == 0)
            return new(TrainStatusContract.Version, false, input.SyncConnected, 0, string.Empty,
                false, input.NowUtc, Array.Empty<TrainExpansionStatus>());

        var marks = input.Marks.Where(m => !m.IsCustom && m.WorldId == input.WorldId
                && m.Instance <= 9 && ExpansionData.Lookup(m.NameId) is not null)
            .GroupBy(m => (m.NameId, m.Instance)).Select(g => g.Last()).ToArray();
        // Keep capture independent of whether the A-rank window is drawing. These
        // copies apply the same train evidence that its once-a-second capture saves.
        var kills = input.Kills.Where(k => k.WorldId == input.WorldId).ToList();
        ARankHistory.Merge(kills, marks.Where(m => m.Dead && (m.SnipedAtUtc ?? m.DeathObservedAtUtc) is not null)
            .Select(m => new ARankKill { NameId = m.NameId, WorldId = m.WorldId, Instance = m.Instance,
                At = (m.SnipedAtUtc ?? m.DeathObservedAtUtc)!.Value,
                LastAliveAt = m.SnipedAtUtc is not null ? m.LastSeenUtc : null,
                Uncertain = m.SnipedAtUtc is not null }), input.NowUtc);
        var sightings = input.Sightings.Where(s => s.WorldId == input.WorldId).ToList();
        ARankSightings.Merge(sightings, marks.Where(m => !m.Dead).Select(m => new ARankSighting {
            NameId = m.NameId, WorldId = m.WorldId, Instance = m.Instance,
            At = m.LastSeenUtc, Alive = true }), input.NowUtc);
        var killsByMark = kills.ToLookup(k => k.NameId);
        var sightingsByMark = sightings.ToDictionary(s => (s.NameId, s.Instance));
        var statuses = input.SRankStatuses.Where(s => s.WorldId == input.WorldId).ToArray();
        var restart = statuses.Where(s => s.Maintenance).Select(s => s.KilledAt).Max();
        var offline = input.Faloop.IsOffline(input.WorldName);

        // Resolve the same full roster and per-zone evidence as /hha; a sighting
        // in one instance establishes the other mark's rows in that zone too.
        var zones = new ARankZoneInstances();
        foreach (var mark in marks) zones.Add(mark.NameId, 0, input.WorldId, mark.Instance);
        foreach (var kill in kills) zones.Add(kill.NameId, 0, input.WorldId, kill.Instance);
        foreach (var sighting in sightings) zones.Add(sighting.NameId, 0, input.WorldId, sighting.Instance);
        foreach (var evidence in input.InstanceEvidence.Where(e => e.WorldId == input.WorldId))
            zones.Add(evidence.NameId, evidence.TerritoryId, input.WorldId, evidence.Instance);
        foreach (var status in statuses) zones.Add(0, status.TerritoryId, input.WorldId, status.Instance);

        var results = new List<TrainExpansionStatus>(Expansions.Length);
        foreach (var (code, name) in Expansions)
        {
            var total = 0;
            var known = 0;
            var completed = 0;
            var progressSum = 0d;
            foreach (var (nameId, info) in ExpansionData.ModelIdToMark.Where(p => p.Value.Expansion == name))
            {
                var markKills = killsByMark[nameId].ToArray();
                var instances = ARankInstances.Resolve(zones.Get(input.WorldId, info.Location), markKills.Select(k => k.Instance));
                var territory = ARankZoneInstances.ZoneTerritories.TryGetValue(info.Location, out var territories) ? territories[0] : 0;
                instances = input.Faloop.CurrentInstancesInPlace(territory, instances);
                total += instances.Count;
                if (offline) continue;
                foreach (var instance in instances)
                {
                    var kill = markKills.FirstOrDefault(k => k.Instance == instance);
                    var sighting = sightingsByMark.GetValueOrDefault((nameId, instance));
                    var up = ARankSightings.IsSpawned(sighting, kill, restart, input.NowUtc);
                    var (opens, ends) = ARankHistory.Window(kill, info.MinHours, info.MaxHours, restart);
                    if (ARankSpawnProgress.Fraction(up, opens, ends, input.NowUtc) is not { } progress) continue;
                    progressSum += progress;
                    known++;
                    if (progress == 1d) completed++;
                }
            }
            var recorded = marks.Count(m => !m.Dead && m.SnipedAtUtc is null
                && ExpansionData.Lookup(m.NameId)!.Expansion == name);
            results.Add(new(code, recorded, total, known, known > 0 ? progressSum / known : null, completed));
        }
        return new(TrainStatusContract.Version, true, input.SyncConnected, input.WorldId, input.WorldName,
            offline, input.NowUtc, results.ToArray());
    }
}
