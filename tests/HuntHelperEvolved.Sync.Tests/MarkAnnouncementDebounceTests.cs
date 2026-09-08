using System.Numerics;
using Xunit;
namespace HuntHelperEvolved;
public class MarkAnnouncementDebounceTests
{
    [Fact]
    public void ReacquiringSameSpotDoesNotRepeatAndNewSpotDoes()
    {
        var d = new MarkAnnouncementDebounce();
        Assert.True(d.ShouldAnnounce(1, 0, 37, 2, new(10,10), false));
        for (var i=0;i<20;i++) Assert.False(d.ShouldAnnounce(1, 0, 37, 2, new(10.1f,10), false));
        Assert.True(d.ShouldAnnounce(1, 0, 37, 3, new(12,10), false));
        Assert.True(d.ShouldAnnounce(1, 0, 37, 2, new(10,10), false));
    }
    [Fact]
    public void KillAndZoneChangeResetSuppression()
    {
        var d = new MarkAnnouncementDebounce();
        Assert.True(d.ShouldAnnounce(1, 0, 37, 2, new(10,10), false));
        d.Killed(1,0,37);
        Assert.True(d.ShouldAnnounce(1, 0, 37, 2, new(10,10), false));
        d.Clear();
        Assert.True(d.ShouldAnnounce(1, 0, 37, 2, new(10,10), false));
    }
    [Fact]
    public void UnknownSpotsUsePositionAndLearnSpotWithoutRepeating()
    {
        var d = new MarkAnnouncementDebounce();
        Assert.True(d.ShouldAnnounce(1, 0, 37, null, new(10,10), false));
        Assert.False(d.ShouldAnnounce(1, 0, 37, null, new(10.1f,10), false));
        Assert.False(d.ShouldAnnounce(1, 0, 37, 2, new(10,10), false));
        Assert.False(d.ShouldAnnounce(1, 0, 37, null, new(30,30), true));
        Assert.True(d.ShouldAnnounce(1, 0, 37, 3, new(30,30), false));
    }
    [Fact]
    public void DifferentMarksWorldsAndInstancesAreIndependent()
    {
        var d = new MarkAnnouncementDebounce();
        Assert.True(d.ShouldAnnounce(1, 0, 37, 2, Vector2.Zero, false));
        Assert.True(d.ShouldAnnounce(2, 0, 37, 2, Vector2.Zero, false));
        Assert.True(d.ShouldAnnounce(1, 1, 37, 2, Vector2.Zero, false));
        Assert.True(d.ShouldAnnounce(1, 0, 38, 2, Vector2.Zero, false));
        d.Killed(2,0,37);
        Assert.False(d.ShouldAnnounce(1, 0, 37, 2, Vector2.Zero, false));
    }
}
