using System.Text.Json;
using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class SpawnConditionTests
{
    public static IEnumerable<object[]> Forecasts()
    {
        using var stream=typeof(SpawnConditionTests).Assembly.GetManifestResourceStream("condition-reference.json")!;
        using var doc=JsonDocument.Parse(stream);
        foreach(var row in doc.RootElement.EnumerateArray())
            yield return new object[]{row.GetProperty("name").GetString()!,row.GetProperty("at").GetDateTime(),row.GetProperty("start").GetDateTime(),row.GetProperty("end").GetDateTime()};
    }
    [Theory]
    [MemberData(nameof(Forecasts))]
    public void MatchesFaloopForecastAcrossWeatherMoonTimeAndCombinedRules(string name,DateTime at,DateTime start,DateTime end)
    {
        var actual=SpawnConditionData.Next(name,at);
        Assert.NotNull(actual);
        Assert.True(Math.Abs((actual.Value.Start-start).TotalMilliseconds)<=50, $"{name}: expected {start:o}–{end:o}, actual {actual.Value.Start:o}–{actual.Value.End:o}");
        Assert.InRange(Math.Abs((actual.Value.End-end).TotalMilliseconds),0,50);
    }
    [Fact]
    public void EndIsExclusiveAndNextDayWindowDoesNotReuseExpiredTime()
    {
        var first=SpawnConditionData.Next("Bonnacon",new DateTime(2026,9,6,12,0,0,DateTimeKind.Utc))!.Value;
        Assert.Equal(first.End,SpawnConditionData.Next("Bonnacon",first.Start.AddSeconds(1))!.Value.End);
        Assert.True(SpawnConditionData.Next("Bonnacon",first.End.AddMilliseconds(1))!.Value.Start>=first.End);
    }
    [Fact]
    public void ManualTriggersHaveNoInventedScheduleAndAllTimedRanksHaveDescriptions()
    {
        Assert.Null(SpawnConditionData.Next("Aglaope",DateTime.UtcNow));
        foreach(var mark in SRankTimerData.All)
            Assert.DoesNotContain("not yet documented",SpawnConditionData.Description(mark.Name));
    }
}
