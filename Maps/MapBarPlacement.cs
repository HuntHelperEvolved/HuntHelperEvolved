using System;

namespace HuntHelperEvolved;

public static class MapBarPlacement
{
    public static float Top(float mapTop, float mapHeight, float barHeight, float viewportTop, float viewportHeight)
    {
        if (mapTop - viewportTop >= barHeight) return mapTop - barHeight;
        // Prefer below the map; on a tall map use its bottom edge, never its title bar.
        return Math.Max(viewportTop, Math.Min(mapTop + mapHeight, viewportTop + viewportHeight - barHeight));
    }
}
