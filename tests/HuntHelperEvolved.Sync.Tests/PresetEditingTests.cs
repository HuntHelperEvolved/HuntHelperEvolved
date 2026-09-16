using HuntHelperEvolved.TrainPresets;
using HuntHelperEvolved.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

public class PresetEditingTests
{
    [Fact]
    public void RouteCatalogMatchesExistingMarkAndTerritoryDefinitions()
    {
        foreach (var zone in RouteCatalog.Zones)
        {
            Assert.Equal(zone.Name, SRankTimerData.All.Single(s => s.TerritoryId == zone.TerritoryId).Zone);
            foreach (var mark in zone.Marks)
            {
                var info = HuntHelperEvolved.ExpansionData.Lookup(mark.NameId)!;
                Assert.Equal(zone.Name, info.Location);
                Assert.Equal(zone.Expansion, info.Expansion);
                Assert.Equal(mark.Name, info.Name);
            }
        }
    }
    [Fact]
    public void ZoneAndExpansionMovesKeepExpansionBlocksTogether()
    {
        var preset = new TrainPreset { Name = "Route" };
        foreach (var territory in new uint[] { 1187, 960, 813, 818, 1188 }) PresetEditing.AddZone(preset, territory);
        Assert.Equal(new uint[] { 1187, 1188, 960, 813, 818 }, preset.Zones.Select(z => z.TerritoryId));
        PresetEditing.MoveExpansion(preset, "Shadowbringers", -1);
        PresetEditing.MoveZone(preset, 818, -1);
        PresetEditing.MoveZone(preset, 960, -1);
        Assert.Equal(new uint[] { 1187, 1188, 818, 813, 960 }, preset.Zones.Select(z => z.TerritoryId));
        Assert.Null(RouteCatalog.Validate(preset));
        PresetEditing.AddZone(preset, 813);
        Assert.Equal(5, preset.Zones.Count);
    }

    [Fact]
    public void LegacyWelcomeDoesNotEnablePresetsAndNewWireRoundTrips()
    {
        var old = SyncProtocol.Deserialize<WelcomeMessage>(JObject.Parse("{'protocol':4}"))!;
        Assert.False(old.SupportsTrainPresets);
        Assert.Empty(old.TrainPresets.Presets);
        var preset = new TrainPreset { Name = "Route" };
        PresetEditing.AddZone(preset, 960);
        preset.Zones[0].Strict = true;
        preset.Zones[0].MarkOrder = new() { 10633, 10634 };
        preset.RallyInInstancedZones = true;
        preset.RallyBeforeExpansions.Add("Endwalker");
        preset.Zones[0].EntryAetheryteId = RouteCatalog.ByTerritory[960].Aetherytes[0].Id;
        var message = new TrainPresetMessage { Action = "save", BaseRevision = 5, Preset = preset };
        var wire = JObject.Parse(SyncProtocol.Serialize(message));
        Assert.Equal("train.preset", wire.Value<string>("type"));
        var parsed = SyncProtocol.Deserialize<TrainPresetMessage>(wire)!;
        Assert.Equal(new uint[] { 10633, 10634 }, parsed.Preset!.Zones[0].MarkOrder);
        Assert.Equal(5, parsed.BaseRevision);
        Assert.True(parsed.Preset.RallyInInstancedZones);
        Assert.Equal(preset.RallyBeforeExpansions, parsed.Preset.RallyBeforeExpansions);
        Assert.Equal(preset.Zones[0].EntryAetheryteId, parsed.Preset.Zones[0].EntryAetheryteId);
        var state = new PresetState { Rallies = new() { Revision = 3,
            Completed = new() { new(new(80, 960, 1), "Endwalker") },
            Rows = new() { new(new(new(80, 960, 1), "Endwalker"), uint.MaxValue, true) } } };
        var broadcast = new TrainPresetsBroadcast { State = state };
        var received = SyncProtocol.Deserialize<TrainPresetsBroadcast>(JObject.Parse(SyncProtocol.Serialize(broadcast)))!;
        Assert.Equal(state.Rallies.Rows, received.State.Rallies.Rows);
        Assert.Equal(state.Rallies.Completed, received.State.Rallies.Completed);
    }

    [Fact]
    public void ManualPresetAdjustmentCarriesTheExactOrderAndPausedState()
    {
        var order = new List<SyncKey> { SyncKey.From((10634u, 1u, 80u)), SyncKey.From((10633u, 1u, 80u)) };
        var command = new TrainPresetMessage { Action = "reorder", BaseRevision = 5, Order = order };
        var received = SyncProtocol.Deserialize<TrainPresetMessage>(JObject.Parse(SyncProtocol.Serialize(command)))!;
        Assert.Equal("reorder", received.Action);
        Assert.Equal(order.Select(k => k.NameId), received.Order!.Select(k => k.NameId));
        var broadcast = new TrainPresetsBroadcast { State = new() { OrderingPaused = true } };
        Assert.True(SyncProtocol.Deserialize<TrainPresetsBroadcast>(JObject.Parse(SyncProtocol.Serialize(broadcast)))!.State.OrderingPaused);
    }
}
