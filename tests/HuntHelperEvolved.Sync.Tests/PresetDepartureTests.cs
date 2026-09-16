using HuntHelperEvolved.TrainPresets;
using Xunit;

namespace TrainPresetTests;

public class DepartureRoutingTests
{
    private static RouteAetheryte Entrance(uint id) => RouteCatalog.ByTerritory[958].Aetherytes.Single(a => a.Id == id);
    private static TrainPreset Preset() => new()
    {
        Name = "Garlemald", RallyInInstancedZones = true,
        Zones = new() { new() { TerritoryId = 958, MarkOrder = new() { 10627, 10628 } } },
    };
    private static RoutePoint Mark(uint name, float x, float y, uint instance = 1) => new(name, 80, 958, instance, x, y);
    private static List<RoutePoint> Order(TrainPreset preset, params RoutePoint[] marks) => PresetRouter.Order(marks, preset, p => p);

    [Theory]
    [InlineData(31.5f, 12.7f, 5.208646)]
    [InlineData(31.5f, 10f, 7.908646)]
    [InlineData(31.5f, 22.7f, 15.208646)]
    public void TertiumIncludesFlightToNorthernExitBeforeFlightToMark(float x, float y, double expected)
    {
        Assert.Equal(expected, RouteDistance.FromAetheryte(Entrance(173), x, y), 5);
    }

    [Fact]
    public void OtherEntrancesKeepDirectFlightEstimates()
    {
        Assert.Equal(5, RouteDistance.FromAetheryte(Entrance(172), 16.3f, 35), 5);
        Assert.All(RouteCatalog.Zones.SelectMany(z => z.Aetherytes).Where(a => a.Id != 173),
            a => Assert.Null(a.DepartureWaypoint));
    }

    [Theory]
    [InlineData(28f, 24f, 172u)]
    [InlineData(30f, 10f, 173u)]
    public void AutomaticEntryCanAvoidTertiumOrStillChooseIt(float x, float y, uint expected)
    {
        var stops = PresetRallies.Plan(new[] { Mark(10627, x, y) }, Preset(), Array.Empty<RallyVisit>());
        Assert.Equal(expected, Assert.Single(stops).Aetheryte.Id);
    }

    [Fact]
    public void DetourChangesAutomaticMarkOrderInEveryInstance()
    {
        var south = Mark(10627, 31.8f, 18.9f);
        var west = Mark(10628, 15, 24);
        var south2 = south with { Instance = 2 };
        var west2 = west with { Instance = 2 };
        var preset = Preset();
        var ordered = Order(preset, south2, west2, south, west);
        Assert.Equal(new[] { west, south, west2, south2 }, ordered);
        Assert.All(PresetRallies.Plan(ordered, preset, Array.Empty<RallyVisit>()),
            stop => Assert.Equal(172u, stop.Aetheryte.Id));
    }

    [Fact]
    public void FixedTertiumEntryOrdersFromTheExitButRalliesAtTheAetheryte()
    {
        var preset = Preset();
        preset.Zones[0].EntryAetheryteId = 173;
        var south = Mark(10627, 31.5f, 20);
        var north = Mark(10628, 31.5f, 10);
        var ordered = Order(preset, south, north);
        Assert.Equal(new[] { north, south }, ordered);
        var stop = Assert.Single(PresetRallies.Plan(ordered, preset, Array.Empty<RallyVisit>()));
        Assert.Equal(173u, stop.Aetheryte.Id);
        Assert.Equal(31.8f, stop.Aetheryte.X);
        Assert.Equal(17.9f, stop.Aetheryte.Y);
    }

    [Fact]
    public void StrictMarkOrderKeepsItsPreferenceWhileRallyUsesAdjustedDistance()
    {
        var preset = Preset();
        preset.Zones[0].Strict = true;
        var south = Mark(10627, 28, 24);
        var north = Mark(10628, 30, 10);
        var ordered = Order(preset, north, south);
        Assert.Equal(new[] { south, north }, ordered);
        Assert.Equal(172u, Assert.Single(PresetRallies.Plan(ordered, preset, Array.Empty<RallyVisit>())).Aetheryte.Id);
        preset.Zones[0].EntryAetheryteId = 173;
        Assert.Equal(173u, Assert.Single(PresetRallies.Plan(ordered, preset, Array.Empty<RallyVisit>())).Aetheryte.Id);
        preset.Zones[0].EntryAetheryteId = 0;
        preset.ExcludedAetheryteIds.Add(172);
        Assert.Equal(173u, Assert.Single(PresetRallies.Plan(ordered, preset, Array.Empty<RallyVisit>())).Aetheryte.Id);
    }

    [Theory]
    [InlineData(172u)]
    [InlineData(173u)]
    public void TravelEstimateStillChoosesTheRallysOwnAetheryte(uint id)
    {
        var rally = Entrance(id);
        var nearest = RouteCatalog.ByTerritory[958].Aetherytes
            .OrderBy(a => RouteDistance.FromAetheryte(a, rally.X, rally.Y)).First();
        Assert.Equal(id, nearest.Id);
    }
}
