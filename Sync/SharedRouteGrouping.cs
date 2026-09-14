using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;
public static class SharedRouteGrouping
{
    public static List<T> GroupByWorldInRouteOrder<T>(IEnumerable<T> marks,
        Func<T, uint> world, Func<T, string> expansion, bool groupExpansions = true) =>
        marks.GroupBy(world).SelectMany(block => groupExpansions
            ? GroupInRouteOrder(block, expansion)
            : block.ToList()).ToList();

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
