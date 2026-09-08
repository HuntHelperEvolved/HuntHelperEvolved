using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class TimerTableSortTests
{
    [Fact]
    public void NumericColumnsDoNotSortTheirFormattedText()
    {
        Assert.Equal(new[]{2,10,100},TimerTableSort.Apply(new[]{100,2,10},n=>n,false));
        Assert.Equal(new[]{100,10,2},TimerTableSort.Apply(new[]{100,2,10},n=>n,true));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void UnknownDatesRemainLastAndEqualValuesKeepTheirOrder(bool descending)
    {
        var at=new DateTime(2026,9,8,23,0,0,DateTimeKind.Utc);
        var rows=new (string Name,DateTime? At)[]{("unknown",null),("first",at),("second",at),("tomorrow",at.AddHours(2))};
        var sorted=TimerTableSort.Apply(rows,r=>r.At,descending);
        Assert.Equal("unknown",sorted[^1].Name);
        Assert.True(sorted.FindIndex(r=>r.Name=="first")<sorted.FindIndex(r=>r.Name=="second"));
        Assert.Equal(descending ? "tomorrow" : "first",sorted[0].Name);
    }
    [Fact]
    public void NamesIgnoreCaseAndRetainStableWorldInstanceTies()
    {
        var rows=new[]{("Zeta",1),("alpha",2),("Alpha",3)};
        Assert.Equal(new[]{2,3,1},TimerTableSort.Apply(rows,r=>r.Item1,false).Select(r=>r.Item2));
    }
}
