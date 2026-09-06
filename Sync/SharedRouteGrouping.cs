using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;
public static class SharedRouteGrouping
{
    /// <summary>Keep existing block order and relative scout order; fold new appended marks into their block.</summary>
    public static List<T> GroupInRouteOrder<T>(IEnumerable<T> marks, Func<T, string> expansion) =>
        marks.GroupBy(expansion).SelectMany(block => block).ToList();

    public static bool HasContiguousBlocks(IEnumerable<string> expansions)
    {
        var seen = new HashSet<string>(); string? previous = null;
        foreach (var expansion in expansions)
        {
            if (expansion == previous) continue;
            if (!seen.Add(expansion)) return false;
            previous = expansion;
        }
        return true;
    }
}
