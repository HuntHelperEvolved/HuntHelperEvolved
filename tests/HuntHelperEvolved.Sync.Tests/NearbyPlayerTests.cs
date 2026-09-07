using System.Numerics;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class NearbyPlayerTests
{
    [Fact]
    public void CulledPlayersRemainEstimatedAndFreshPositionsReplaceOldOnes()
    {
        var cache=new NearbyPlayerCache(); cache.SetScope(37,1187,1);
        cache.Observe(1,Vector3.Zero); cache.Observe(2,new(50,0,0));
        Assert.Equal(2,cache.CountNear(Vector3.Zero));
        // Next frame supplies only one character; the other was culled.
        cache.Observe(1,Vector3.Zero); Assert.Equal(2,cache.CountNear(Vector3.Zero));
        cache.Observe(2,new(51,0,0)); Assert.Equal(1,cache.CountNear(Vector3.Zero));
        cache.Observe(1,Vector3.Zero); Assert.Equal(1,cache.CountNear(Vector3.Zero));
    }
    [Theory]
    [InlineData(38,1187,1)][InlineData(37,1188,1)][InlineData(37,1187,2)]
    public void ChangedScopeClearsEstimates(uint world,uint territory,uint instance)
    {
        var cache=new NearbyPlayerCache();cache.SetScope(37,1187,1);cache.Observe(1,Vector3.Zero);
        cache.SetScope(world,territory,instance);Assert.Equal(0,cache.CountNear(Vector3.Zero));
    }
    [Fact]
    public void LogoutAndInvalidCharactersDoNotCarryCountsForward()
    {
        var cache=new NearbyPlayerCache();cache.Observe(1,Vector3.Zero);cache.Clear();
        cache.Observe(0,Vector3.Zero);cache.Observe(0xE0000000,Vector3.Zero);cache.Observe(2,new(float.NaN,0,0));
        Assert.Equal(0,cache.CountNear(Vector3.Zero));
    }
}
