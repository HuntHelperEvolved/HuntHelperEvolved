using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public class SpeechAnnouncementsTests
{
    private sealed class Voice : ISpeechVoice
    {
        public event Action? Completed;
        public bool CompleteImmediately, FailStart, FailCancel, FailDispose;
        public int Disposals, Cancels;
        public Action? CapturedCallback => Completed;
        public void Start(string message) { if (CompleteImmediately) Completed?.Invoke(); if (FailStart) throw new Exception("start"); }
        public void Finish() => Completed?.Invoke();
        public void Cancel() { Cancels++; if (FailCancel) throw new Exception("cancel"); }
        public void Dispose() { Disposals++; if (FailDispose) throw new Exception("dispose"); }
    }
    [Fact]
    public void ImmediateCompletionIsCapturedAndDisposedOnlyWhenDrained()
    {
        var voice = new Voice { CompleteImmediately = true };
        using var speech = new SpeechAnnouncements(() => voice, _ => { });
        Assert.True(speech.Start("mark"));
        Assert.Equal(0, voice.Disposals);
        speech.Drain(); speech.Drain();
        Assert.Equal(1, voice.Disposals);
        Assert.Null(voice.CapturedCallback);
    }
    [Fact]
    public void StartFailureReleasesVoiceAndRemovesHandler()
    {
        var voice = new Voice { FailStart = true };
        using var speech = new SpeechAnnouncements(() => voice, _ => { });
        Assert.Throws<Exception>(() => speech.Start("mark"));
        Assert.Equal(1, voice.Disposals);
        Assert.Null(voice.CapturedCallback);
    }
    [Fact]
    public void BurstIsBoundedAndShutdownReleasesEveryVoiceDespiteFailure()
    {
        var voices = new List<Voice>();
        var speech = new SpeechAnnouncements(() => { var v = new Voice(); voices.Add(v); return v; }, _ => { });
        for (var i = 0; i < SpeechAnnouncements.MaximumVoices; i++) Assert.True(speech.Start("mark"));
        Assert.False(speech.Start("overflow"));
        Assert.Equal(SpeechAnnouncements.MaximumVoices, voices.Count);
        voices[0].FailCancel = true;
        var lateCallback = voices[1].CapturedCallback!;
        Assert.Throws<AggregateException>(speech.Dispose);
        lateCallback(); speech.Drain(); speech.Dispose();
        Assert.All(voices, v => { Assert.Equal(1, v.Disposals); Assert.Equal(1, v.Cancels); });
        Assert.False(speech.Start("after unload"));
    }
    [Fact]
    public async Task BackgroundCompletionDoesNotDisposeOrThrowOnCallbackThread()
    {
        var voice = new Voice { FailDispose = true };
        using var speech = new SpeechAnnouncements(() => voice, _ => throw new Exception("log failed"));
        speech.Start("mark");
        await Task.Run(voice.Finish);
        Assert.Equal(0, voice.Disposals);
        speech.Drain();
        Assert.Equal(1, voice.Disposals);
    }
}
