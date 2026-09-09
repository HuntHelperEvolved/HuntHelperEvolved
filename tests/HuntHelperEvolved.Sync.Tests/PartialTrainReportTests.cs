using Xunit;

namespace HuntHelperEvolved.Tests;

/// <summary>
/// A report covers only the expansions the train actually killed something in,
/// and only those come off the train when it posts. These fix the boundaries of
/// "actually killed": a leg found already stripped does not count, and a leg
/// that was run keeps its unfinished marks in the report.
/// </summary>
public class PartialTrainReportTests
{
    private static readonly DateTime Now = new(2026,9,7,12,0,0,DateTimeKind.Utc);

    private const uint Shb = 8906;   // Nuckelavee
    private const uint Shb2 = 8907;  // Nariphon
    private const uint Ew = 10623;   // Storsie
    private const uint Dt = 13361;   // queen hawk

    private static TrackedMark Alive(uint modelId) => new()
    { Name=$"mark {modelId}", ModelId=modelId, WorldId=80, WorldName="World 80",
      Dead=false, LastSeenUtc=Now.AddHours(-1) };

    private static TrackedMark Killed(uint modelId) => new()
    { Name=$"mark {modelId}", ModelId=modelId, WorldId=80, WorldName="World 80",
      Dead=true, DeathObservedAtUtc=Now, LastSeenUtc=Now.AddHours(-1) };

    private static TrackedMark Sniped(uint modelId) => new()
    { Name=$"mark {modelId}", ModelId=modelId, WorldId=80, WorldName="World 80",
      Dead=true, SnipedAtUtc=Now, LastSeenUtc=Now.AddHours(-1) };

    private static TrackedMark DeadOnArrival(uint modelId) => new()
    { Name=$"mark {modelId}", ModelId=modelId, WorldId=80, WorldName="World 80",
      Dead=true, LastSeenUtc=Now.AddHours(-1) };

    [Fact]
    public void ScoutedButUnrunLegsAreLeftOutOfTheReport()
    {
        var marks = new List<TrackedMark> { Killed(Dt), Alive(Shb), Alive(Ew) };

        Assert.Equal(new[] {"Dawntrail"}, TrainReport.ReportedExpansions(marks));
        Assert.Equal(new[] {Dt}, TrainReport.ForReport(marks).Select(m => m.ModelId));
    }

    [Fact]
    public void ARunLegKeepsItsUnfinishedMarksInTheReport()
    {
        // The point of the filter is which LEGS go out, never which marks
        // within one: "we ran Shadowbringers and did not get Nariphon" is
        // exactly what the unfinished section is for.
        var marks = new List<TrackedMark> { Killed(Shb), Alive(Shb2) };

        Assert.Equal(new[] {Shb, Shb2}, TrainReport.ForReport(marks).Select(m => m.ModelId));
        Assert.Equal(new[] {Shb}, TrainReport.SubmittedMarks(marks).Select(m => m.ModelId));
    }

    [Fact]
    public void ALegFoundAlreadyStrippedIsNotAReportedLeg()
    {
        // Nobody on the train killed anything here, so the leg was not run.
        // Its marks stay on the train rather than being published and cleared.
        var marks = new List<TrackedMark> { Sniped(Shb), DeadOnArrival(Shb2), Killed(Dt) };

        Assert.Equal(new[] {"Dawntrail"}, TrainReport.ReportedExpansions(marks));
        Assert.DoesNotContain(TrainReport.ForReport(marks), m => m.ModelId == Shb);
        Assert.DoesNotContain(TrainReport.SubmittedMarks(marks), m => m.ModelId == Shb2);
    }

    [Fact]
    public void SnipedMarksOnALegTheTrainDidRunStillGoOutAndAreCleared()
    {
        var marks = new List<TrackedMark> { Killed(Shb), Sniped(Shb2) };

        Assert.Equal(new[] {Shb, Shb2}, TrainReport.SubmittedMarks(marks).Select(m => m.ModelId));
        Assert.Equal(2, TrainReport.BuildEntries(TrainReport.ForReport(marks)).Count);
    }

    [Fact]
    public void AssumedSnipedNamesOnlyTheLegsTheReportCovers()
    {
        var marks = new List<TrackedMark> { Killed(Dt), Alive(Shb) };
        var groups = TrainReport.BuildSniped(TrainReport.ForReport(marks));

        Assert.All(groups, g => Assert.Equal("Dawntrail", g.Expansion));
    }

    [Fact]
    public void ATrainWithNothingKilledReportsNothingAtAll()
    {
        var marks = new List<TrackedMark> { Alive(Shb), Sniped(Ew) };

        Assert.Empty(TrainReport.ReportedExpansions(marks));
        Assert.Empty(TrainReport.ForReport(marks));
        Assert.Empty(TrainReport.SubmittedMarks(marks));
    }

    [Fact]
    public void WatchesFollowTheLegTheirMarkIsOn()
    {
        // Run Dawntrail now, Shadowbringers and Endwalker later: the Dawntrail
        // watch is published and consumed, the other two stay up.
        var watches = new List<FlagEntry>
        {
            new() { Label = "Neyoozoteel" },
            new() { Label = "Ophioneus" },
            new() { Label = "Tyger" },
        };

        var (reported, kept) = TrainReport.SplitWatches(watches, new HashSet<string> { "Dawntrail" });

        Assert.Equal(new[] {"Neyoozoteel"}, reported.Select(w => w.Label));
        Assert.Equal(new[] {"Ophioneus","Tyger"}, kept.Select(w => w.Label));
    }

    [Fact]
    public void ASuffixedWatchLabelStillResolvesToItsExpansion()
    {
        var watches = new List<FlagEntry> { new() { Label = "Narrow-rift — Spawn 3 (13.3, 10.4)" } };

        Assert.Single(TrainReport.SplitWatches(watches, new HashSet<string> { "Endwalker" }).Reported);
        Assert.Single(TrainReport.SplitWatches(watches, new HashSet<string> { "Dawntrail" }).Kept);
    }

    [Fact]
    public void AWatchNamingNoKnownSRankIsReportedRatherThanStranded()
    {
        // Nothing could ever clear it otherwise.
        var watches = new List<FlagEntry> { new() { Label = "something else entirely" } };

        Assert.Single(TrainReport.SplitWatches(watches, new HashSet<string> { "Dawntrail" }).Reported);
    }

    [Fact]
    public void KerShroudDoesNotResolveAsKer()
    {
        Assert.Equal("Endwalker", SRankData.ExpansionOfWatch("Ker Shroud"));
        Assert.Equal("Endwalker", SRankData.ExpansionOfWatch("Ker"));
        Assert.Null(SRankData.ExpansionOfWatch(""));
    }

    [Fact]
    public void ReportedRowsAreForgottenSoTheNextReportCannotCarryThemAgain()
    {
        // History retains removed dead marks on purpose, so that "Remove Dead"
        // cannot lose kills. A posted report is the one removal that must not
        // retain: keeping these would publish the same kills twice.
        var history = new TrainReportHistory();
        history.Update(new[] { Killed(Dt), Alive(Shb) });

        var submitted = TrainReport.SubmittedMarks(history.Snapshot().Values.ToList());
        history.Forget(submitted.Select(m => m.Key));

        Assert.Equal(new[] {Shb}, history.Snapshot().Values.Select(m => m.ModelId));
        Assert.Empty(TrainReport.BuildEntries(history.Snapshot().Values.ToList()));
    }

    [Fact]
    public void ForgettingLeavesTheKeptLegAvailableToALaterReport()
    {
        var history = new TrainReportHistory();
        history.Update(new[] { Killed(Dt), Alive(Shb) });
        history.Forget(TrainReport.SubmittedMarks(history.Snapshot().Values.ToList()).Select(m => m.Key));

        // The later train runs the leg it kept.
        history.Update(new[] { Killed(Shb) });
        var second = history.Snapshot().Values.ToList();

        Assert.Equal(new[] {"Shadowbringers"}, TrainReport.ReportedExpansions(second));
        Assert.Equal("mark 8906", Assert.Single(TrainReport.BuildEntries(second)).Name);
    }
}
