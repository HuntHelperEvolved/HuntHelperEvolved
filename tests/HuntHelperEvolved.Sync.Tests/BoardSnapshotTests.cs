using System.Diagnostics;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public sealed class BoardSnapshotTests
{
    [Fact]
    public void ReusesRowsUntilDeadlineAndRefreshesAtDeadline()
    {
        var cache = new BoardSnapshot<object>();
        var worlds = new uint[] { 80 };
        var expansions = new[] { "Dawntrail" };
        var calls = 0;
        object Build() { calls++; return new(); }
        var first = cache.Get(worlds, expansions, "", false, true, 0, Build);
        Assert.Same(first, cache.Get(worlds, expansions, "", false, true, Stopwatch.Frequency / 10 - 1, Build));
        Assert.NotSame(first, cache.Get(worlds, expansions, "", false, true, Stopwatch.Frequency / 10, Build));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void FiltersConnectionAndExplicitInvalidationRefreshImmediately()
    {
        var cache = new BoardSnapshot<object>();
        var worlds = new List<uint> { 80 };
        var expansions = new List<string> { "Dawntrail" };
        var search = ""; var available = false; var connected = true;
        object Read() => cache.Get(worlds, expansions, search, available, connected, 0, () => new());
        var last = Read();
        void Changed() { var next = Read(); Assert.NotSame(last, next); last = next; }
        worlds[0] = 81; Changed();
        expansions[0] = "Endwalker"; Changed();
        search = "mark"; Changed();
        available = true; Changed();
        connected = false; Changed();
        cache.Invalidate(); Changed();
    }
}
