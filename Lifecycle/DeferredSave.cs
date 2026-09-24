using System;

namespace HuntHelperEvolved;

/// <summary>Coalesces changes without postponing a busy stream of edits indefinitely.</summary>
internal sealed class DeferredSave
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private bool _pending;
    private long _revision;
    private TimeSpan _due;

    public void MarkChanged(TimeSpan now)
    {
        if (!_pending) _due = now + Interval;
        _pending = true;
        _revision++;
    }

    public void Flush(TimeSpan now, Action write, bool force = false)
    {
        if (!_pending || !force && now < _due) return;
        var revision = _revision;
        // A failed write stays pending, but must not throw on every frame.
        _due = now + RetryInterval;
        write();
        if (_revision == revision) _pending = false;
        else _due = now + Interval;
    }
}
