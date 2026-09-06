using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class SpawnAlertTests
{
    [Fact]
    public void FiltersScopeHistoryFutureAndDuplicatesButAllowsOtherWorlds()
    {
        var filter = new SpawnAlertFilter(); var now = DateTime.UtcNow;
        var s = new SRankSpawnBroadcast { NameId=8905, WorldId=80, SpawnedAt=now };
        Assert.False(filter.Accept(s, false, now));
        Assert.False(filter.Accept(s, true, now.AddMinutes(3)));
        Assert.False(filter.Accept(s, true, now.AddMinutes(-1)));
        Assert.True(filter.Accept(s, true, now));
        Assert.False(filter.Accept(s, true, now));
        s.SpawnedAt = now.AddMinutes(1); Assert.False(filter.Accept(s, true, s.SpawnedAt));
        s.WorldId=81; Assert.True(filter.Accept(s, true, s.SpawnedAt));
        s.SpawnedAt=now.AddHours(84); Assert.True(filter.Accept(s,true,s.SpawnedAt));
    }
}
