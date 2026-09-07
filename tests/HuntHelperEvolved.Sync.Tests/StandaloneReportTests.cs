using Newtonsoft.Json;
using Xunit;

namespace HuntHelperEvolved.Tests;

public class StandaloneReportTests
{
    private static readonly DateTime Now = new(2026,9,7,12,0,0,DateTimeKind.Utc);
    private static TrackedMark Mark(uint world, bool dead, DateTime? death = null) => new()
    { Name="Vogaal Ja", ModelId=2945, WorldId=world, WorldName=$"World {world}",
      Dead=dead, DeathObservedAtUtc=death, LastSeenUtc=Now.AddHours(-1) };

    [Fact]
    public void PartiallyCompletedTrainDoesNotInventKillsForLiveOrUnknownMarks()
    {
        var entries = TrainReport.BuildEntries(new() { Mark(80,true,Now), Mark(81,false), Mark(82,true) });
        var entry = Assert.Single(entries);
        Assert.Equal(80u,entry.WorldId);
        Assert.Equal(Now,entry.KillTimeUtc);
        Assert.Equal(Now.AddHours(3.5),entry.WindowOpensUtc);
    }
    [Fact]
    public void NoKillsAreProducedForAnAllLiveTrainEvenWithAnOldTimestamp()
    {
        Assert.Empty(TrainReport.BuildEntries(new() {Mark(80,false,Now.AddDays(-1))}));
    }
    [Fact]
    public void SameMarkOnTwoWorldsRemainsTwoReportEntries()
    {
        var entries=TrainReport.BuildEntries(new() {Mark(80,true,Now),Mark(81,true,Now.AddMinutes(3))});
        Assert.Equal(new uint[] {80,81},entries.Select(e=>e.WorldId));
        Assert.NotEqual(entries[0].DisplayName,entries[1].DisplayName);
        var restored=JsonConvert.DeserializeObject<List<TrackedMark>>(JsonConvert.SerializeObject(new[] {Mark(80,true,Now),Mark(81,true,Now)}))!;
        Assert.Equal(2,restored.Select(m=>m.Key).Distinct().Count());
    }
    [Fact]
    public void SnipedReportRetainsBoundsAndIsSeparateFromAnExactKill()
    {
        var mark=Mark(80,true);mark.SnipedAtUtc=Now;
        var entry=Assert.Single(TrainReport.BuildEntries(new() {mark}));
        Assert.True(entry.Sniped);
        Assert.Equal(Now.AddHours(2.5),entry.WindowOpensUtc);
        Assert.Equal(Now.AddHours(4.5),entry.WindowCapsUtc);
    }
    [Fact]
    public void NativeIpcRoundTripPreservesWorldAndUnknownDeathTime()
    {
        var record=new NativeTrainRecord("Vogaal Ja",2945,134,19,2,80,"Cerberus",new(20,21),true,Now,null,null);
        Assert.Equal(record,JsonConvert.DeserializeObject<NativeTrainRecord>(JsonConvert.SerializeObject(record)));
    }
    [Fact]
    public void FirstSeenCorpseOrUninitializedHpDoesNotProveADeath()
    {
        var evidence=new MarkDeathEvidence();
        Assert.False(evidence.Observe(1,0,0));
        Assert.False(evidence.Observe(1,0,100));
        Assert.False(evidence.Observe(1,0,100));
    }
    [Fact]
    public void DeathRequiresAliveEvidenceForTheSameLoadedObjectAndFiresOnce()
    {
        var evidence=new MarkDeathEvidence();
        Assert.False(evidence.Observe(1,100,100));
        Assert.False(evidence.Observe(2,0,100));
        Assert.True(evidence.Observe(1,0,100));
        Assert.False(evidence.Observe(1,0,100));
        evidence.Observe(1,100,100);
        evidence.Retain(new HashSet<ulong>());
        Assert.False(evidence.Observe(1,0,100));
        evidence.Observe(1,100,100);evidence.Clear();
        Assert.False(evidence.Observe(1,0,100));
    }
    [Fact]
    public async Task DelayedCompletionCannotClearNewerEditsAndRejectsDuplicateSubmissions()
    {
        var guard=new TrainCompletionGuard();
        var response=new TaskCompletionSource<bool>();
        Assert.True(guard.TryBegin("generation1 revision1"));
        Assert.False(guard.TryBegin("generation1 revision1"));
        async Task<bool> Complete() { await response.Task; return guard.CanClear("generation1 revision2",false); }
        var pending=Complete();response.SetResult(true);
        Assert.False(await pending);
        guard.Finish();Assert.False(guard.IsBusy);
        Assert.True(guard.TryBegin("generation1 revision2"));
        Assert.True(guard.CanClear("generation1 revision2",false));
    }
    [Fact]
    public void SharedTrainsAndReplacementSessionsAreNeverClearedByAnOldReport()
    {
        var guard=new TrainCompletionGuard();guard.TryBegin("train1");
        Assert.False(guard.CanClear("train1",true));
        Assert.False(guard.CanClear("train2",false));
        guard.Finish();Assert.False(guard.CanClear("train1",false));
    }
    [Fact]
    public void NativeExchangePreservesExactAndUnknownTimesAcrossWorlds()
    {
        var marks = new[] {
            new DetectedMark {NameId=2945, WorldId=80, WorldName="Cerberus", Dead=true, LastSeenUtc=Now.AddMinutes(-2), DeathObservedAtUtc=Now},
            new DetectedMark {NameId=2945, WorldId=81, WorldName="Other", Dead=true, LastSeenUtc=Now, DeathObservedAtUtc=null},
            new DetectedMark {NameId=2946, WorldId=80, Dead=true, LastSeenUtc=Now.AddHours(-1), SnipedAtUtc=Now}
        };
        var result=TrainExchange.Import(TrainExchange.Export(marks))!;
        Assert.Equal(3,result.Select(m=>m.Key).Distinct().Count());
        Assert.Equal(Now,result[0].DeathObservedAtUtc);
        Assert.Null(result[1].DeathObservedAtUtc);
        Assert.Null(result[2].DeathObservedAtUtc);
        Assert.Equal(Now,result[2].SnipedAtUtc);
    }
    [Fact]
    public void HistoryRetainsRemovedDeathsAcrossWorldsAndPersistsWithoutLiveRows()
    {
        var history=new TrainReportHistory();
        history.Update(new[] {Mark(80,true,Now),Mark(81,true,Now),Mark(82,false)});
        history.Update(Array.Empty<TrackedMark>());
        Assert.Equal(2,history.Snapshot().Count);
        var snapshot=history.Snapshot(); snapshot.Values.First().WorldName="changed copy";
        Assert.DoesNotContain(history.Snapshot().Values,m=>m.WorldName=="changed copy");
        var saved=JsonConvert.DeserializeObject<List<TrackedMark>>(JsonConvert.SerializeObject(history.Snapshot().Values))!;
        history.Clear();Assert.Empty(history.Snapshot());
        history.Restore(saved);
        Assert.Equal(2,TrainReport.BuildEntries(history.Snapshot().Values.ToList()).Count);
    }
    [Fact]
    public void LegacyCodeWithOnlyLastSeenDoesNotInventDeathEvidence()
    {
        var json=JsonConvert.SerializeObject(new[] { new { Name="Vogaal Ja", MobID=2945, Dead=true, LastSeenUTC=Now } });
        using var output=new System.IO.MemoryStream();
        using (var gzip=new System.IO.Compression.GZipStream(output,System.IO.Compression.CompressionMode.Compress,true))
            gzip.Write(System.Text.Encoding.UTF8.GetBytes(json));
        var mark=Assert.Single(TrainExchange.Import(Convert.ToBase64String(output.ToArray()))!);
        Assert.True(mark.Dead);
        Assert.Null(mark.DeathObservedAtUtc);
        Assert.Equal(Now,mark.LastSeenUtc);
    }
}
