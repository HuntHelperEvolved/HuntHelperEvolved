using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class MappingTests
{
    private static readonly SpawnPoint[] Points={new(10,10,SpawnRanks.A|SpawnRanks.S),new(15,10,SpawnRanks.B|SpawnRanks.S)};
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
