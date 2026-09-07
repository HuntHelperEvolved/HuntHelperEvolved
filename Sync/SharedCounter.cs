using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
namespace HuntHelperEvolved.Sync;

public sealed class SharedCounter
{
    public uint WorldId { get; set; }
    public uint TerritoryId { get; set; }
    public uint Instance { get; set; }
    public string Mob { get; set; } = string.Empty;
    public string Epoch { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public Dictionary<string, int> Contributions { get; set; } = new();
    public long Total => Contributions.Values.Sum(x => (long)x);
    [JsonIgnore] public string Key => $"{WorldId}:{TerritoryId}:{Instance}:{Mob}";
}
public sealed class CounterUpdateMessage
{
    public string Type => "counter.update";
    public SharedCounter Counter { get; set; } = new();
    public string Contributor { get; set; } = string.Empty;
    public int Count { get; set; }
}
public sealed class CounterResetMessage
{
    public string Type => "counter.reset";
    public uint WorldId { get; set; }
    public uint TerritoryId { get; set; }
    public uint Instance { get; set; }
    public Dictionary<string, string> Epochs { get; set; } = new();
}
public sealed class CounterBroadcast
{
    public List<SharedCounter> Counters { get; set; } = new();
}
public sealed class CounterContribution
{
    public SharedCounter Counter { get; set; } = new();
    public int Count { get; set; }
    public bool Reconcile(SharedCounter row, string contributor)
    {
        var acknowledged = row.Contributions.GetValueOrDefault(contributor);
        var count = row.Epoch == Counter.Epoch ? Math.Max(Count, acknowledged) : acknowledged;
        var changed = count != Count || Counter.Epoch != row.Epoch;
        Counter.Epoch = row.Epoch; Count = count;
        return changed;
    }
}
