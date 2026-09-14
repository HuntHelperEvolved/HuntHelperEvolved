using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace HuntHelperEvolved;

internal static class TrainCompletionSnapshot
{
    // Ignore routine sightings, but retain sniped bounds and alive evidence after an observed death.
    public static string Create(long generation, IEnumerable<DetectedMark> route,
        IEnumerable<TrackedMark> history, IEnumerable<FlagEntry> watches, bool shared) =>
        JsonConvert.SerializeObject(new
        {
            Generation = generation,
            Shared = shared,
            Marks = route.Select(m => new
            {
                m.NameId, m.WorldId, m.Instance, m.Name, m.WorldName, m.TerritoryId, m.MapId,
                m.IsCustom, m.ZoneName, m.Spiced, m.Dead, m.FirstSeenUtc,
                m.DeathObservedAtUtc, m.SnipedAtUtc,
                LastAlive = RelevantLastAlive(m.Dead, m.DeathObservedAtUtc, m.SnipedAtUtc, m.LastSeenUtc),
                Position = m.IsCustom ? (System.Numerics.Vector2?)m.MapPosition : null
            }),
            History = history.OrderBy(m => m.WorldId).ThenBy(m => m.ModelId).ThenBy(m => m.Instance)
                .Select(m => new
                {
                    m.ModelId, m.WorldId, m.Instance, m.Name, m.WorldName, m.TerritoryId,
                    m.Dead, m.DeathObservedAtUtc, m.SnipedAtUtc,
                    LastAlive = RelevantLastAlive(m.Dead, m.DeathObservedAtUtc, m.SnipedAtUtc, m.LastSeenUtc)
                }),
            Flags = watches
        });

    private static DateTime? RelevantLastAlive(bool dead, DateTime? death, DateTime? sniped, DateTime lastAlive) =>
        sniped.HasValue || dead && death.HasValue && lastAlive > death.Value ? lastAlive : null;
}
