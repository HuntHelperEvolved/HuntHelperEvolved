using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public sealed class TrainPresentationTests
{
    [Fact]
    public void NavigationMatchesStableRouteOrderWithTiesDeathsAndNoPointer()
    {
        var random = new Random(18);
        for (var n = 0; n < 200; n++)
        {
            var marks = Enumerable.Range(0, random.Next(1, 40)).Select(i => new DetectedMark
                { NameId = (uint)i, Order = random.Next(8), Dead = random.Next(3) == 0 }).ToList();
            var currentIndex = random.Next(marks.Count + 1);
            var current = currentIndex == marks.Count ? null : marks[currentIndex];
            var ordered = marks.OrderBy(m => m.Order).ToList();
            var expected = ordered.Skip(current is null ? 0 : ordered.IndexOf(current) + 1).FirstOrDefault(m => !m.Dead);
            Assert.Same(expected, TrainNavigation.Next(marks, current));
        }
    }

    [Fact]
    public void GroupingRefreshesForRouteAndPreferenceChangesButNotTelemetry()
    {
        var stamp = new TrainGroupingState();
        var marks = new List<DetectedMark> { new() { NameId = 1, WorldId = 80 } };
        var fallback = new List<string> { "Dawntrail" };
        var orders = new Dictionary<uint, List<string>>();
        bool Check(bool shared = false) => stamp.Changed(marks, true, shared, fallback, orders);
        Assert.True(Check()); Assert.False(Check());
        marks[0].LastSeenUtc = DateTime.UtcNow; marks[0].Dead = true; Assert.False(Check());
        marks[0].Order = 2; Assert.True(Check());
        marks[0].WorldId = 81; Assert.True(Check());
        marks[0].ZoneName = "custom"; Assert.True(Check());
        orders[81] = new() { "Endwalker" }; Assert.True(Check()); Assert.False(Check());
        orders[81][0] = "Dawntrail"; Assert.True(Check());
        Assert.True(Check(true)); fallback.Add("Endwalker"); Assert.False(Check(true));
        marks.Clear(); Assert.True(Check(true)); Assert.False(Check(true));
    }
}
