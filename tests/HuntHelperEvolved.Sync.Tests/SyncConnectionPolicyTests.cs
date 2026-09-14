using HuntHelperEvolved.Sync;
using Xunit;
public class SyncConnectionPolicyTests
{
    [Fact]
    public void BothPlaintextToggleTransitionsReevaluateTheRunningConnection()
    {
        var policy = new SyncConnectionPolicy();
        var settings = new SyncConnectionPolicy.Settings(true, "ws://localhost:8080", "secret", "scout", true, false);
        var events = new List<string>();
        string error = "";
        Uri? connected = null;
        void Apply() => policy.Apply(settings, false,
            () => { events.Add("stop"); connected = null; }, e => error = e,
            uri => { events.Add("start"); connected = uri; });
        Apply();
        Assert.Null(connected); Assert.NotEmpty(error);
        settings = settings with { AllowPlaintext = true };
        Apply();
        Assert.Equal("ws", connected!.Scheme); Assert.Empty(error);
        Apply();
        Assert.Equal(new[] { "stop", "stop", "start" }, events);
        settings = settings with { AllowPlaintext = false };
        Apply();
        Assert.Null(connected); Assert.NotEmpty(error);
        Assert.Equal("stop", events.Last());
    }
}
