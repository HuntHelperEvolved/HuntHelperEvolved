using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

// Pending writes are not server state. Retry only marks still on the current
// local train, and discard all pending work when an authoritative snapshot arrives.
internal sealed class TrainUpsertRetry
{
    private sealed record Pending(int Signature, DateTime RetryAt, int Attempts);
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), Pending> _pending = new();
    private DateTime _blockedUntil;
    private int _rejections;
    public IEnumerable<(uint NameId, uint Instance, uint WorldId)> Keys => _pending.Keys;
    public bool Recovering { get; private set; }
    public bool Contains((uint, uint, uint) key) => _pending.ContainsKey(key);

    public bool TrySend((uint, uint, uint) key, int signature, DateTime now)
    {
        if (now < _blockedUntil) return false;
        var attempts = 0;
        if (_pending.TryGetValue(key, out var pending))
        {
            if (pending.Signature == signature && now < pending.RetryAt) return false;
            attempts = pending.Attempts;
            if (now >= pending.RetryAt) Recovering = true;
        }
        // Missing confirmations back off too, including a connection that stays
        // open but cannot accept our writes. Ordinary edits can still be sent.
        var seconds = Math.Min(60, 15 * (1 << Math.Min(attempts, 2)));
        _pending[key] = new(signature, now.AddSeconds(seconds), Math.Min(attempts + 1, 3));
        return true;
    }

    public bool HasNewerLocalEdit((uint, uint, uint) key, int signature) =>
        _pending.TryGetValue(key, out var pending) && pending.Signature != signature;

    public void Reject(string? operation, string code, DateTime now)
    {
        if (_pending.Count == 0 || operation is not (null or "train.upsert")
            || code is not ("limit" or "server" or "json")) return;
        Recovering = true;
        _blockedUntil = now.AddSeconds(Math.Min(60, 5 * (1 << Math.Min(_rejections++, 4))));
        foreach (var key in _pending.Keys.ToArray())
            _pending[key] = _pending[key] with { RetryAt = _blockedUntil };
    }

    public void Forget((uint, uint, uint) key)
    {
        _pending.Remove(key);
        if (_pending.Count == 0) Reset();
    }

    public void Reset()
    {
        _pending.Clear();
        _blockedUntil = default;
        _rejections = 0;
        Recovering = false;
    }
}
