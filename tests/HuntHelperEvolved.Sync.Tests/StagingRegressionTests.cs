using HuntHelperEvolved.Sync;
using Xunit;

namespace HuntHelperEvolved.Sync.Tests;

public class StagingRegressionTests
{
    [Theory]
    [InlineData(301, SRankNameState.ConditionsUnmet)]
    [InlineData(300, SRankNameState.ConditionsUnmet)]
    [InlineData(299, SRankNameState.OpeningSoon)]
    [InlineData(1, SRankNameState.OpeningSoon)]
    [InlineData(0, SRankNameState.Ready)]
    public void FiveMinuteConditionWarningHasExactBoundaries(int seconds, SRankNameState expected)
    {
        var now=DateTime.UtcNow;
        var condition=new ConditionWindow(now.AddSeconds(seconds),now.AddMinutes(10));
        Assert.Equal(expected,SRankBoardFilter.NameState(SRankPhase.Window,true,condition,now));
        Assert.Equal(SRankNameState.NotReady,SRankBoardFilter.NameState(SRankPhase.Cooldown,true,condition,now));
        Assert.Equal(expected==SRankNameState.OpeningSoon,SRankBoardFilter.OpensSoon(condition.Start,now));
    }
    [Fact]
    public void EmptyTrainWithoutHistoryHasNothingToSubmit()
    {
        var empty=new System.Collections.Generic.List<TrackedMark>();
        Assert.Empty(TrainReport.ForReport(empty));
        Assert.Empty(TrainReport.SubmittedMarks(empty));
        Assert.Empty(TrainReport.BuildEntries(empty));
    }
    [Fact]
    public void EightConcurrentInstancesDieIndependentlyAndOldReportsCannotOverrideKillClocks()
    {
        var now = DateTime.UtcNow;
        var grace = new ActiveMarkGrace();
        for (uint instance=1; instance<=8; instance++)
            grace.Update(new() { Mark=new() { NameId=2953, WorldId=37, Instance=instance,
                HpPercent=100, SeenAt=now }, ObserverIds=new(){"scout"} }, now);
        for (uint instance=1; instance<=8; instance++)
        {
            var killed = now.AddSeconds(1);
            Assert.True(grace.IsAlive((2953,instance,37),null,killed));
            Assert.False(grace.IsAlive((2953,instance,37),killed,killed));
            var cycle=SRankTimerData.Compute(SRankTimerData.ByNameId[2953],new(){KilledAt=killed},killed,
                grace.IsAlive((2953,instance,37),killed,killed));
            Assert.Equal(SRankPhase.Cooldown,cycle.Phase);
        }
        Assert.False(grace.IsAlive((2953,1,37),null,now.AddSeconds(5)));
        // A new cycle can be seen later; an old instance's death must not hide it.
        grace.Update(new() { Mark=new() { NameId=2953,WorldId=37,Instance=1,HpPercent=100,SeenAt=now.AddDays(3) } },now.AddDays(3));
        Assert.True(grace.IsAlive((2953,1,37),now.AddSeconds(1),now.AddDays(3)));
        Assert.False(grace.IsAlive((2953,2,37),null,now.AddDays(3)));
    }
    [Theory]
    [InlineData(0,500,56,0,1080,500)]
    [InlineData(200,500,56,0,1080,144)]
    [InlineData(0,1080,56,0,1080,1024)]
    [InlineData(100,500,56,100,1080,600)]
    public void MapToolbarPreservesTitleBar(float top,float height,float bar,float viewportTop,float viewportHeight,float expected)
        => Assert.Equal(expected,MapBarPlacement.Top(top,height,bar,viewportTop,viewportHeight));
}
