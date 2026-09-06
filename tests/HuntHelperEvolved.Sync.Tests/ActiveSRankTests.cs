using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class ActiveSRankTests
{
    [Fact]
    public void FreshCommunityMembershipSurvivesOldSpawnTimestampButExpiresAndDies()
    {
        var now = DateTime.UtcNow;
        var s = new SyncSRankStatus { SpawnedAt=now.AddMinutes(-20), FaloopActiveAt=now, FaloopActiveUntil=now.AddMinutes(5) };
        Assert.Equal("Faloop reports active", ActiveSRankFilter.Status(s,false,now));
        Assert.Equal(SRankPhase.Up, SRankTimerData.Compute(SRankTimerData.ByNameId[8905], s, now, false).Phase);
        Assert.Null(ActiveSRankFilter.Status(s,false,now.AddMinutes(6)));
        s.KilledAt=now;
        Assert.Null(ActiveSRankFilter.Status(s,false,now));
    }
    [Fact]
    public void OldSightingsAndWithdrawnCommunityReportsDoNotRemainActive()
    {
        var s = new SyncSRankStatus { SpawnedAt=DateTime.UtcNow, LastSeenUpAt=DateTime.UtcNow };
        Assert.Equal("Visible to a scout",ActiveSRankFilter.Status(s,true,DateTime.UtcNow));
        Assert.Null(ActiveSRankFilter.Status(s,false,DateTime.UtcNow));
    }
}
