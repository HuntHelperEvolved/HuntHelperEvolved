using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

/// <summary>Briefly coalesces one spawn's reports before the caller resolves its current health.</summary>
public sealed class SpawnAlertQueue
{
    public const int MaxCount = 100;
    public static readonly TimeSpan CoalesceDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxQueueAge = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _ordered = new();
    private readonly Dictionary<(uint Name, uint World, uint Instance), LinkedListNode<Entry>> _byKey = new();

    private sealed class Entry(SRankSpawnBroadcast spawn, DateTime receivedAt)
    {
        public readonly SRankSpawnBroadcast Spawn = spawn;
        public readonly DateTime ReceivedAt = receivedAt;
        public int PositionPriority = Priority(spawn.Source);
        public DateTime PositionAt = spawn.SpawnedAt;
    }

    public int Count { get { lock (_gate) return _ordered.Count; } }

    public void Enqueue(SRankSpawnBroadcast spawn, DateTime localNow)
    {
        if (spawn is null || spawn.Event is not ("spawn" or "release") || spawn.NameId == 0
            || spawn.WorldId == 0 || spawn.Instance > 9 || spawn.SpawnedAt <= DateTime.UnixEpoch) return;
        var incoming = Copy(spawn);
        if (!HasPosition(incoming)) { incoming.X = null; incoming.Y = null; }
        var key = (incoming.NameId, incoming.WorldId, incoming.Instance);
        lock (_gate)
        {
            RemoveExpired(localNow);
            if (_byKey.TryGetValue(key, out var existing))
            {
                var entry = existing.Value;
                // Preserve the first event time and local deadline. Unrelated old reports
                // must not turn a fresh queued alert into stale history.
                if ((incoming.SpawnedAt - entry.Spawn.SpawnedAt).Duration() > MergeWindow) return;
                if (incoming.Event == "release") entry.Spawn.Event = "release";
                var priority = Priority(incoming.Source);
                if (priority > Priority(entry.Spawn.Source)
                    || string.IsNullOrWhiteSpace(entry.Spawn.Source) && !string.IsNullOrWhiteSpace(incoming.Source))
                {
                    entry.Spawn.Source = incoming.Source;
                    if (!string.IsNullOrWhiteSpace(incoming.DataCenter)) entry.Spawn.DataCenter = incoming.DataCenter;
                }
                else if (string.IsNullOrWhiteSpace(entry.Spawn.DataCenter) && !string.IsNullOrWhiteSpace(incoming.DataCenter))
                    entry.Spawn.DataCenter = incoming.DataCenter;
                // Track coordinate provenance separately: a higher source with no position
                // cannot make a lower source's coordinates look equally authoritative.
                if (HasPosition(incoming) && (!HasPosition(entry.Spawn) || priority > entry.PositionPriority
                    || priority == entry.PositionPriority && incoming.SpawnedAt >= entry.PositionAt))
                {
                    entry.Spawn.X = incoming.X; entry.Spawn.Y = incoming.Y;
                    entry.PositionPriority = priority; entry.PositionAt = incoming.SpawnedAt;
                }
                return;
            }

            if (_ordered.Count >= MaxCount) RemoveFirst();
            var added = new Entry(incoming, localNow);
            var previous = _ordered.Last;
            while (previous is not null && previous.Value.ReceivedAt > localNow) previous = previous.Previous;
            var node = previous is null ? _ordered.AddFirst(added) : _ordered.AddAfter(previous, added);
            _byKey.Add(key, node);
        }
    }

    public bool TryDequeue(DateTime localNow, out SRankSpawnBroadcast spawn)
    {
        lock (_gate)
        {
            RemoveExpired(localNow);
            if (_ordered.First is not { } first || localNow - first.Value.ReceivedAt < CoalesceDelay)
            { spawn = null!; return false; }
            spawn = first.Value.Spawn;
            RemoveFirst();
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) { _ordered.Clear(); _byKey.Clear(); }
    }

    private void RemoveFirst()
    {
        var spawn = _ordered.First!.Value.Spawn;
        _byKey.Remove((spawn.NameId, spawn.WorldId, spawn.Instance));
        _ordered.RemoveFirst();
    }

    private void RemoveExpired(DateTime localNow)
    {
        while (_ordered.First is { } first && localNow - first.Value.ReceivedAt > MaxQueueAge) RemoveFirst();
    }

    private static int Priority(string? source) => source?.Trim().ToLowerInvariant() switch
    { "group" or "group scout" => 3, "bear" => 2, "faloop" => 1, _ => 0 };

    private static bool HasPosition(SRankSpawnBroadcast spawn) => spawn.X is { } x && spawn.Y is { } y
        && float.IsFinite(x) && float.IsFinite(y) && x is >= 1 and <= 100 && y is >= 1 and <= 100;

    private static SRankSpawnBroadcast Copy(SRankSpawnBroadcast source) => new()
    {
        Event = source.Event, NameId = source.NameId, WorldId = source.WorldId, Instance = source.Instance,
        SpawnedAt = source.SpawnedAt, X = source.X, Y = source.Y,
        DataCenter = source.DataCenter ?? "", Source = source.Source ?? "",
    };
}
