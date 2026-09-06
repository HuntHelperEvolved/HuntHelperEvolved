using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class ARankInstanceTests
{
    [Fact]
    public void ScoutingSecondInstanceCreatesSeparateRowsBeforeAnyKill()
    {
        Assert.Equal(new uint[]{1,2}, ARankInstances.Resolve(new uint[]{2}, Array.Empty<uint>()));
        Assert.Equal(new uint[]{1,2,3}, ARankInstances.Resolve(new uint[]{3,1,3}, new uint[]{2}));
    }
    [Fact]
    public void UnknownInstanceKillsAreNeverRelabelledAndNoEvidenceDoesNotInventInstances()
    {
        Assert.Equal(new uint[]{0,1,2}, ARankInstances.Resolve(new uint[]{2}, new uint[]{0}));
        Assert.Equal(new uint[]{0}, ARankInstances.Resolve(Array.Empty<uint>(), Array.Empty<uint>()));
        Assert.Equal(new uint[]{0}, ARankInstances.Resolve(new uint[]{99}, Array.Empty<uint>()));
    }
}
