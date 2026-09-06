using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;

public static class TrainResetRecovery
{
    // Keep the saved order, retain newer edits, and append marks added after reset.
    public static List<T> Merge<T,TKey>(IEnumerable<T> saved, IEnumerable<T> current,
        Func<T,TKey> key, Func<T,DateTime> changedAt) where TKey : notnull
    {
        var result = saved.ToList();
        var indexes = result.Select((row,index)=>(row,index)).ToDictionary(x=>key(x.row),x=>x.index);
        foreach (var row in current)
        {
            if (!indexes.TryGetValue(key(row),out var index)) { indexes[key(row)] = result.Count; result.Add(row); }
            else if (changedAt(row)>changedAt(result[index])) result[index]=row;
        }
        return result;
    }
}
