using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed class ARankSighting
{
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public DateTime At { get; set; }
    /// <summary>Receive-side local clock equivalent; At retains the original evidence timestamp.</summary>
    public DateTime? LocalAt { get; set; }
    public bool Alive { get; set; }
    public string Source { get; set; } = string.Empty;
}

public static class ARankSightings
{
    public static string SourceOf(ARankSighting sighting) => sighting.Source == "bear" ? "bear" : sighting.Source == "snipe" ? "snipe" : "observed";

    public static DateTime LocalTime(ARankSighting sighting) => sighting.LocalAt ?? sighting.At;

    public static bool IsSpawned(ARankSighting? sighting, ARankKill? kill, DateTime? restart, DateTime now,
        DateTime? killedAtLocal = null) =>
        sighting is { Alive: true } && LocalTime(sighting) <= now && now - LocalTime(sighting) <= TimeSpan.FromDays(14)
        && (kill is null || LocalTime(sighting) > (killedAtLocal ?? ARankHistory.EvidenceTime(kill))) && (restart is null || LocalTime(sighting) > restart);

    public static IEnumerable<ARankSighting> FromMarks(IEnumerable<SyncMark> marks) => marks
        .Where(m => !m.Dead && !m.IsCustom)
        .Select(m => new ARankSighting { NameId = m.NameId, WorldId = m.WorldId, Instance = m.Instance,
            At = m.LastSeen, Alive = true, Source = "observed" });

    public static IEnumerable<ARankSighting> FromSightings(IEnumerable<SyncSighting> sightings) => sightings
        .Where(s => float.IsFinite(s.HpPercent) && s.HpPercent is >= 0 and <= 100)
        .Select(s => new ARankSighting { NameId = s.NameId, WorldId = s.WorldId, Instance = s.Instance,
            At = s.SeenAt, Alive = s.HpPercent > 0, Source = "observed" });

    public static bool Merge(List<ARankSighting> history, IEnumerable<ARankSighting> incoming, DateTime now)
    {
        var changed = false;
        foreach (var sighting in incoming)
        {
            var localAt = LocalTime(sighting);
            if (ExpansionData.Lookup(sighting.NameId) is null || sighting.WorldId == 0 || sighting.Instance > 9
                || localAt <= DateTime.UnixEpoch || localAt > now || now - localAt > TimeSpan.FromDays(14)) continue;
            var old = history.FirstOrDefault(s => s.NameId == sighting.NameId
                && s.WorldId == sighting.WorldId && s.Instance == sighting.Instance);
            // A stale live echo cannot undo a newer corpse observation; death wins equal timestamps.
            if (old is not null && (LocalTime(old) > localAt || LocalTime(old) == localAt && (!old.Alive || sighting.Alive))) continue;
            if (old is not null) history.Remove(old);
            history.Add(new() { NameId = sighting.NameId, WorldId = sighting.WorldId, Instance = sighting.Instance,
                At = sighting.At, LocalAt = sighting.LocalAt, Alive = sighting.Alive, Source = SourceOf(sighting) });
            changed = true;
        }
        return history.RemoveAll(s => now - LocalTime(s) > TimeSpan.FromDays(14)) > 0 || changed;
    }
}
