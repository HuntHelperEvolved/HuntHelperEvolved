using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public sealed class ConditionVisibilityTests
{
    [Fact]
    public void RedHidesAndYellowGreenShowAcrossConditionBoundaries()
    {
        var start = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var window = new ConditionWindow(start, start.AddMinutes(10));
        bool Visible(DateTime now) => SRankBoardFilter.MatchesConditions(true, SRankPhase.Window, true, window, now);
        Assert.False(Visible(start.AddMinutes(-6)));
        Assert.False(Visible(start.AddMinutes(-5)));
        Assert.True(Visible(start.AddMinutes(-5).AddTicks(1)));
        Assert.True(Visible(start));
        Assert.True(Visible(window.End.AddTicks(-1)));
        Assert.False(Visible(window.End));
        Assert.True(SRankBoardFilter.MatchesConditions(false, SRankPhase.Window, true, window, window.End));
    }

    [Fact]
    public void UntimedAndGreyStatesAreUnaffected()
    {
        var now = DateTime.UtcNow;
        Assert.True(SRankBoardFilter.MatchesConditions(true, SRankPhase.Forced, false, null, now));
        Assert.True(SRankBoardFilter.MatchesConditions(true, SRankPhase.Window, true, null, now));
        foreach (var phase in new[] { SRankPhase.Up, SRankPhase.Cooldown, SRankPhase.Unknown, SRankPhase.Uncertain })
            Assert.True(SRankBoardFilter.MatchesConditions(true, phase, true, new(now.AddHours(1), now.AddHours(2)), now));
    }
}
