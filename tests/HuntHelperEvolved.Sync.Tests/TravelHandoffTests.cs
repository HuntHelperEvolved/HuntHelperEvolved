using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class TravelHandoffTests
{
    [Fact]
    public void WorldTravelSurvivesIdleGapsAndWaitsForArrivalReadiness()
    {
        var now=DateTime.UtcNow;var handoff=new TravelHandoff(81,now);
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(10),80,false,true));
        Assert.False(handoff.Expired(now.AddSeconds(10)));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(20),0,false,false));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(30),81,true,true));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(31),81,false,true));
        Assert.True(handoff.ShouldAttempt(now.AddSeconds(33),81,false,true));
    }
    [Fact]
    public void TemporaryRefusalRetriesWithoutAnotherClickAndIsBounded()
    {
        var now=DateTime.UtcNow;var handoff=new TravelHandoff(81,now);
        handoff.ShouldAttempt(now,81,false,true);
        Assert.True(handoff.ShouldAttempt(now.AddSeconds(2),81,false,true));
        handoff.Refused(now.AddSeconds(2));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(2.5),81,false,true));
        Assert.True(handoff.ShouldAttempt(now.AddSeconds(3),81,false,true));
        Assert.True(handoff.Expired(now.AddSeconds(32)));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(33),81,false,true));
    }
    [Fact]
    public void LoadingRestartsSettlingAndOverallTravelTimesOut()
    {
        var now=DateTime.UtcNow;var handoff=new TravelHandoff(81,now);
        handoff.ShouldAttempt(now,81,false,true);
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(1),81,false,false));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(2),81,false,true));
        Assert.False(handoff.ShouldAttempt(now.AddSeconds(3),81,false,true));
        Assert.True(handoff.ShouldAttempt(now.AddSeconds(4),81,false,true));
        Assert.True(handoff.Expired(now.AddMinutes(15)));
    }
    [Fact]
    public void LocalFoundSuppressesOwnRelayButNotOtherWorldsOrLaterCycles()
    {
        var now=DateTime.UtcNow;var filter=new SpawnAlertFilter();
        filter.RecordLocal(8905,80,1,now);
        var spawn=new SRankSpawnBroadcast {NameId=8905,WorldId=80,Instance=1,SpawnedAt=now};
        Assert.False(filter.Accept(spawn,true,now.AddSeconds(1)));
        spawn.Event="release";Assert.False(filter.Accept(spawn,true,now.AddSeconds(2)));
        spawn.WorldId=81;Assert.True(filter.Accept(spawn,true,now.AddSeconds(2)));
        spawn.WorldId=80;spawn.Instance=2;Assert.True(filter.Accept(spawn,true,now.AddSeconds(2)));
        spawn.Instance=1;spawn.SpawnedAt=now.AddHours(48);
        Assert.True(filter.Accept(spawn,true,spawn.SpawnedAt));
    }
}
