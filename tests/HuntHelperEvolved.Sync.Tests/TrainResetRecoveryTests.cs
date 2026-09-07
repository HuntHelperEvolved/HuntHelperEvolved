using HuntHelperEvolved.Sync;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class TrainResetRecoveryTests
{
    private sealed record Row(uint Mark,uint World,uint Instance,int Revision);
    private static List<Row> Recover(IEnumerable<Row> saved,IEnumerable<Row> current) => TrainResetRecovery.Merge(saved,current,
        r=>(r.Mark,r.World,r.Instance),r=>DateTime.UnixEpoch.AddSeconds(r.Revision));
    [Fact]
    public void RestoresOrderWithoutLosingNewEditsOrOtherWorlds()
    {
        Row[] saved={new(2,37,0,1),new(1,37,0,1)};
        Row[] current={new(1,37,0,2),new(2,37,0,0),new(1,74,0,3),new(1,37,1,3)};
        var restored=Recover(saved,current);
        Assert.Equal(new[]{saved[0],current[0],current[2],current[3]},restored);
        Assert.Equal(1,saved[1].Revision);
    }
    [Fact]
    public void EmptyPostResetTrainRestoresEverySavedMark()
    {
        Row[] saved={new(1,37,0,1),new(2,37,0,1)};
        var restored=Recover(saved,Array.Empty<Row>());
        Assert.Equal(saved,restored);restored.Clear();Assert.Equal(2,saved.Length);
    }
}
