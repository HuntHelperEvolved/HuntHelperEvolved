using HuntHelperEvolved.Sync;
using Xunit;

public class InstanceTravelTests
{
    private static readonly DateTime Now = new(2026,9,12,12,0,0,DateTimeKind.Utc);
    [Fact]
    public void WaitsForCorrectWorldZoneAndSettledArrivalThenRequestsOnlyOnce()
    {
        var handoff=new InstanceTravelHandoff(37,134,2,Now);
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now,38,134,1,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now.AddSeconds(3),37,135,1,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now.AddSeconds(4),37,134,1,3,true,true,true));
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now.AddSeconds(5),37,134,1,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Change,handoff.Step(Now.AddSeconds(7),37,134,1,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now.AddSeconds(8),37,134,1,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now.AddSeconds(9),37,134,2,3,true,true,false));
        Assert.Equal(InstanceTravelAction.Wait,handoff.Step(Now.AddSeconds(10),37,134,2,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Complete,handoff.Step(Now.AddSeconds(12),37,134,2,3,true,false,true));
    }
    [Fact]
    public void AlreadyInCorrectInstanceDoesNotRequestTravel()
    {
        var h=new InstanceTravelHandoff(37,134,2,Now);
        h.Step(Now,37,134,2,0,false,false,true);
        Assert.Equal(InstanceTravelAction.Complete,h.Step(Now.AddSeconds(2),37,134,2,0,false,false,true));
    }
    [Fact]
    public void RetiredInstanceIsRejectedAndUnavailableTravelTimesOut()
    {
        var h=new InstanceTravelHandoff(37,134,3,Now);
        h.Step(Now,37,134,1,2,true,false,true);
        Assert.Equal(InstanceTravelAction.Unavailable,h.Step(Now.AddSeconds(2),37,134,1,2,true,false,true));
        var waiting=new InstanceTravelHandoff(37,134,2,Now);
        Assert.Equal(InstanceTravelAction.Wait,waiting.Step(Now.AddSeconds(10),37,134,0,0,false,false,true));
        Assert.Equal(InstanceTravelAction.TimedOut,waiting.Step(Now.AddMinutes(2),37,134,0,0,false,false,true));
    }
    [Fact]
    public void LoadingResetsSettlingPeriod()
    {
        var h=new InstanceTravelHandoff(37,134,2,Now);
        h.Step(Now,37,134,1,3,true,false,true);
        h.Step(Now.AddSeconds(1),37,134,1,3,true,false,false);
        Assert.Equal(InstanceTravelAction.Wait,h.Step(Now.AddSeconds(3),37,134,1,3,true,false,true));
        Assert.Equal(InstanceTravelAction.Change,h.Step(Now.AddSeconds(5),37,134,1,3,true,false,true));
    }
    [Fact]
    public void CountMergeRetainsNewestHealthWithoutAlternatingOrMutatingReports()
    {
        var remote=new SyncSighting { SeenAt=Now,HpPercent=90,NearbyPlayers=20 };
        var local=new SyncSighting { SeenAt=Now.AddSeconds(1),HpPercent=50,NearbyPlayers=10 };
        var merged=ActiveMarkRows.MergeObservation(local,remote);
        Assert.Equal(20,merged.NearbyPlayers);
        Assert.Equal(50,merged.HpPercent);
        Assert.Equal(10,local.NearbyPlayers);
        Assert.Equal(90,remote.HpPercent);
        remote.SeenAt=Now.AddSeconds(2); remote.HpPercent=0;
        Assert.Equal(20,ActiveMarkRows.MergeObservation(local,remote).NearbyPlayers);
        Assert.Equal(0,ActiveMarkRows.MergeObservation(local,remote).HpPercent);
    }
    [Fact]
    public void UnknownCountsDoNotHideKnownCounts()
    {
        var local=new SyncSighting { SeenAt=Now,NearbyPlayers=null };
        var remote=new SyncSighting { SeenAt=Now,NearbyPlayers=0 };
        Assert.Equal(0,ActiveMarkRows.MergeObservation(local,remote).NearbyPlayers);
        remote.NearbyPlayers=null;
        Assert.Null(ActiveMarkRows.MergeObservation(local,remote).NearbyPlayers);
    }
}
