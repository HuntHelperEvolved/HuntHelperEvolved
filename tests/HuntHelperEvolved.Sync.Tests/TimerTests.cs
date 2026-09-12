using HuntHelperEvolved.Sync;
using Xunit;

namespace HuntHelperEvolved.Sync.Tests;
public class TimerTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    [Fact]
    public void MappingCanRestartAtSnipeReceiptWithoutPretendingTheKillIsExact()
    {
        var status=new SyncSRankStatus { KilledAt=Now.AddHours(-24), Uncertain=true };
        var zone=new SyncSpawnZone { SinceAt=Now, ResetBySnipe=true };
        Assert.True(SpawnMapping.ReliableCycle(zone,status));
        zone.ResetBySnipe=false;
        Assert.False(SpawnMapping.ReliableCycle(zone,status));
    }

    [Theory]
    [InlineData(90, SRankPhase.Cooldown, 0)]
    [InlineData(120, SRankPhase.Window, 0)]
    [InlineData(144, SRankPhase.Window, 50)]
    [InlineData(168, SRankPhase.Forced, 100)]
    public void SafatSnipedWindowMatchesFaloopBounds(double elapsed, SRankPhase phase, double percent)
    {
        var baseline = Now.AddHours(-elapsed);
        var cycle = SRankTimerData.Compute(SRankTimerData.ByNameId[2968], new()
        { KilledAt=baseline.AddHours(60),KilledAtLatest=baseline.AddHours(84),Uncertain=true },Now,false);
        Assert.Equal(baseline.AddHours(120),cycle.OpensAtUtc);
        Assert.Equal(baseline.AddHours(168),cycle.ForcedAtUtc);
        Assert.Equal(phase,cycle.Phase); Assert.Equal(percent,cycle.Percent);
    }

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
    [Theory]
    [InlineData(50, SRankPhase.Cooldown, 0)]
    [InlineData(50.4, SRankPhase.Window, 0)]
    [InlineData(64.8, SRankPhase.Window, 50)]
    [InlineData(79.2, SRankPhase.Forced, 100)]
    public void MaintenanceKeepsExactFaloopBoundaries(double hours, SRankPhase phase, double percent)
    {
        var restart = new DateTime(2026, 9, 9, 6, 40, 0, DateTimeKind.Utc);
        var cycle = SRankTimerData.Compute(SRankTimerData.ByNameId[8905],
            new() { KilledAt = restart, Maintenance = true }, restart.AddSeconds(Math.Round(hours * 3600)), false);
        Assert.Equal(new DateTime(2026, 9, 11, 9, 4, 0, DateTimeKind.Utc), cycle.OpensAtUtc);
        Assert.Equal(new DateTime(2026, 9, 12, 13, 52, 0, DateTimeKind.Utc), cycle.ForcedAtUtc);
        Assert.Equal(phase, cycle.Phase);
        Assert.Equal(percent, cycle.Percent, 8);
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
        var c = SRankTimerData.Compute(SRankTimerData.ByNameId[8905], new() { KilledAt = Now.AddHours(-64.8), Maintenance = true }, Now, false);
        Assert.Equal(50, c.Percent, 8);
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
