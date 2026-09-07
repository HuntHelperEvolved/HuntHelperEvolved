using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class StagedWindowTests
{
    [Fact]
    public void SnipedBoardMatchesDiscordRangeAndDoesNotInventMissingLowerBound()
    {
        var alive=DateTime.UtcNow.AddHours(-2); var missing=alive.AddMinutes(45);
        var report=Assert.Single(TrainReport.BuildEntries(new(){new(){ModelId=8906,Name="Nuckelavee",WorldId=37,Dead=true,LastSeenUtc=alive,SnipedAtUtc=missing}}));
        var kill=new ARankKill{ NameId=8906,WorldId=37,At=missing,LastAliveAt=alive,Uncertain=true };
        Assert.Equal((report.WindowOpensUtc,report.WindowCapsUtc),ARankHistory.Window(kill,4,6,null));
        kill.LastAliveAt=null;Assert.Null(ARankHistory.Window(kill,4,6,null).Opens);
        kill.LastAliveAt=alive;Assert.Null(ARankHistory.Window(kill,4,6,alive.AddMinutes(1)).Opens);
    }
    [Fact]
    public void SRankColoursRespectRespawnAndConditionBoundaries()
    {
        var now=DateTime.UtcNow; var open=new ConditionWindow(now,now.AddMinutes(1));
        Assert.Equal(SRankNameState.NotReady,SRankBoardFilter.NameState(SRankPhase.Cooldown,true,open,now));
        Assert.Equal(SRankNameState.Ready,SRankBoardFilter.NameState(SRankPhase.Window,true,open,now));
        Assert.Equal(SRankNameState.ConditionsUnmet,SRankBoardFilter.NameState(SRankPhase.Forced,true,open,open.End));
        Assert.Equal(SRankNameState.Ready,SRankBoardFilter.NameState(SRankPhase.Forced,false,null,now));
        Assert.Equal(SRankNameState.NotReady,SRankBoardFilter.NameState(SRankPhase.Uncertain,false,null,now));
    }
}
