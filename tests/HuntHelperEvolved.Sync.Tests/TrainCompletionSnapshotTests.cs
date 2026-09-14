using System.Numerics;
using Xunit;

namespace HuntHelperEvolved.Tests;

public class TrainCompletionSnapshotTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
    private static DetectedMark Mark(bool dead = false) => new()
    {
        NameId = 13361, WorldId = 37, Name = "Queen Hawk", Dead = dead,
        FirstSeenUtc = Now.AddHours(-1), LastSeenUtc = Now.AddSeconds(-10),
        DeathObservedAtUtc = dead ? Now : null
    };
    private static string Snapshot(DetectedMark mark, long generation = 1) =>
        TrainCompletionSnapshot.Create(generation, new[] { mark }, new[] { new TrackedMark
        {
            ModelId = mark.NameId, WorldId = mark.WorldId, Dead = mark.Dead,
            DeathObservedAtUtc = mark.DeathObservedAtUtc, SnipedAtUtc = mark.SnipedAtUtc,
            LastSeenUtc = mark.LastSeenUtc
        } }, Array.Empty<FlagEntry>(), true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoutineSightingRefreshDoesNotInvalidateAReport(bool dead)
    {
        var mark = Mark(dead);
        var before = Snapshot(mark);
        mark.LastSeenUtc = Now.AddSeconds(-9);
        mark.MapPosition = new Vector2(20, 21);
        Assert.Equal(before, Snapshot(mark));
    }

    [Theory]
    [InlineData("revived")]
    [InlineData("death time")]
    [InlineData("sniped")]
    [InlineData("world")]
    [InlineData("replacement")]
    [InlineData("seen alive after death")]
    public void ChangesToTheReportedKillOrTrainStillPreventClearing(string change)
    {
        var mark = Mark(true);
        var before = Snapshot(mark);
        if (change == "revived") mark.Dead = false;
        if (change == "death time") mark.DeathObservedAtUtc = Now.AddSeconds(1);
        if (change == "sniped") mark.SnipedAtUtc = Now;
        if (change == "world") mark.WorldId = 62;
        if (change == "seen alive after death") mark.LastSeenUtc = Now.AddSeconds(1);
        Assert.NotEqual(before, Snapshot(mark, change == "replacement" ? 2 : 1));
    }

    [Fact]
    public void SnipedBoundsAndCustomStopPositionsRemainProtected()
    {
        var mark = Mark(true); mark.SnipedAtUtc = Now;
        var before = Snapshot(mark);
        mark.LastSeenUtc = Now.AddSeconds(-5);
        Assert.NotEqual(before, Snapshot(mark));
        mark = Mark(); mark.IsCustom = true;
        before = Snapshot(mark);
        mark.MapPosition = new Vector2(10, 20);
        Assert.NotEqual(before, Snapshot(mark));
    }
}
