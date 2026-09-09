using System;
using System.Linq;
using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Tests;
public class ReportedHistoryTests
{
    private static readonly DateTime Now=new(2026,9,9,12,0,0,DateTimeKind.Utc);
    [Fact]
    public void ReconnectForgetsOldDeathsButKeepsLaterCyclesAndUnrunLegs()
    {
        var history=new TrainReportHistory();
        history.Restore(new[]{
            new TrackedMark{ModelId=1,WorldId=37,Dead=true,LastSeenUtc=Now.AddMinutes(-2),DeathObservedAtUtc=Now.AddMinutes(-1)},
            new TrackedMark{ModelId=2,WorldId=37,Dead=false,LastSeenUtc=Now},
            new TrackedMark{ModelId=3,WorldId=37,Dead=true,LastSeenUtc=Now.AddHours(5),DeathObservedAtUtc=Now.AddHours(5)},
            new TrackedMark{ModelId=1,WorldId=38,Dead=true,LastSeenUtc=Now.AddMinutes(-2),DeathObservedAtUtc=Now.AddMinutes(-1)}
        });
        var payload=Newtonsoft.Json.Linq.JObject.Parse(SyncProtocol.Serialize(new WelcomeMessage{SupportsReportedHistory=true,
            ReportedMarks=new(){new(){NameId=1,WorldId=37,Through=Now},new(){NameId=3,WorldId=37,Through=Now}}}));
        var welcome=SyncProtocol.Deserialize<WelcomeMessage>(payload)!;
        var keys=ReportedHistory.CompletedKeys(history.Snapshot().Values,welcome.ReportedMarks);
        Assert.Equal((1u,0u,37u),Assert.Single(keys));history.Forget(keys);
        Assert.Equal(3,history.Snapshot().Count);
        Assert.Empty(ReportedHistory.CompletedKeys(history.Snapshot().Values,welcome.ReportedMarks));
    }
    [Fact]
    public void FiveExpansionTrainReportsThreeAndKeepsTwoUnrunLegs()
    {
        var ids=ExpansionData.ModelIdToMark.GroupBy(p=>p.Value.Expansion).Take(5).Select(g=>g.First().Key).ToArray();
        Assert.Equal(5,ids.Length);
        var marks=ids.Select((id,i)=>new TrackedMark{ModelId=id,WorldId=37,Dead=i<3,
            LastSeenUtc=Now.AddMinutes(-2),DeathObservedAtUtc=i<3?Now.AddMinutes(-1):null}).ToList();
        var submitted=TrainReport.SubmittedMarks(marks);
        Assert.Equal(3,submitted.Count);
        var history=new TrainReportHistory();history.Update(marks);history.Forget(submitted.Select(m=>m.Key));
        Assert.Equal(ids.Skip(3),history.Snapshot().Values.Select(m=>m.ModelId));
        Assert.Empty(TrainReport.ForReport(history.Snapshot().Values.ToList()));
    }
}
