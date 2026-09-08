using System.Collections.Generic;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>Notification memory independent of short-lived visible-mark observations.</summary>
internal sealed class MarkAnnouncementDebounce
{
    private readonly Dictionary<(uint Name, uint Instance, uint World), (int? Spot, Vector2 Position)> _announced = new();
    public void Clear() => _announced.Clear();
    public void Killed(uint name, uint instance, uint world) => _announced.Remove((name, instance, world));

    public bool ShouldAnnounce(uint name, uint instance, uint world, int? spot, Vector2 position, bool inCombat)
    {
        var key = (name, instance, world);
        if (_announced.TryGetValue(key, out var previous))
        {
            // Pulled marks can move far from their spawn. That does not make a new spawn.
            if (inCombat) return false;
            var same = previous.Spot is { } a && spot is { } b
                ? a == b : Vector2.DistanceSquared(previous.Position, position) <= 1f;
            if (same)
            {
                // Learn a known spot after the first observation without another alert.
                if (previous.Spot is null && spot is not null) _announced[key] = (spot, position);
                return false;
            }
        }
        _announced[key] = (spot, position);
        return true;
    }
}
