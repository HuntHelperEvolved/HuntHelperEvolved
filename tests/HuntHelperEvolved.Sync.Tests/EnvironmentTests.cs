using HuntHelperEvolved.Sync;
using Xunit;
public class EnvironmentTests
{
    [Fact]
    public void MetadataReplacesOldInstanceRowsAndOfflineStatusCanClear()
    {
        var status=new SyncFaloopStatus { MetadataAt=DateTime.UtcNow,OfflineWorlds=new(){"mateus"},ZoneInstances=new(){[148]=2} };
        Assert.True(status.IsOffline("Mateus"));
        Assert.Equal(new uint[]{1,2},status.CurrentInstances(148,new uint[]{0,1,3}));
        status.ZoneInstances[148]=1;status.OfflineWorlds.Clear();
        Assert.Equal(new uint[]{0},status.CurrentInstances(148,new uint[]{1,2}));
        Assert.False(status.IsOffline("Mateus"));
    }
    [Fact]
    public void OlderServersRetainObservationBasedInstances()
    {
        var status=new SyncFaloopStatus();
        Assert.False(status.IsOffline("Mateus"));
        Assert.Equal(new uint[]{0,2},status.CurrentInstances(148,new uint[]{2,0,2}));
    }
}
