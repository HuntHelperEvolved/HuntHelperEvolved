using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using HuntHelperEvolved.Sync;

public class FaloopTimingReferenceTests
{
    // Numerical reference from https://faloop.app/main.c856b566072482c7.js,
    // checked 2026-09-10. Test offline; no login or polling needed.
    [Fact]
    public void AllTimedMarksMatchFaloopNormalAndMaintenanceRanges()
    {
        using var stream = typeof(FaloopTimingReferenceTests).Assembly.GetManifestResourceStream("faloop-timings.json")!;
        using var reference = JsonDocument.Parse(stream);
        Assert.Equal(47, reference.RootElement.EnumerateObject().Count());
        Assert.Equal(47, SRankTimerData.All.Count);
        foreach (var timer in SRankTimerData.All)
        {
            var slug = Regex.Replace(timer.Name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            var expected = reference.RootElement.GetProperty(slug);
            Assert.Equal(expected.GetProperty("normal").GetProperty("min").GetDouble(), timer.MinHours);
            Assert.Equal(expected.GetProperty("normal").GetProperty("cap").GetDouble(), timer.MaxHours);
            Assert.Equal(expected.GetProperty("maintenance").GetProperty("min").GetDouble(), timer.MaintMinHours);
            Assert.Equal(expected.GetProperty("maintenance").GetProperty("cap").GetDouble(), timer.MaintMaxHours);
        }
    }
}
