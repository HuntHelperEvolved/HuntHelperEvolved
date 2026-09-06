using System.Collections.Generic;
namespace HuntHelperEvolved.Sync;
public static class SharedRouteGrouping
{
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
