using HuntHelperEvolved.TrainPresets;
using Xunit;

namespace TrainPresetTests;

public class RoutingTests
{
    private static TrainPreset Preset(params uint[] territories) => new()
    {
        Name = "Test route",
        Zones = territories.Select(t => new PresetZone
        {
            TerritoryId = t, MarkOrder = RouteCatalog.ByTerritory[t].Marks.Select(m => m.NameId).ToList(),
        }).ToList(),
    };

    private static List<RoutePoint> Order(TrainPreset preset, params RoutePoint[] points) => PresetRouter.Order(points, preset, p => p);

    [Fact]
    public void ConductorExpansionAndZoneOrdersOverrideScoutOrder()
    {
        var marks = new[] { new RoutePoint(10634, 80, 960, 0, 15, 20), new RoutePoint(8906, 80, 813, 0, 15, 20),
            new RoutePoint(8896, 80, 818, 0, 15, 20), new RoutePoint(13361, 80, 1187, 0, 15, 20) };
        Assert.Equal(new uint[] { 1187, 818, 813, 960 }, Order(Preset(1187, 818, 813, 960), marks).Select(m => m.TerritoryId));
        Assert.Equal(new uint[] { 1187, 960, 813, 818 }, Order(Preset(1187, 960, 813, 818), marks).Select(m => m.TerritoryId));
    }

    [Fact]
    public void AutomaticRouteChoosesNearestUsableEntranceThenVisitsBothMarks()
    {
        var preset = Preset(960);
        var arch = new RoutePoint(10634, 80, 960, 0, 10.5f, 26.8f);
        var fan = new RoutePoint(10633, 80, 960, 0, 15, 20);
        Assert.Equal(new[] { arch, fan }, Order(preset, fan, arch));
        Assert.Equal(new[] { arch, fan }, Order(preset, arch, fan));
    }

    [Fact]
    public void StrictUltimaThuleEndsWithArchEtaEvenWhenItIsNearest()
    {
        var preset = Preset(960);
        preset.Zones[0].Strict = true;
        preset.Zones[0].MarkOrder = new() { 10633, 10634 };
        var arch = new RoutePoint(10634, 80, 960, 0, 10.5f, 26.8f);
        var fan = new RoutePoint(10633, 80, 960, 0, 15, 20);
        Assert.Equal(new[] { fan, arch }, Order(preset, arch, fan));
    }

    [Fact]
    public void EveryWorldAndInstanceRetainsEveryMarkAndStrictFinalMark()
    {
        var preset = Preset(960);
        preset.Zones[0].Strict = true;
        preset.Zones[0].MarkOrder = new() { 10633, 10634 };
        var input = new[] { new RoutePoint(10634, 80, 960, 3, 1, 1), new RoutePoint(10633, 81, 960, 1, 1, 1),
            new RoutePoint(10633, 80, 960, 1, 1, 1), new RoutePoint(10633, 80, 960, 3, 1, 1),
            new RoutePoint(10634, 81, 960, 1, 1, 1), new RoutePoint(10634, 80, 960, 1, 1, 1) };
        var result = Order(preset, input);
        Assert.Equal(new uint[] { 80, 80, 80, 80, 81, 81 }, result.Select(m => m.WorldId));
        Assert.Equal(new uint[] { 1, 1, 3, 3, 1, 1 }, result.Select(m => m.Instance));
        Assert.Equal(input.Length, result.Distinct().Count());
        Assert.All(result.Chunk(2), pair => Assert.Equal(new uint[] { 10633, 10634 }, pair.Select(m => m.NameId)));
    }

    [Fact]
    public void OmittedZonesStayInTheirExpansionAndUnknownZonesAreRetained()
    {
        var preset = Preset(1187, 818, 960);
        var input = new[] { new RoutePoint(8906, 80, 813, 0, 1, 1), new RoutePoint(10634, 80, 960, 0, 1, 1),
            new RoutePoint(1, 80, 9999, 0, 1, 1), new RoutePoint(8896, 80, 818, 0, 1, 1),
            new RoutePoint(13361, 80, 1187, 0, 1, 1) };
        Assert.Equal(new uint[] { 1187, 818, 813, 960, 9999 }, Order(preset, input).Select(m => m.TerritoryId));
    }

    [Fact]
    public void DeadRowsKeepTheirEvidenceAndDoNotPullRemainingRouteTowardThem()
    {
        var dead = new RoutePoint(10634, 80, 960, 0, 10.5f, 26.8f, Dead: true);
        var alive = new RoutePoint(10633, 80, 960, 0, 15, 20);
        Assert.Equal(new[] { dead, alive }, Order(Preset(960), alive, dead));
    }

    [Fact]
    public void StrictMarkOrderWinsAcrossAFlagWhileTheFlagKeepsItsSlot()
    {
        var preset = Preset(960);
        preset.Zones[0].Strict = true;
        preset.Zones[0].MarkOrder = new() { 10633, 10634 };
        var marks = new[] { new RoutePoint(10634, 80, 960, 0, 10, 10),
            new RoutePoint(123, 80, 960, 0, 12, 12, IsCustom: true), new RoutePoint(10633, 80, 960, 0, 15, 20) };
        Assert.Equal(new[] { marks[2], marks[1], marks[0] }, Order(preset, marks));
        preset.Zones[0].Strict = false;
        Assert.Equal(marks, Order(preset, marks));
    }

    [Fact]
    public void MissingPositionOrAllEntrancesExcludedRetainsScoutOrder()
    {
        var preset = Preset(960);
        var marks = new[] { new RoutePoint(10633, 80, 960, 0, 15, 20), new RoutePoint(10634, 80, 960, 0, 0, 0) };
        Assert.Equal(marks, Order(preset, marks));
        preset.ExcludedAetheryteIds = RouteCatalog.ByTerritory[960].Aetherytes.Select(a => a.Id).ToList();
        marks[1] = marks[1] with { X = 10.5f, Y = 26.8f };
        Assert.Equal(marks, Order(preset, marks));
    }

    [Fact]
    public void EqualCostRoutesRemainStableAcrossRepeatedUpdates()
    {
        var marks = new[] { new RoutePoint(10634, 80, 960, 0, 20, 20), new RoutePoint(10633, 80, 960, 0, 20, 20) };
        var preset = Preset(960);
        for (var i = 0; i < 20; i++) Assert.Equal(marks, Order(preset, marks));
    }

    [Fact]
    public void ExclusionsChangeTheChosenEntranceForEveryoneUsingThePreset()
    {
        var preset = Preset(960);
        var arch = new RoutePoint(10634, 80, 960, 0, 10.5f, 26.8f);
        var fan = new RoutePoint(10633, 80, 960, 0, 31, 28);
        Assert.Equal(arch, Order(preset, fan, arch)[0]);
        preset.ExcludedAetheryteIds.Add(179);
        Assert.Equal(fan, Order(preset, fan, arch)[0]);
    }

    [Fact]
    public void ValidationRejectsCrossZoneMarkOrdersAndSplitExpansionBlocks()
    {
        var bad = Preset(960);
        bad.Zones[0].Strict = true;
        bad.Zones[0].MarkOrder = new() { 8906, 10634 };
        Assert.NotNull(RouteCatalog.Validate(bad));
        Assert.NotNull(RouteCatalog.Validate(Preset(1187, 960, 1188)));
        Assert.NotNull(RouteCatalog.Validate(Preset(960, 960)));
        bad = Preset(960);
        bad.Name = new string('x', 81);
        Assert.NotNull(RouteCatalog.Validate(bad));
        bad.Name = "Valid"; bad.Zones[0].MarkOrder = null!;
        Assert.NotNull(RouteCatalog.Validate(bad));
        Assert.Null(RouteCatalog.Validate(Preset(1187, 813, 960)));
    }

    [Fact]
    public void EditingCopiesCannotChangeAnAlreadySharedPreset()
    {
        var state = new PresetState { Presets = new() { Preset(960) }, Revision = 10 };
        var copy = state.Copy();
        copy.Presets[0].Zones[0].MarkOrder.Reverse();
        copy.Presets[0].ExcludedAetheryteIds.Add(179);
        Assert.NotEqual(copy.Presets[0].Zones[0].MarkOrder, state.Presets[0].Zones[0].MarkOrder);
        Assert.Empty(state.Presets[0].ExcludedAetheryteIds);
        Assert.Equal(10, state.Revision);
    }

    [Fact]
    public void CatalogContainsAllHuntZonesAndOnlyUsableEntrances()
    {
        Assert.Equal(47, RouteCatalog.Zones.Count);
        Assert.Equal(77, RouteCatalog.Zones.Sum(z => z.Marks.Count));
        Assert.All(RouteCatalog.Zones.SelectMany(z => z.Aetherytes), a => Assert.True(a.X > 0 && a.Y > 0));
    }
}
