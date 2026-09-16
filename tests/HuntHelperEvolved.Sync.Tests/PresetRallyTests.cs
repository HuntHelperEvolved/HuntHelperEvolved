using HuntHelperEvolved.TrainPresets;
using System.Text.Json;
using Xunit;

namespace TrainPresetTests;

public class RallyTests
{
    private sealed record Row(RoutePoint Point, string Label = "");
    private static Row Mark(uint zone, uint instance = 0, uint world = 80, int mark = 0) =>
        new(new(RouteCatalog.ByTerritory[zone].Marks[mark].NameId, world, zone, instance, 15, 20));
    private static TrainPreset Preset(params uint[] zones) => new()
    {
        Name = "Rally route", Zones = zones.Select(z => new PresetZone { TerritoryId = z }).ToList(),
    };
    private static RallyRoute<Row> Route(IEnumerable<Row> rows, TrainPreset? preset, RallyProgress progress) =>
        PresetRallies.Reconcile(rows.ToList(), preset, progress, r => r.Point,
            (stop, id, old) => new Row(new(id, stop.Key.WorldId, stop.Key.TerritoryId, stop.Key.Instance,
                stop.Aetheryte.X, stop.Aetheryte.Y, IsCustom: true), stop.Aetheryte.Name));

    [Fact]
    public void NoRalliesByDefaultAndNoFlagsBeforeAnyScouting()
    {
        var preset = Preset(1187, 813, 960);
        var input = new[] { Mark(1187, 1), Mark(813), Mark(960) };
        Assert.Equal(input, Route(input, preset, new()).Rows);
        preset.RallyInInstancedZones = true;
        preset.RallyBeforeExpansions.Add("Endwalker");
        Assert.Empty(Route(Array.Empty<Row>(), preset, new()).Rows);
    }

    [Fact]
    public void InstanceAndExpansionRalliesMergeAndInstancesAlwaysStartAtOne()
    {
        var preset = Preset(1187, 813, 960);
        preset.RallyInInstancedZones = true;
        preset.RallyBeforeExpansions = new() { "Shadowbringers", "Endwalker" };
        var route = Route(new[] { Mark(960, 2), Mark(813, 1), Mark(1187, 2), Mark(1187, 1), Mark(960, 1),
            Mark(1187, 1, mark: 1), Mark(1187, 2, mark: 1) }, preset, new()).Rows;
        Assert.Equal(new uint[] { 1187, 1187, 813, 960, 960 }, route.Where(r => r.Point.IsCustom).Select(r => r.Point.TerritoryId));
        Assert.Equal(new uint[] { 1, 2, 1, 1, 2 }, route.Where(r => r.Point.IsCustom).Select(r => r.Point.Instance));
        Assert.Equal(12, route.Count);
        Assert.True(route[0].Point.IsCustom);
        Assert.All(route.Where(r => r.Point.IsCustom), r => Assert.False(string.IsNullOrWhiteSpace(r.Label)));
    }

    [Theory]
    [InlineData("Shadowbringers", 813)]
    [InlineData("Endwalker", 960)]
    public void EachExpansionEntryCanBeEnabledIndependently(string expansion, uint territory)
    {
        var preset = Preset(1187, 813, 960);
        preset.RallyBeforeExpansions.Add(expansion);
        var rows = Route(new[] { Mark(1187, 1), Mark(813, 1), Mark(813, 2), Mark(960, 1) }, preset, new()).Rows;
        Assert.Equal(territory, Assert.Single(rows, r => r.Point.IsCustom).Point.TerritoryId);
    }

    [Fact]
    public void FirstExpansionAndUninstancedZonesDoNotGainInstanceRallies()
    {
        var preset = Preset(1187);
        preset.RallyBeforeExpansions.Add("Dawntrail");
        preset.RallyInInstancedZones = true;
        Assert.Single(Route(new[] { Mark(1187) }, preset, new()).Rows);
    }

    [Fact]
    public void RemovedFlagStaysGoneAfterMoreScoutingAndProgressReload()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var progress = new RallyProgress();
        var rows = Route(new[] { Mark(960, 1) }, preset, progress).Rows;
        Assert.True(rows[0].Point.IsCustom);
        rows.RemoveAt(0);
        rows.Add(Mark(960, 1, mark: 1));
        Assert.Equal(2, Route(rows, preset, progress).Rows.Count);
        Assert.Single(progress.Completed);
        progress = JsonSerializer.Deserialize<RallyProgress>(JsonSerializer.Serialize(progress))!;
        Assert.Equal(2, Route(rows, preset, progress).Rows.Count);
        Assert.Equal(3, Route(rows, preset, new()).Rows.Count);
    }

    [Fact]
    public void RepeatedPlanningKeepsFlagIdsAndManualFlags()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var manual = new Row(new(123, 80, 960, 1, 12, 12, IsCustom: true), "Wait here");
        var progress = new RallyProgress();
        var first = Route(new[] { Mark(960, 1), manual, Mark(960, 1, mark: 1) }, preset, progress);
        var next = Route(first.Rows, preset, progress);
        Assert.Equal(first.Rows, next.Rows);
        Assert.False(next.ProgressChanged);
        Assert.Contains(manual, next.Rows);
        Assert.Equal(2, next.Rows.Count(r => r.Point.IsCustom));
        Assert.Equal(new[] { Mark(960, 1), manual, Mark(960, 1, mark: 1) }, Route(next.Rows, null, progress).Rows);
    }

    [Fact]
    public void CheckedFlagCanBeUncheckedAndRemovingItDoesNotRecreateIt()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var progress = new RallyProgress();
        var rows = Route(new[] { Mark(960, 1) }, preset, progress).Rows;
        rows[0] = rows[0] with { Point = rows[0].Point with { Dead = true } };
        rows = Route(rows, preset, progress).Rows;
        Assert.True(rows[0].Point.Dead);
        Assert.Single(progress.Completed);
        rows[0] = rows[0] with { Point = rows[0].Point with { Dead = false } };
        rows = Route(rows, preset, progress).Rows;
        Assert.False(rows[0].Point.Dead);
        Assert.Empty(progress.Completed);
        rows.RemoveAt(0);
        Assert.Single(Route(rows, preset, progress).Rows);
    }

    [Fact]
    public void CompletedExpansionRallyDoesNotMoveToNextZoneAfterEarlierZoneIsCleared()
    {
        var preset = Preset(1187, 813, 818); preset.RallyBeforeExpansions.Add("Shadowbringers");
        var progress = new RallyProgress();
        var rows = Route(new[] { Mark(813), Mark(818) }, preset, progress).Rows;
        rows.RemoveAt(0);
        rows = Route(rows, preset, progress).Rows;
        Assert.Single(progress.Completed);
        rows.RemoveAll(r => r.Point.TerritoryId == 813);
        Assert.Single(Route(rows, preset, progress).Rows);
    }

    [Fact]
    public void WorldsHaveSeparateRalliesAndCompletion()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var progress = new RallyProgress();
        var rows = Route(new[] { Mark(960, 1), Mark(960, 1, 81) }, preset, progress).Rows;
        rows.RemoveAt(0);
        rows = Route(rows, preset, progress).Rows;
        Assert.Equal(81u, Assert.Single(rows, r => r.Point.IsCustom).Point.WorldId);
    }

    [Fact]
    public void FixedEntranceControlsRouteAndRallyAndExclusionsAreValidated()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var entry = RouteCatalog.ByTerritory[960].Aetherytes.Last();
        preset.Zones[0].EntryAetheryteId = entry.Id;
        var far = Mark(960, 1);
        var close = Mark(960, 1, mark: 1) with { Point = Mark(960, 1, mark: 1).Point with { X = entry.X, Y = entry.Y } };
        var rows = Route(new[] { far, close }, preset, new()).Rows;
        Assert.Equal(close, rows[1]);
        Assert.Equal(entry.X, rows[0].Point.X);
        Assert.Equal(entry.Y, rows[0].Point.Y);
        Assert.Null(RouteCatalog.Validate(preset));
        preset.ExcludedAetheryteIds.Add(entry.Id);
        Assert.NotNull(RouteCatalog.Validate(preset));
    }

    [Fact]
    public void MissingCoordinatesOrAllExcludedEntrancesDoNotInventARallyLocation()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var mark = Mark(960, 1) with { Point = Mark(960, 1).Point with { X = 0, Y = 0 } };
        Assert.Single(Route(new[] { mark }, preset, new()).Rows);
        preset.ExcludedAetheryteIds = RouteCatalog.ByTerritory[960].Aetherytes.Select(a => a.Id).ToList();
        Assert.Single(Route(new[] { Mark(960, 1) }, preset, new()).Rows);
    }

    [Fact]
    public void ScoutingADeadMarkDoesNotCompleteTheRallyForRemainingLiveMarks()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        var progress = new RallyProgress();
        var rows = Route(new[] { Mark(960, 1), Mark(960, 1, mark: 1) }, preset, progress).Rows;
        rows[1] = rows[1] with { Point = rows[1].Point with { Dead = true } };
        rows = Route(rows, preset, progress).Rows;
        Assert.False(Assert.Single(rows, r => r.Point.IsCustom).Point.Dead);
        Assert.Empty(progress.Completed);
        rows = rows.Select(r => r.Point.IsCustom ? r : r with { Point = r.Point with { Dead = true } }).ToList();
        Assert.All(Route(rows, preset, progress).Rows, r => Assert.False(r.Point.IsCustom));
    }

    [Fact]
    public void PresetCopiesAndStateCopiesDoNotShareRallySettingsOrProgress()
    {
        var preset = Preset(960); preset.RallyInInstancedZones = true;
        preset.RallyBeforeExpansions.Add("Endwalker");
        preset.Zones[0].EntryAetheryteId = RouteCatalog.ByTerritory[960].Aetherytes[0].Id;
        var copy = preset.Copy();
        copy.RallyBeforeExpansions.Clear(); copy.Zones[0].EntryAetheryteId = 0;
        Assert.True(copy.RallyInInstancedZones);
        Assert.Single(preset.RallyBeforeExpansions);
        Assert.NotEqual(0u, preset.Zones[0].EntryAetheryteId);
        var state = new PresetState();
        Route(new[] { Mark(960, 1) }, preset, state.Rallies);
        state.Copy().Rallies.Rows.Clear();
        Assert.Single(state.Rallies.Rows);
    }
}
