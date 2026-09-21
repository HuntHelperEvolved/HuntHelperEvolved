using System;

namespace HuntHelperEvolved.Sync;

internal static class TrainMarkSignature
{
    /// <summary>
    /// Coalesces routine sightings while preserving the exact last-seen times
    /// used by reports and the server's completion checks.
    /// </summary>
    public static int Create(DetectedMark m)
    {
        var h = new HashCode();
        h.Add(m.Dead);
        h.Add(m.DeathObservedAtUtc?.Ticks / TimeSpan.TicksPerSecond ?? 0);
        h.Add(m.Spiced);
        h.Add(m.SnipedAtUtc?.Ticks ?? 0);
        h.Add(m.Name);
        h.Add(m.ZoneName);
        h.Add(m.IsCustom);
        h.Add(m.TerritoryId);
        h.Add(m.MapId);
        h.Add(MathF.Round(m.MapPosition.X, 1));
        h.Add(MathF.Round(m.MapPosition.Y, 1));
        // Sniped windows and alive evidence after a death must reach the server
        // even when the newer sighting falls in the same ten-second interval.
        var exactLastSeen = m.SnipedAtUtc.HasValue
            || m.Dead && m.DeathObservedAtUtc is { } death && m.LastSeenUtc > death;
        h.Add(exactLastSeen ? m.LastSeenUtc.Ticks : m.LastSeenUtc.Ticks / (10 * TimeSpan.TicksPerSecond));
        return h.ToHashCode();
    }
}
