using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class TrainWatchPlannerTests
{
    [Fact]
    public void MateusTrainDoesNotPermitCoeurlOrAnotherInstanceReminder()
    {
        var route=new[]{new DetectedMark{WorldId=37,TerritoryId=1189,Instance=1}};
        Assert.True(TrainWatchPlanner.BelongsToTrain(route,37,1189,1));
        Assert.False(TrainWatchPlanner.BelongsToTrain(route,74,1189,1));
        Assert.False(TrainWatchPlanner.BelongsToTrain(route,37,1189,2));
        Assert.False(TrainWatchPlanner.BelongsToTrain(route,37,961,1));
    }
    [Fact]
    public void TimerMustBeOpenAndNotAlreadyUp()
    {
        var now=DateTime.UtcNow; var timer=SRankTimerData.ForTerritory(1189)!;
        Assert.False(TrainWatchPlanner.Available(timer,null,now,false));
        Assert.False(TrainWatchPlanner.Available(timer,new(){KilledAt=now},now,false));
        var status=new SyncSRankStatus{KilledAt=now.AddHours(-timer.MinHours-1)};
        Assert.True(TrainWatchPlanner.Available(timer,status,now,false));
        Assert.False(TrainWatchPlanner.Available(timer,status,now,true));
    }
    [Fact]
    public void AutomaticWatchesRefreshCoordinatesAndPreserveManualOrCompletedChecks()
    {
        FlagEntry Auto() => new(){Label="Neyoozoteel",TerritoryId=1189,WorldId=37,Automatic=true};
        var watches=new List<FlagEntry>();
        Assert.True(TrainWatchPlanner.Reconcile(watches,new[]{Auto()}));
        Assert.False(TrainWatchPlanner.Reconcile(watches,new[]{Auto()}));
        var mapped=Auto(); mapped.HasLocation=true; mapped.X=20; mapped.Y=21;
        Assert.True(TrainWatchPlanner.Reconcile(watches,new[]{mapped}));
        Assert.Equal(20,Assert.Single(watches).X);
        Assert.True(TrainWatchPlanner.Reconcile(watches,new[]{Auto()}));
        Assert.False(Assert.Single(watches).HasLocation);
        Assert.True(TrainWatchPlanner.Reconcile(watches,Array.Empty<FlagEntry>()));
        var completed=Auto(); completed.SpawnStatus=SpawnStatus.NotSpawned; watches.Add(completed);
        Assert.False(TrainWatchPlanner.Reconcile(watches,Array.Empty<FlagEntry>())); Assert.Single(watches);
        watches.Clear(); watches.Add(new(){TerritoryId=1189,Label="Manual Neyoozoteel"});
        Assert.False(TrainWatchPlanner.Reconcile(watches,new[]{Auto()})); Assert.Single(watches);
    }
}
