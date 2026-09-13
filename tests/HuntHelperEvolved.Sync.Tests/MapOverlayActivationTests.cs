using Xunit;

namespace HuntHelperEvolved.Sync.Tests;

public class MapOverlayActivationTests
{
    [Fact]
    public void StartingInDutyNeverCreatesOrAttachesOverlay()
    {
        var policy = new MapOverlayActivation();
        for (var frame = 0; frame < 1000; frame++)
            Assert.False(policy.Update(false, () => throw new Exception("Must not attach in duty"), _ => throw new Exception("No overlay exists")));
        Assert.False(policy.Started);
    }
    [Fact]
    public void HuntDutyHuntChangesVisibilityWithoutRepeatedNativeAttachment()
    {
        var policy = new MapOverlayActivation();
        var starts = 0; var visibility = new List<bool>();
        bool Start() { starts++; return true; }
        for (var trip = 0; trip < 3; trip++)
        {
            for (var frame = 0; frame < 600; frame++) Assert.True(policy.Update(true, Start, visibility.Add));
            for (var frame = 0; frame < 600; frame++) Assert.False(policy.Update(false, Start, visibility.Add));
        }
        Assert.Equal(1, starts);
        Assert.Equal(new[] { true, false, true, false, true, false }, visibility);
    }
    [Fact]
    public void FailedStartupCanRetryWithoutClaimingAttachedState()
    {
        var policy = new MapOverlayActivation();
        Assert.False(policy.Update(true, () => false, _ => throw new Exception()));
        Assert.False(policy.Started); Assert.False(policy.Visible);
        Assert.True(policy.Update(true, () => true, _ => { }));
        Assert.True(policy.Started);
    }
    [Fact]
    public void DisableLoadingAndLogoutHideUntilEligibleAgain()
    {
        var policy = new MapOverlayActivation(); var visible = false;
        policy.Update(true, () => true, value => visible = value);
        policy.Update(false, () => throw new Exception(), value => visible = value);
        Assert.False(visible);
        Assert.True(policy.Update(true, () => throw new Exception("Already attached"), value => visible = value));
        Assert.True(visible);
    }
}
