using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;

/// <summary>Display-only retention; never feeds map visibility, scouting or notifications.</summary>
public sealed class ActiveMarkGrace
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);
    private sealed class Entry
    {
        public VisibleMark Row = new();
        public readonly Dictionary<string, DateTime> Ids = new();
        public readonly Dictionary<string, DateTime> Names = new(StringComparer.OrdinalIgnoreCase);
    }
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId, uint TerritoryId, uint EntityId), Entry> _rows = new();
    public void Clear() => _rows.Clear();
    public void Update(VisibleMark row, DateTime receivedAt)
    {
        if (!_rows.TryGetValue(row.Mark.LiveKey, out var entry)) _rows[row.Mark.LiveKey] = entry = new();
        if (entry.Row.Mark.SeenAt > row.Mark.SeenAt) return;
        var until = receivedAt + Grace;
        foreach (var id in row.ObserverIds) entry.Ids[id] = until;
        foreach (var name in row.Observers) entry.Names[name] = until;
        entry.Row = new VisibleMark { Mark=row.Mark, DisplayUntil=until };
    }
    public bool IsAlive((uint NameId, uint Instance, uint WorldId) key, DateTime? killedAt, DateTime now) =>
        _rows.Values.Any(entry => entry.Row.Mark.Key == key && ActiveSRankFilter.LivingObservation(
            entry.Row.Mark.HpPercent, entry.Row.Mark.SeenAt, entry.Row.DisplayUntil ?? DateTime.MinValue, killedAt, now));

    public List<VisibleMark> Snapshot(DateTime now)
    {
        foreach (var key in _rows.Where(p=>p.Value.Row.DisplayUntil<=now).Select(p=>p.Key).ToList()) _rows.Remove(key);
        return _rows.Values.Select(e => new VisibleMark { Mark=e.Row.Mark, DisplayUntil=e.Row.DisplayUntil,
            ObserverIds=e.Ids.Where(p=>p.Value>now).Select(p=>p.Key).ToList(),
            Observers=e.Names.Where(p=>p.Value>now).Select(p=>p.Key).Order(StringComparer.OrdinalIgnoreCase).ToList() }).ToList();
    }
    public static List<string> ObserverLabels(VisibleMark row, string selfId, string selfAlias)
    {
        var own = row.ObserverIds.Contains(selfId);
        return row.Observers.Where(n=>!own || !n.Equals(selfAlias,StringComparison.OrdinalIgnoreCase))
            .Concat(own ? new[]{"You"} : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
