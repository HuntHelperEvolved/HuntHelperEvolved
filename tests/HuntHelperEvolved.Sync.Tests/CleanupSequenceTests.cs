using Xunit;

namespace HuntHelperEvolved.Sync.Tests;

public sealed class CleanupSequenceTests
{
    [Fact]
    public void FailedUnregisterAndDisableStillReleaseRemainingResources()
    {
        var cleanup = new CleanupSequence();
        var calls = new List<string>();
        cleanup.Run("unregister", () => { calls.Add("unregister"); throw new InvalidOperationException("event failure"); });
        cleanup.Run("disable hook", () => { calls.Add("disable"); throw new InvalidOperationException("hook failure"); });
        cleanup.Run("dispose hook", () => calls.Add("dispose"));
        cleanup.Run("disconnect sync", () => calls.Add("disconnect"));
        cleanup.Run("save train", () => calls.Add("save"));
        var error = Assert.Throws<AggregateException>(cleanup.ThrowIfFailed);
        Assert.Equal(new[] { "unregister", "disable", "dispose", "disconnect", "save" }, calls);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Contains("unregister", error.InnerExceptions[0].Message);
        Assert.Equal("hook failure", error.InnerExceptions[1].InnerException!.Message);
    }

    [Fact]
    public void FailedTeardownCannotBecomeSuccessfulByCheckingItAgain()
    {
        var cleanup = new CleanupSequence();
        cleanup.Run("native overlay", () => throw new InvalidOperationException());
        Assert.Throws<AggregateException>(cleanup.ThrowIfFailed);
        // Runtime keeps this result: another Start/Exit must not replace a
        // partly stopped runtime with a new set of hooks and native nodes.
        Assert.Throws<AggregateException>(cleanup.ThrowIfFailed);
    }

    [Fact]
    public void SuccessfulCleanupAllowsSessionReplacement()
    {
        var cleanup = new CleanupSequence();
        var connected = true;
        cleanup.Run("disconnect", () => connected = false);
        cleanup.ThrowIfFailed();
        Assert.False(connected);
    }
}
