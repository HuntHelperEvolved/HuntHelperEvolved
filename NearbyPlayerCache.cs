using System.Collections.Generic;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>Zone-session estimate, retaining positions when character slots are culled.</summary>
public sealed class NearbyPlayerCache
{
    private readonly Dictionary<uint, Vector3> _positions = new();
    private (uint World, uint Territory, uint Instance) _scope;

    public void SetScope(uint world, uint territory, uint instance)
    {
        var scope = (world, territory, instance);
        if (scope != _scope) { _positions.Clear(); _scope = scope; }
    }
    public void Clear() { _positions.Clear(); _scope = default; }
    public void Observe(uint entityId, Vector3 position)
    {
        if (entityId is 0 or 0xE0000000 || !float.IsFinite(position.X)
            || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) return;
        _positions[entityId] = position;
    }
    public int CountNear(Vector3 position)
    {
        var count = 0;
        foreach (var known in _positions.Values)
            if (Vector3.DistanceSquared(known, position) <= 2500f) count++;
        return count;
    }
}
