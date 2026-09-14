using System.Collections.Generic;
namespace HuntHelperEvolved.Sync;

/// <summary>Count and wire-byte limits, shared by producer and game-thread consumer.</summary>
public sealed class ByteBudgetQueue<T>(int maxItems, int maxBytes)
{
    private readonly object _gate=new();
    private readonly Queue<(T Item,int Bytes)> _items=new();
    private int _bytes;
    public int Bytes { get { lock(_gate) return _bytes; } }
    public bool TryEnqueue(T item,int bytes)
    {
        lock(_gate)
        {
            if(bytes<0 || bytes>maxBytes-_bytes || _items.Count>=maxItems) return false;
            _items.Enqueue((item,bytes)); _bytes+=bytes;return true;
        }
    }
    public bool TryDequeue(out T item)
    {
        lock(_gate)
        {
            if(_items.TryDequeue(out var entry)) { _bytes-=entry.Bytes;item=entry.Item;return true; }
            item=default!;return false;
        }
    }
    public void Clear() { lock(_gate) { _items.Clear();_bytes=0; } }
}
