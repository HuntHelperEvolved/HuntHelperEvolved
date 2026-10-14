using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelperEvolved;

// Loads images independently of native nodes. Failed loads can retry, and a
// refresh never replaces working markers with nodes waiting for a texture.
internal sealed class MapTextureCache<T> : IDisposable where T : class, IDisposable
{
    private sealed class Entry
    {
        public Task<T>? Loading;
        public T? Texture;
        public DateTime RetryAfter;
        public bool FailureLogged;
    }

    private readonly Func<string, CancellationToken, Task<T>> _load;
    private readonly Action<string, Exception>? _failed;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    public MapTextureCache(Func<string, CancellationToken, Task<T>> load, Action<string, Exception>? failed = null)
    {
        _load = load;
        _failed = failed;
    }

    // Called only on the overlay's update thread. Async completions never
    // mutate nodes or this dictionary.
    public bool Prepare(IEnumerable<string> paths, DateTime now)
    {
        if (_disposed) return false;
        var wanted = paths.ToHashSet(StringComparer.Ordinal);
        foreach (var path in _entries.Keys.Where(path => !wanted.Contains(path)).ToArray())
        {
            Release(_entries[path]);
            _entries.Remove(path);
        }

        var ready = true;
        foreach (var path in wanted)
        {
            if (!_entries.TryGetValue(path, out var entry))
                _entries.Add(path, entry = new Entry());
            if (entry.Texture is not null) continue;
            if (entry.Loading is null && now >= entry.RetryAfter)
            {
                try { entry.Loading = _load(path, _cancellation.Token); }
                catch (Exception ex) { entry.Loading = Task.FromException<T>(ex); }
            }
            if (entry.Loading is not { IsCompleted: true } loading)
            {
                ready = false;
                continue;
            }

            entry.Loading = null;
            try { entry.Texture = loading.GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                ready = false;
                entry.RetryAfter = now.AddSeconds(1);
                if (!entry.FailureLogged)
                {
                    entry.FailureLogged = true;
                    _failed?.Invoke(path, ex);
                }
            }
        }
        return ready;
    }

    public T Get(string path) => _entries.TryGetValue(path, out var entry) && entry.Texture is { } texture
        ? texture : throw new InvalidOperationException("Map texture is not ready.");

    private static void Release(Entry entry)
    {
        entry.Texture?.Dispose();
        if (entry.Loading is not { } loading) return;
        // A loader may complete even after cancellation. Dispose that result
        // without scheduling any callback against an unloaded native addon.
        _ = loading.ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully) task.Result.Dispose();
            else _ = task.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        foreach (var entry in _entries.Values) Release(entry);
        _entries.Clear();
        _cancellation.Dispose();
    }
}
