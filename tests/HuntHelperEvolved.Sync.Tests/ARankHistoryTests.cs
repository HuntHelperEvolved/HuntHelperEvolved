using HuntHelperEvolved.Sync;
using Newtonsoft.Json;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;
public class ARankHistoryTests
{
    [Fact]
    public void WelcomeHistoryPopulatesEmptyBoardWithoutTrainRows()
    {
        var at = DateTime.UtcNow.AddHours(-1);
        var welcome = JsonConvert.DeserializeObject<WelcomeMessage>(JsonConvert.SerializeObject(new { arankKills = new[] {
            new { nameId = 2945, worldId = 80, instance = 1, at, uncertain = false },
            new { nameId = 2945, worldId = 80, instance = 2, at = at.AddMinutes(5), uncertain = true }
        }}))!;
        var history = new List<ARankKill>();
        Assert.Empty(welcome.Marks);
        Assert.True(ARankHistory.Merge(history, welcome.ARankKills, DateTime.UtcNow));
        Assert.Equal(at, history.Single(k => k.Instance == 1).At);
        Assert.True(history.Single(k => k.Instance == 2).Uncertain);
        Assert.False(ARankHistory.Merge(history, welcome.ARankKills, DateTime.UtcNow));
    }
    [Fact]
    public void OlderSnapshotsDoNotOverwriteNewerLocalEvidenceAndExactWinsTies()
    {
        var now = DateTime.UtcNow;
        var history = new List<ARankKill> { new() { NameId=2945, WorldId=80, At=now, Uncertain=true } };
        Assert.False(ARankHistory.Merge(history, new[] { new ARankKill { NameId=2945, WorldId=80, At=now.AddHours(-1) } }, now));
        Assert.True(ARankHistory.Merge(history, new[] { new ARankKill { NameId=2945, WorldId=80, At=now } }, now));
        Assert.False(history.Single().Uncertain);
        Assert.Empty(JsonConvert.DeserializeObject<WelcomeMessage>("{}")!.ARankKills);
    }
}
