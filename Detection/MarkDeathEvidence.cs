using System.Collections.Generic;

namespace HuntHelperEvolved;

/// <summary>Only an alive-to-dead observation of the same loaded object proves a death time.</summary>
internal sealed class MarkDeathEvidence
{
    private readonly HashSet<ulong> _alive = new();
    public bool Observe(ulong objectId, uint currentHp, uint maxHp)
    {
        if (maxHp == 0) return false;
        if (currentHp > 0) { _alive.Add(objectId); return false; }
        return _alive.Remove(objectId);
    }
    public void Retain(IReadOnlySet<ulong> visible) => _alive.RemoveWhere(id => !visible.Contains(id));
    public void Clear() => _alive.Clear();
}
