using Xunit;
namespace HuntHelperEvolved.Tests;
public class TrainExpansionProgressTests
{
    [Fact]
    public void FinishingAnExpansionOpensTheSameExpansionOnTheNextWorld()
    {
        var tracker = new TrainExpansionProgress();
        tracker.Update(new[] { ("63:Dawntrail", false), ("37:Dawntrail", false), ("37:Endwalker", false) });
        Assert.Equal(new[] { "37:Dawntrail" }, tracker.Update(new[]
            { ("63:Dawntrail", true), ("37:Dawntrail", false), ("37:Endwalker", false) }));
        Assert.Equal(new[] { "37:Endwalker" }, tracker.Update(new[]
            { ("63:Dawntrail", true), ("37:Dawntrail", true), ("37:Endwalker", false) }));
    }

    [Fact]
    public void OpensOnceAndSkipsFinishedLegsInRouteOrder()
    {
        var tracker = new TrainExpansionProgress();
        Assert.Empty(tracker.Update(new[] {("DT",false),("EW",true),("ShB",false)}));
        Assert.Equal(new[] {"ShB"},tracker.Update(new[] {("DT",true),("EW",true),("ShB",false)}));
        Assert.Empty(tracker.Update(new[] {("DT",true),("EW",true),("ShB",false)}));
    }
    [Fact]
    public void LoadedFinishedLegsDoNotOpenButRevivedLegsCanFinishAgain()
    {
        var tracker = new TrainExpansionProgress();
        Assert.Empty(tracker.Update(new[] {("DT",true),("EW",false)}));
        Assert.Empty(tracker.Update(new[] {("DT",false),("EW",false)}));
        Assert.Equal(new[] {"EW"},tracker.Update(new[] {("DT",true),("EW",false)}));
    }
    [Fact]
    public void RemainingRallyStopPreventsHandoverUntilItIsCompleted()
    {
        var tracker = new TrainExpansionProgress();
        tracker.Update(new[] {("DT",false),("DT",false),("EW",false)});
        Assert.Empty(tracker.Update(new[] {("DT",true),("DT",false),("EW",false)}));
        Assert.Equal(new[] {"EW"},tracker.Update(new[] {("DT",true),("DT",true),("EW",false)}));
    }
    [Fact]
    public void ResetOrDisablingDoesNotReplayOldTransitions()
    {
        var tracker = new TrainExpansionProgress();
        tracker.Update(new[] {("DT",false),("EW",false)});
        tracker.Reset();
        Assert.Empty(tracker.Update(new[] {("DT",true),("EW",false)}));
        tracker.Update(Array.Empty<(string,bool)>());
        Assert.Empty(tracker.Update(new[] {("DT",true),("EW",false)}));
    }
    [Fact]
    public void RemovedLegCanBeScoutedAndFinishedAgain()
    {
        var tracker = new TrainExpansionProgress();
        tracker.Update(new[] {("DT",true),("EW",false)});
        tracker.Update(new[] {("EW",false)});
        tracker.Update(new[] {("DT",false),("EW",false)});
        Assert.Equal(new[] {"EW"},tracker.Update(new[] {("DT",true),("EW",false)}));
    }
}
