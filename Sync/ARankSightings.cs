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
    public bool Alive { get; set; }
}

public static class ARankSightings
{
    public static bool IsSpawned(ARankSighting? sighting, ARankKill? kill, DateTime? restart, DateTime now) =>
        sighting is { Alive: true } && sighting.At <= now && now - sighting.At <= TimeSpan.FromDays(14)
        && (kill is null || sighting.At > kill.At) && (restart is null || sighting.At > restart);

    public static IEnumerable<ARankSighting> FromMarks(IEnumerable<SyncMark> marks) => marks
        .Where(m => !m.Dead && !m.IsCustom)
        .Select(m => new ARankSighting { NameId = m.NameId, WorldId = m.WorldId, Instance = m.Instance,
            At = m.LastSeen, Alive = true });

    public static IEnumerable<ARankSighting> FromSightings(IEnumerable<SyncSighting> sightings) => sightings
        .Where(s => float.IsFinite(s.HpPercent) && s.HpPercent is >= 0 and <= 100)
        .Select(s => new ARankSighting { NameId = s.NameId, WorldId = s.WorldId, Instance = s.Instance,
            At = s.SeenAt, Alive = s.HpPercent > 0 });

    public static bool Merge(List<ARankSighting> history, IEnumerable<ARankSighting> incoming, DateTime now)
    {
        var changed = false;
        foreach (var sighting in incoming)
        {
            if (ExpansionData.Lookup(sighting.NameId) is null || sighting.WorldId == 0 || sighting.Instance > 9
                || sighting.At > now || now - sighting.At > TimeSpan.FromDays(14)) continue;
            var old = history.FirstOrDefault(s => s.NameId == sighting.NameId
                && s.WorldId == sighting.WorldId && s.Instance == sighting.Instance);
            // A stale live echo cannot undo a newer corpse observation; death wins equal timestamps.
            if (old is not null && (old.At > sighting.At || old.At == sighting.At && (!old.Alive || sighting.Alive))) continue;
            if (old is not null) history.Remove(old);
            history.Add(new() { NameId = sighting.NameId, WorldId = sighting.WorldId, Instance = sighting.Instance,
                At = sighting.At, Alive = sighting.Alive });
            changed = true;
        }
        return history.RemoveAll(s => now - s.At > TimeSpan.FromDays(14)) > 0 || changed;
    }
}
