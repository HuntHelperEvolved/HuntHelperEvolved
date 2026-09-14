using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public class StartupTransactionTests
{
    [Fact]
    public void FailureRollsBackInReverseOrderAndContinuesAfterCleanupFailure()
    {
        var startup = new StartupTransaction();
        var undone = new List<string>();
        startup.Add(() => undone.Add("hook"));
        startup.Add(() => { undone.Add("sync"); throw new InvalidOperationException("disconnect failed"); });
        startup.Add(() => undone.Add("UI"));
        var cause = new Exception("late startup failure");
        var error = Assert.IsType<StartupFailureException>(startup.Rollback(cause));
        Assert.Equal(new[] { "UI", "sync", "hook" }, undone);
        Assert.Same(cause, error.InnerExceptions[0]);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.True(error.RollbackFailed);
        startup.Rollback(cause);
        Assert.Equal(3, undone.Count);
    }
    [Fact]
    public void NestedFailedRollbackCannotBeMistakenForSafeRetry()
    {
        var inner = new StartupTransaction();
        inner.Add(() => throw new Exception("native cleanup failed"));
        var outer = new StartupTransaction();
        var failure = Assert.IsType<StartupFailureException>(outer.Rollback(inner.Rollback(new Exception("startup"))));
        Assert.True(failure.RollbackFailed);
    }
    [Fact]
    public void CommitTransfersOwnershipWithoutDisposingLiveResources()
    {
        var startup = new StartupTransaction();
        startup.Add(() => throw new Exception("must remain alive"));
        startup.Commit();
        var error = Assert.IsType<StartupFailureException>(startup.Rollback(new Exception()));
        Assert.Single(error.InnerExceptions);
        Assert.False(error.RollbackFailed);
    }
}
