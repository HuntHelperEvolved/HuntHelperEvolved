using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

internal static class ARankManualReports
{
    /// <summary>Records a manual kill or found-missing report for exactly one world and instance.</summary>
    internal static ARankKill Record(List<ARankKill> kills, List<ARankSighting> sightings, DetectedMark? mark,
        uint nameId, uint world, uint instance, DateTime? killedAt, DateTime now, DateTime? restart = null)
    {
        var info = ExpansionData.Lookup(nameId);
        if (info is null || world == 0 || instance > 9)
            throw new ArgumentException("Select a valid A-rank, world and instance.");
        if (now <= DateTime.UnixEpoch || killedAt is { } at
            && (at <= DateTime.UnixEpoch || at > now || now - at > TimeSpan.FromDays(14)))
            throw new ArgumentOutOfRangeException(nameof(killedAt), "Kill time must be within the past 14 days.");

        var previous = kills.FirstOrDefault(k => k.NameId == nameId && k.WorldId == world && k.Instance == instance);
        var sighting = sightings.FirstOrDefault(s => s.NameId == nameId && s.WorldId == world && s.Instance == instance);
        var matchingMark = mark is { IsCustom: false } && mark.NameId == nameId
            && mark.WorldId == world && mark.Instance == instance ? mark : null;
        DateTime? lastAlive = null;
        void ConsiderAlive(DateTime? candidate)
        {
            if (candidate is not { } alive || alive <= DateTime.UnixEpoch || alive > (killedAt ?? now)
                || now - alive > TimeSpan.FromDays(14)
                || restart is { } ended && alive <= ended
                || previous is { Uncertain: false } && alive <= previous.At) return;
            if (lastAlive is null || alive > lastAlive) lastAlive = alive;
        }

        if (sighting is { Alive: true }) ConsiderAlive(sighting.At);
        if (matchingMark is { Dead: false }) ConsiderAlive(matchingMark.LastSeenUtc);
        if (matchingMark is { SnipedAtUtc: { } sniped } && matchingMark.LastSeenUtc <= sniped
            && (previous?.ReportedAt is not { } reportedAt || matchingMark.LastSeenUtc > reportedAt
                || matchingMark.LastSeenUtc == previous.LastAliveAt))
            ConsiderAlive(matchingMark.LastSeenUtc);
        if (previous is { Uncertain: true }) ConsiderAlive(previous.LastAliveAt);
        DateTime? earliestKilledAt = null;
        if (killedAt is null && lastAlive is null && previous is not null)
        {
            // Repeated missing reports describe the same unknown death until a
            // new live sighting establishes another spawn; do not advance it.
            var candidate = previous.Uncertain ? previous.EarliestKilledAt
                : ARankHistory.Window(previous, info.MinHours, info.MaxHours, restart).Opens;
            if (candidate is { } earliest && earliest > DateTime.UnixEpoch && earliest <= now
                && (restart is null || earliest > restart)) earliestKilledAt = earliest;
        }
        var report = new ARankKill
        {
            NameId = nameId, WorldId = world, Instance = instance,
            At = killedAt ?? now, LastAliveAt = lastAlive,
            EarliestKilledAt = earliestKilledAt,
            Uncertain = killedAt is null, ReportedAt = now,
        };
        // A known time may precede the previous found-missing report; it is an
        // explicit correction rather than another historical observation.
        kills.RemoveAll(k => k.NameId == nameId && k.WorldId == world && k.Instance == instance);
        kills.Add(report);
        // Receipt time prevents an old live echo from reviving a backdated kill.
        // A genuinely newer live observation can still establish a new spawn.
        ARankSightings.Merge(sightings, new[] { new ARankSighting
        {
            NameId = nameId, WorldId = world, Instance = instance, At = now, Alive = false,
        } }, now);
        if (matchingMark is not null)
        {
            matchingMark.Dead = true;
            // The timer can be exact without claiming this train witnessed or
            // participated in the kill. Existing train sync keeps it sniped.
            matchingMark.DeathObservedAtUtc = null;
            matchingMark.SnipedAtUtc = now;
            // CaptureHistory reads this field as the lower bound for a snipe.
            // Keep an absent bound absent instead of reusing a corpse timestamp.
            matchingMark.LastSeenUtc = lastAlive ?? DateTime.UnixEpoch;
        }
        return report;
    }

    /// <summary>
    /// Shared train timestamps only advance, so an accepted snipe echo can still
    /// carry a previous corpse sighting. Restore this client's factual bound for
    /// that exact manual report without changing the location observation time.
    /// </summary>
    internal static bool RestoreSnipeBound(IEnumerable<ARankKill> kills, SyncMark incoming,
        DetectedMark local, DetectedMark canonical)
    {
        if (incoming.IsCustom || !incoming.Dead || incoming.DeathAt is not null
            || incoming.SnipedAt is not { } reportedAt || incoming.LastSeen > reportedAt
            || local.IsCustom || !local.Dead || local.DeathObservedAtUtc is not null
            || local.SnipedAtUtc != reportedAt || local.LastSeenUtc > reportedAt
            || local.NameId != incoming.NameId || local.WorldId != incoming.WorldId || local.Instance != incoming.Instance)
            return false;

        var report = kills.FirstOrDefault(k => k.NameId == incoming.NameId && k.WorldId == incoming.WorldId
            && k.Instance == incoming.Instance && k.ReportedAt == reportedAt && k.At <= reportedAt);
        if (report is null) return false;
        var latest = report.Uncertain ? reportedAt : report.At;
        var bound = report.LastAliveAt is { } alive && alive > DateTime.UnixEpoch && alive <= latest
            ? alive : DateTime.UnixEpoch;
        local.LastSeenUtc = bound;
        // DiffTrain must compare the same accepted evidence on both sides. Keep
        // all other canonical fields so unsent local edits still get published.
        canonical.LastSeenUtc = bound;
        if (local.LocationSeenAtUtc > canonical.LocationSeenAtUtc)
        {
            // A corpse location can be newer than the echoed location without
            // establishing a live lower bound. It stays local: re-sending it
            // with an older/absent live timestamp would be ignored by the server.
            canonical.MapPosition = local.MapPosition;
            canonical.LocationSeenAtUtc = local.LocationSeenAtUtc;
        }
        return true;
    }
}
