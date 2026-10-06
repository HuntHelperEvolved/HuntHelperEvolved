using System;

namespace HuntHelperEvolved.Sync;

internal static class ARankSpawnProgress
{
    // Shared by the A-rank board and external train-status summaries. A missing
    // opening is unknown until the latest possible respawn; a known unopened
    // window is genuinely zero.
    internal static double? Fraction(bool spawned, DateTime? opens, DateTime? ends, DateTime now) =>
        spawned || ends is { } latest && now >= latest ? 1 : opens is { } start && ends is { } end && end > start
            ? Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1)
            : null;
}
