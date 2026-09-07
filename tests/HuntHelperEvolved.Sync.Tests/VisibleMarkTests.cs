using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class VisibleMarkTests
{
    private static readonly DateTime Now=DateTime.UtcNow;
    private static VisibleMark Seen(float hp=100,bool? combat=false) => new()
    { Mark=new() { NameId=8905,WorldId=80,TerritoryId=813,Rank="S",HpPercent=hp,InCombat=combat,SeenAt=Now },ObserverIds=new(){"other"} };
    [Fact] public void FreshnessAndObserversAreRequired()
    {
        var row=Seen(); var options=new VisibleMarkOptions { IncludeOwn=false };
        Assert.True(VisibleMarkFilter.Matches(row,options,"me",Now,1,"Shadowbringers"));
        Assert.False(VisibleMarkFilter.Matches(row,options,"me",Now.AddSeconds(3),1,"Shadowbringers"));
        row.ObserverIds=new(){"me"}; Assert.False(VisibleMarkFilter.Matches(row,options,"me",Now,1,"Shadowbringers"));
        row.ObserverIds.Add("other"); Assert.True(VisibleMarkFilter.Matches(row,options,"me",Now,1,"Shadowbringers"));
        row.ObserverIds.Clear(); options.IncludeOwn=true; Assert.False(VisibleMarkFilter.Matches(row,options,"me",Now,1,"Shadowbringers"));
    }
    [Fact] public void CombatIsIndependentOfHealthAndDeadHasNoCombatLabel()
    {
        Assert.Equal("Pulled",VisibleMarkFilter.CombatLabel(Seen(100,true).Mark));
        Assert.Equal("Not pulled",VisibleMarkFilter.CombatLabel(Seen(70,false).Mark));
        Assert.Equal("Unknown",VisibleMarkFilter.CombatLabel(Seen(70,null).Mark));
        Assert.Equal("—",VisibleMarkFilter.CombatLabel(Seen(0,true).Mark));
        var options=new VisibleMarkOptions { Pulled=false,NotPulled=false,UnknownCombat=false };
        Assert.True(VisibleMarkFilter.Matches(Seen(0),options,"me",Now,1,"Shadowbringers"));
        Assert.False(VisibleMarkFilter.Matches(Seen(),options,"me",Now,1,"Shadowbringers"));
        options.Dead=false; Assert.False(VisibleMarkFilter.Matches(Seen(0),options,"me",Now,1,"Shadowbringers"));
    }
    [Fact] public void ScopeSelectionsIntersectAndRankTabsIncludeSsWithS()
    {
        var row=Seen(); var options=new VisibleMarkOptions { Worlds=new(){80,81},DataCenters=new(){1,2},Expansions=new(){"Shadowbringers","Endwalker"} };
        Assert.True(VisibleMarkFilter.Matches(row,options,"me",Now,1,"Shadowbringers"));
        Assert.False(VisibleMarkFilter.Matches(row,options,"me",Now,3,"Shadowbringers"));
        Assert.False(VisibleMarkFilter.Matches(row,options,"me",Now,1,"ARR"));
        row.Mark.WorldId=82; Assert.False(VisibleMarkFilter.Matches(row,options,"me",Now,1,"Shadowbringers"));
        Assert.True(ActiveMarkRows.MatchesTab("SS","S")); Assert.True(ActiveMarkRows.MatchesTab("B","All")); Assert.False(ActiveMarkRows.MatchesTab("A","S"));
    }
    [Fact] public void LiveCorpseOverridesCommunityActiveAndExpiredHealthIsNotReused()
    {
        var community=new SyncSRankStatus { NameId=8905,WorldId=80,SpawnedAt=Now.AddMinutes(-5),FaloopActiveAt=Now,FaloopActiveUntil=Now.AddMinutes(5) };
        var row=Assert.Single(ActiveMarkRows.Merge(new[]{Seen(0)},new[]{community},Now));
        Assert.True(row.HealthKnown); Assert.Equal(0,row.Mark.HpPercent);
        row=Assert.Single(ActiveMarkRows.Merge(new[]{Seen(65)},new[]{community},Now.AddSeconds(4)));
        Assert.False(row.HealthKnown); Assert.Null(row.Mark.InCombat); Assert.False(row.HasPosition);
        Assert.Empty(ActiveMarkRows.Merge(new[]{Seen(65)},new[]{community},Now.AddMinutes(6)));
    }
}
