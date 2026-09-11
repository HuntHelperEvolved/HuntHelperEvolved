using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class MappingTests
{
    private static readonly SpawnPoint[] Points={new(10,10,SpawnRanks.A|SpawnRanks.S),new(15,10,SpawnRanks.B|SpawnRanks.S)};
    [Fact]
    public void ManualExclusionsCountTowardLastCandidateAndUndoPreservesAutomaticReason()
    {
        var points=new[] {new SpawnPoint(10,10,SpawnRanks.S),new SpawnPoint(20,20,SpawnRanks.S)};
        var zone=new SyncSpawnZone { SinceAt=DateTime.UtcNow,Eliminated=new(){new(){Index=0,Rank="Manual"}} };
        Assert.Equal(1,SpawnMapping.ConfirmedPoint(points,zone,true));
        zone.Eliminated.Add(new(){Index=0,Rank="A"});
        zone.Eliminated.RemoveAll(e=>e.Rank=="Manual");
        Assert.True(zone.IsRuledOut(0));
        Assert.Equal(1,SpawnMapping.ConfirmedPoint(points,zone,true));
    }
    [Fact]
    public void TravelUsesRemainingCandidatesOnlyWithReliableMapping()
    {
        var zone = new SyncSpawnZone { SinceAt = DateTime.UtcNow, LastSDeathIndex = 0 };
        Assert.Equal(new System.Numerics.Vector2(15,10), SpawnMapping.TravelEstimate(Points,zone,true));
        Assert.Equal(new System.Numerics.Vector2(12.5f,10), SpawnMapping.TravelEstimate(Points,zone,false));
        zone.SinceAt = null;
        Assert.Equal(new System.Numerics.Vector2(12.5f,10), SpawnMapping.TravelEstimate(Points,zone,true));
    }
    [Fact]
    public void TravelFallsBackForEmptyMappingAndIgnoresOtherRanks()
    {
        SpawnPoint[] points = { new(10,10,SpawnRanks.S), new(30,30,SpawnRanks.A) };
        var zone = new SyncSpawnZone { SinceAt = DateTime.UtcNow, LastSDeathIndex = 0 };
        Assert.Equal(new System.Numerics.Vector2(10,10), SpawnMapping.TravelEstimate(points,zone,true));
        Assert.Equal(new System.Numerics.Vector2(10,10), SpawnMapping.TravelEstimate(points,null,false));
        Assert.Equal(new System.Numerics.Vector2(21.5f,21.5f), SpawnMapping.TravelEstimate([],null,false));
    }
    [Fact]
    public void TravelUsesObservedSpawnPointBeforeCandidateAverage()
    {
        var zone = new SyncSpawnZone { SCurrentIndex = 1 };
        Assert.Equal(new System.Numerics.Vector2(15,10), SpawnMapping.TravelEstimate(Points,zone,false));
    }
    [Fact]
    public void IdlePatrolCanMatchOriginButDistantOrWrongRankCannot()
    {
        Assert.Equal(0,SpawnMapping.Match(Points,new(11.2f,10),SpawnRanks.A));
        Assert.Null(SpawnMapping.Match(Points,new(13,10),SpawnRanks.A));
        Assert.Null(SpawnMapping.Match(Points,new(10,10),SpawnRanks.B));
    }
    [Fact]
    public void AmbiguousOriginIsNotEliminated()
    {
        SpawnPoint[] close={new(10,10,SpawnRanks.A),new(12,10,SpawnRanks.A)};
        Assert.Null(SpawnMapping.Match(close,new(11,10),SpawnRanks.A));
    }
    [Fact]
    public void LastPointNeedsReliableCycleToBecomeSolid()
    {
        var zone=new SyncSpawnZone { SinceAt=DateTime.UtcNow, LastSDeathIndex=0 };
        Assert.Null(SpawnMapping.ConfirmedPoint(Points,zone,false));
        Assert.Equal(1,SpawnMapping.ConfirmedPoint(Points,zone,true));
        zone.LastSDeathIndex=null;
        Assert.Null(SpawnMapping.ConfirmedPoint(Points,zone,true));
        zone.SCurrentIndex=0;
        Assert.Equal(0,SpawnMapping.ConfirmedPoint(Points,zone,false));
    }
    [Theory]
    [InlineData(SRankPhase.Window,true)]
    [InlineData(SRankPhase.Forced,true)]
    [InlineData(SRankPhase.Cooldown,false)]
    [InlineData(SRankPhase.Up,false)]
    [InlineData(SRankPhase.Unknown,false)]
    [InlineData(SRankPhase.Uncertain,false)]
    public void AvailableFilterRequiresKnownOpenWindow(SRankPhase phase,bool expected) => Assert.Equal(expected,SRankBoardFilter.Available(phase));
}
