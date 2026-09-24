using System;

namespace HuntHelperEvolved.Sync;

/// <summary>Maps the local clock to the server clock without rewriting received observations.</summary>
public sealed class RemoteSightingClock
{
    private long _offsetTicks;

    public void Reset() => _offsetTicks = 0;

    public void Update(DateTime serverTime, DateTime receivedAt)
    {
        // Older servers may omit the sample. Keep the last usable offset.
        if (serverTime == default) return;
        _offsetTicks = Utc(serverTime).Ticks - Utc(receivedAt).Ticks;
    }

    public DateTime ToServer(DateTime localTime) => Shift(localTime, _offsetTicks);
    public DateTime ToLocal(DateTime serverTime) => Shift(serverTime, -_offsetTicks);

    private static DateTime Shift(DateTime value, long ticks) => new(
        Math.Clamp(Utc(value).Ticks + ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
