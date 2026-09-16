using System.Text.Json;
using HuntHelperEvolved.TrainPresets;
using Xunit;

namespace TrainPresetTests;

public class PauseTests
{
    private sealed record Row(RoutePoint Point);
    private static TrainPreset Preset() => new()
    {
        Name = "Conductor", RallyInInstancedZones = true,
        Zones = new() { new() { TerritoryId = 960, Strict = true, MarkOrder = new() { 10633, 10634 } } },
    };
    private static Row Mark(uint id, uint instance = 1) => new(new(id, 80, 960, instance, 15, 20));
    private static List<Row> Route(List<Row> rows, TrainPreset preset, RallyProgress progress, bool paused = false) =>
        PresetRallies.Reconcile(rows, preset, progress, r => r.Point,
            (stop, id, _) => new Row(new(id, stop.Key.WorldId, stop.Key.TerritoryId, stop.Key.Instance,
                stop.Aetheryte.X, stop.Aetheryte.Y, IsCustom: true)), paused).Rows;

    [Fact]
    public void PausedRouteKeepsManualOrderFlagsAndLateScoutsUntilReselection()
    {
        var preset = Preset();
        var progress = new RallyProgress();
        var fan = Mark(10633); var arch = Mark(10634);
        var sorted = Route(new() { arch, fan }, preset, progress);
        var rally = sorted[0];
        var manual = new List<Row> { arch, rally, fan, Mark(10634, 2) };
        Assert.Equal(manual, Route(manual, preset, progress, paused: true));
        Assert.Equal(manual, Route(manual, preset, progress, paused: true));
        var resumed = Route(manual, preset, progress);
        Assert.Equal(new[] { rally, fan, arch }, resumed.Take(3));
        Assert.Equal(2, resumed.Count(r => r.Point.IsCustom));
    }

    [Fact]
    public void PausedRouteTracksRemovedFlagsAndNeverRecreatesCompletedRalliesOnResume()
    {
        var preset = Preset(); var progress = new RallyProgress();
        var rows = Route(new() { Mark(10634) }, preset, progress);
        rows.RemoveAt(0);
        rows = Route(rows, preset, progress, paused: true);
        Assert.Single(progress.Completed);
        Assert.Empty(progress.Rows);
        rows.Add(Mark(10633));
        Assert.All(Route(rows, preset, progress), r => Assert.False(r.Point.IsCustom));
    }

    [Fact]
    public void PausedRouteRemovesOrphanRalliesWithoutReorderingRemainingMarks()
    {
        var preset = Preset(); var progress = new RallyProgress();
        var rows = Route(new() { Mark(10634), Mark(10633, 2) }, preset, progress);
        rows.RemoveAll(r => !r.Point.IsCustom && r.Point.Instance == 1);
        var manual = new Row(new(123, 80, 960, 1, 1, 1, IsCustom: true));
        rows.Add(manual);
        var expected = rows.Where(r => r.Point.Instance != 1).Append(manual).ToList();
        Assert.Equal(expected, Route(rows, preset, progress, paused: true));
    }

    [Fact]
    public void PauseSurvivesSnapshotCopyAndSerializationWithoutChangingSavedPreset()
    {
        var preset = Preset();
        var state = new PresetState { Presets = new() { preset }, ActivePresetId = preset.Id, OrderingPaused = true };
        var loaded = JsonSerializer.Deserialize<PresetState>(JsonSerializer.Serialize(state.Copy()))!;
        Assert.True(loaded.OrderingPaused);
        Assert.Equal(preset.Id, loaded.ActivePresetId);
        Assert.Equal(new uint[] { 10633, 10634 }, loaded.Presets[0].Zones[0].MarkOrder);
        Assert.False(JsonSerializer.Deserialize<PresetState>("{}")!.OrderingPaused);
        Assert.Empty(Route(new(), preset, loaded.Rallies, loaded.OrderingPaused));
        var newTrain = new List<Row> { Mark(10634), Mark(10633) };
        Assert.Equal(newTrain, Route(newTrain, preset, loaded.Rallies, loaded.OrderingPaused));
    }
}
