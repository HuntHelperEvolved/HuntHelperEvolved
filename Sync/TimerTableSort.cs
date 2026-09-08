using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public static class TimerTableSort
{
    // Compare underlying numbers/timestamps, not formatted labels. Unknowns stay
    // last in either direction; stable sorting preserves deterministic tie order.
    public static List<T> Apply<T>(IEnumerable<T> rows, Func<T, IComparable?> key, bool descending) =>
        rows.OrderBy(key, Comparer<IComparable?>.Create((left, right) =>
        {
            if (left is null) return right is null ? 0 : 1;
            if (right is null) return -1;
            var result = left is string a && right is string b
                ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : left.CompareTo(right);
            return descending ? -Math.Sign(result) : result;
        })).ToList();
}
