using System;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>Nearby estimate, briefly retaining positions when character slots are culled.</summary>
public sealed class NearbyPlayerCache
{
    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _time;
    private readonly Dictionary<uint, (Vector3 Position, bool Alive, long SeenAt)> _positions = new();
    private readonly List<uint> _expired = new();
    private (uint World, uint Territory, uint Instance) _scope;

    public NearbyPlayerCache(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    public void SetScope(uint world, uint territory, uint instance)
    {
        var scope = (world, territory, instance);
        if (scope != _scope) { _positions.Clear(); _scope = scope; }
    }
    public void Clear() { _positions.Clear(); _scope = default; }
    public void Observe(uint entityId, Vector3 position, bool alive = true)
    {
        if (entityId is 0 or 0xE0000000 || !float.IsFinite(position.X)
            || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) return;
        _positions[entityId] = (position, alive, _time.GetTimestamp());
    }
    public int CountNear(Vector3 position, uint excludeEntityId = 0) => CountPlayersNear(position, excludeEntityId).Total;

    public (int Alive, int Total) CountPlayersNear(Vector3 position, uint excludeEntityId = 0)
    {
        var alive = 0;
        var total = 0;
        var now = _time.GetTimestamp();
        _expired.Clear();
        foreach (var (entityId, known) in _positions)
        {
            if (_time.GetElapsedTime(known.SeenAt, now) >= Retention) _expired.Add(entityId);
            else if (entityId != excludeEntityId && Vector3.DistanceSquared(known.Position, position) <= 2500f)
            {
                total++;
                if (known.Alive) alive++;
            }
        }
        foreach (var entityId in _expired) _positions.Remove(entityId);
        return (alive, total);
    }
}
