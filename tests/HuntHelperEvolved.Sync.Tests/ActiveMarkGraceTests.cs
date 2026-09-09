using System;
using System.Collections.Generic;
using Xunit;
namespace HuntHelperEvolved.Sync;
public class ActiveMarkGraceTests
{
    private static readonly DateTime Now = new(2026,9,9,0,0,0,DateTimeKind.Utc);
    private static VisibleMark Row(DateTime at, float hp=100, params string[] observers) => new() {
        Mark=new() { NameId=1, WorldId=37, SeenAt=at, HpPercent=hp }, ObserverIds=new(observers), Observers=new(observers) };
    [Fact]
    public void DelayedReportsRemainForFiveSecondsFromReceiptWithoutRefreshingOnReads()
    {
        var cache=new ActiveMarkGrace(); cache.Update(Row(Now.AddSeconds(-4),100,"Scout"),Now);
        var row=Assert.Single(cache.Snapshot(Now.AddSeconds(4.9)));
        Assert.True(VisibleMarkFilter.Fresh(row,Now.AddSeconds(4.9)));
        Assert.Equal(Now.AddSeconds(-4),row.Mark.SeenAt);
        Assert.Empty(cache.Snapshot(Now.AddSeconds(5)));
    }
    [Fact]
    public void ObserverNamesSurviveBriefGapsButExpireIndividually()
    {
        var cache=new ActiveMarkGrace();cache.Update(Row(Now,100,"One","Two"),Now);
        cache.Update(Row(Now.AddSeconds(3),80,"One"),Now.AddSeconds(3));
        Assert.Contains("Two",Assert.Single(cache.Snapshot(Now.AddSeconds(4))).Observers);
        Assert.DoesNotContain("Two",Assert.Single(cache.Snapshot(Now.AddSeconds(5))).Observers);
    }
    [Fact]
    public void DeathReplacesAliveImmediatelyAndClearDropsAllRows()
    {
        var cache=new ActiveMarkGrace();cache.Update(Row(Now,100,"One"),Now);
        cache.Update(Row(Now.AddSeconds(1),0,"One"),Now.AddSeconds(1));
        cache.Update(Row(Now,100,"One"),Now.AddSeconds(2));
        Assert.Equal(0,Assert.Single(cache.Snapshot(Now.AddSeconds(2))).Mark.HpPercent);
        cache.Clear();Assert.Empty(cache.Snapshot(Now.AddSeconds(2)));
    }
    [Fact]
    public void OwnAliasBecomesYouWithoutDuplicatingIt()
    {
        var row=new VisibleMark { ObserverIds=new(){"self","other"},Observers=new(){"Alias","Friend","You"} };
        Assert.Equal(new[]{"Friend","You"},ActiveMarkGrace.ObserverLabels(row,"self","Alias"));
        Assert.Contains("Alias",ActiveMarkGrace.ObserverLabels(row,"absent","Alias"));
    }
}
