using System;

namespace HuntHelperEvolved.Sync;

internal static class ARankSpawnProgress
{
    // Shared by the A-rank board and external train-status summaries. A missing
    // window is unknown, while a known unopened window is genuinely zero.
    internal static double? Fraction(bool spawned, DateTime? opens, DateTime? ends, DateTime now) =>
        spawned ? 1 : opens is { } start && ends is { } end && end > start
            ? Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1)
            : null;
}
