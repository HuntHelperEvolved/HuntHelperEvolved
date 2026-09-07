using HuntHelperEvolved.Sync;
using Xunit;

namespace HuntHelperEvolved.Sync.Tests;
public class TimerTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData(80, SRankPhase.Cooldown, 0)]
    [InlineData(84, SRankPhase.Window, 0)]
    [InlineData(108, SRankPhase.Window, 50)]
    [InlineData(132, SRankPhase.Forced, 100)]
    public void NormalWindowBoundaries(double hours, SRankPhase phase, double percent)
    {
        var c = SRankTimerData.Compute(SRankTimerData.ByNameId[8905], new() { KilledAt = Now.AddHours(-hours) }, Now, false);
        Assert.Equal(phase, c.Phase); Assert.Equal(percent, c.Percent);
    }
    [Fact]
    public void OldSightingDoesNotLeaveMarkPermanentlyUp()
    {
        var c = SRankTimerData.Compute(SRankTimerData.ByNameId[8905], new() { KilledAt = Now.AddHours(-100), SpawnedAt = Now.AddHours(-1), LastSeenUpAt = Now.AddHours(-1) }, Now, false);
        Assert.Equal(SRankPhase.Window, c.Phase);
    }
    [Fact]
    public void UncertainKillHasNoPercentageOrLatestDeadline()
    {
        var c = SRankTimerData.Compute(SRankTimerData.ByNameId[8905], new() { KilledAt = Now.AddHours(-100), Uncertain = true }, Now, false);
        Assert.Equal(SRankPhase.Uncertain, c.Phase); Assert.Null(c.ForcedAtUtc); Assert.Equal(0, c.Percent);
    }
    [Fact]
    public void MaintenanceUsesShorterWindow()
    {
        var c = SRankTimerData.Compute(SRankTimerData.ByNameId[8905], new() { KilledAt = Now.AddHours(-65), Maintenance = true }, Now, false);
        Assert.Equal(50, c.Percent);
    }
    [Fact]
    public void FixedWindowHasNoDivisionByZero()
    {
        var c = SRankTimerData.Compute(SRankTimerData.ByNameId[2955], new() { KilledAt = Now.AddHours(-50) }, Now, false);
        Assert.Equal(100, c.Percent); Assert.Equal(SRankPhase.Forced, c.Phase);
    }
    [Fact]
    public void LiveSightingWinsWithoutKnownKill()
    {
        Assert.Equal(SRankPhase.Up, SRankTimerData.Compute(SRankTimerData.ByNameId[8905], null, Now, true).Phase);
    }
}
