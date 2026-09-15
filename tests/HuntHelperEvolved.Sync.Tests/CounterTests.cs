using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class CounterTests
{
    [Theory]
    [InlineData("Cracked Ronkan Doll")]
    [InlineData("Cracked Ronkan Thorn")]
    [InlineData("Cracked Ronkan Vessel")]
    [InlineData("Asvattha")]
    [InlineData("Pisaca")]
    [InlineData("Vajralangula")]
    [InlineData("Thinker")]
    [InlineData("Wanderer")]
    [InlineData("Weeper")]
    [InlineData("Naked Yumemi")]
    [InlineData("Yumemi")]
    [InlineData("Leshy")]
    [InlineData("Diakka")]
    [InlineData("Allagan Chimera")]
    [InlineData("Lesser Hydra")]
    [InlineData("Meracydian Vouivre")]
    [InlineData("Earth Sprite")]
    public void ResetSerializationPreservesMobNamesAndEpochs(string mob)
    {
        var message = new CounterResetMessage
        {
            WorldId = 80, TerritoryId = 147, Instance = 1,
            Epochs = new() { [mob] = "AbC123" }
        };
        using var json = System.Text.Json.JsonDocument.Parse(SyncProtocol.Serialize(message));
        var payload = json.RootElement;
        Assert.Equal("counter.reset", payload.GetProperty("type").GetString());
        Assert.Equal(80u, payload.GetProperty("worldId").GetUInt32());
        var epoch = Assert.Single(payload.GetProperty("epochs").EnumerateObject());
        Assert.Equal(mob, epoch.Name);
        Assert.Equal("AbC123", epoch.Value.GetString());
    }

    [Fact] public void ReconnectRetainsUnacknowledgedCountsAndNeverDecrementsAcknowledgedCounts()
    {
        var entry = new CounterContribution { Count=5 };
        var row = new SharedCounter { Contributions = new() { ["me"]=3, ["friend"]=9 } };
        entry.Reconcile(row,"me"); Assert.Equal(5,entry.Count); Assert.Equal(12,row.Total);
        row.Contributions["me"]=7;
        entry.Reconcile(row,"me"); Assert.Equal(7,entry.Count);
    }
    [Fact] public void ResetDropsPendingOldAttemptWithoutTouchingOtherContributors()
    {
        var entry = new CounterContribution { Count=5 };
        var row = new SharedCounter { Epoch="new", Contributions=new() { ["friend"]=3 } };
        Assert.True(entry.Reconcile(row,"me")); Assert.Equal(0,entry.Count);
        Assert.Equal("new",entry.Counter.Epoch); Assert.Equal(3,row.Total);
        Assert.False(entry.Reconcile(row,"me"));
    }
    [Fact] public void PersistentContributionRoundTripsWithoutLosingEpochOrPendingCount()
    {
        var entry = new CounterContribution { Counter=new() { WorldId=80,TerritoryId=817,Instance=2,Mob="Cracked Ronkan Doll",Epoch="attempt" }, Count=17 };
        var copy=Newtonsoft.Json.JsonConvert.DeserializeObject<CounterContribution>(SyncProtocol.Serialize(entry))!;
        Assert.Equal(entry.Counter.Key,copy.Counter.Key); Assert.Equal("attempt",copy.Counter.Epoch); Assert.Equal(17,copy.Count);
    }
}
