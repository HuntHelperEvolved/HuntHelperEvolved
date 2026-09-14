using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HuntHelperEvolved.Sync;

internal sealed class BoardSnapshot<T> where T : class
{
    private T? _value;
    private uint[] _worlds = Array.Empty<uint>();
    private string[] _expansions = Array.Empty<string>();
    private string _search = string.Empty;
    private bool _available, _connected;
    private long _builtAt;

    internal void Invalidate() => _value = null;

    internal T Get(IReadOnlyList<uint> worlds, IReadOnlyList<string> expansions, string search,
        bool available, bool connected, long timestamp, Func<T> build)
    {
        if (_value is not null && timestamp >= _builtAt && timestamp - _builtAt < Stopwatch.Frequency / 10
            && _available == available && _connected == connected && _search == search
            && _worlds.SequenceEqual(worlds) && _expansions.SequenceEqual(expansions)) return _value;

        var value = build();
        _worlds = worlds.ToArray();
        _expansions = expansions.ToArray();
        _search = search;
        _available = available;
        _connected = connected;
        _builtAt = timestamp;
        return _value = value;
    }
}
