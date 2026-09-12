using HuntHelperEvolved.Sync;
using Newtonsoft.Json.Linq;
using Xunit;
namespace HuntHelperEvolved.Tests;

public class MinionIdentityTests
{
    private static readonly DateTime Now = new(2026,9,12,12,0,0,DateTimeKind.Utc);
    private static VisibleMark Row(uint actor,float hp,float x=10) => new()
    {
        Mark=new() { NameId=8916,WorldId=80,Instance=1,TerritoryId=813,EntityId=actor,
            Rank="SS",HpPercent=hp,X=x,Y=10,SeenAt=Now }, ObserverIds=new(){"one"}
    };
    [Fact]
    public void ActiveRowsKeepIdenticalMinionsSeparateEvenAtSamePosition()
    {
        var rows=ActiveMarkRows.Merge(new[]{Row(101,0),Row(102,45)},Array.Empty<SyncSRankStatus>(),Now);
        Assert.Equal(2,rows.Count);
        Assert.Equal(0,rows.Single(r=>r.Mark.EntityId==101).Mark.HpPercent);
        Assert.Equal(45,rows.Single(r=>r.Mark.EntityId==102).Mark.HpPercent);
    }
    [Fact]
    public void GraceTracksMovementDeathAndObserverNamesPerActor()
    {
        var grace=new ActiveMarkGrace();
        grace.Update(Row(101,80),Now); grace.Update(Row(102,50),Now);
        var moved=Row(101,0,30); moved.Mark.SeenAt=Now.AddSeconds(1);
        grace.Update(moved,Now.AddSeconds(1));
        var rows=grace.Snapshot(Now.AddSeconds(1));
        Assert.Equal(2,rows.Count);
        Assert.Equal(30,rows.Single(r=>r.Mark.EntityId==101).Mark.X);
        Assert.Equal(50,rows.Single(r=>r.Mark.EntityId==102).Mark.HpPercent);
        Assert.Single(grace.Snapshot(Now.AddSeconds(5)));
        Assert.Empty(grace.Snapshot(Now.AddSeconds(6)));
    }
    [Fact]
    public void WireIdentitySurvivesSerializationAndLegacyFieldsRemainOptional()
    {
        var mark=Row(101,80).Mark;
        var copy=SyncProtocol.Deserialize<SyncSighting>(JObject.Parse(SyncProtocol.Serialize(mark)))!;
        Assert.Equal(mark.LiveKey,copy.LiveKey);
        var removed=SyncProtocol.Deserialize<SyncKey>(JObject.Parse(SyncProtocol.Serialize(mark)))!;
        Assert.Equal(mark.LiveKey,removed.ToLiveKey());
        var legacy=SyncProtocol.Deserialize<SyncSighting>(JObject.Parse("{\"nameId\":8916,\"worldId\":80,\"instance\":1}"))!;
        Assert.Equal(0u,legacy.EntityId); Assert.Equal(mark.Key,legacy.Key);
        Assert.NotEqual(mark.LiveKey,legacy.LiveKey);
    }
}
