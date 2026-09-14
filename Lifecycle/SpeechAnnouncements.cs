using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HuntHelperEvolved;

internal interface ISpeechVoice : IDisposable
{
    event Action Completed;
    void Start(string message);
    void Cancel();
}

/// <summary>Called on the framework thread; completion callbacks only enqueue work.</summary>
internal sealed class SpeechAnnouncements : IDisposable
{
    internal const int MaximumVoices = 8;
    private readonly Func<ISpeechVoice> create;
    private readonly Action<Exception> report;
    private readonly Dictionary<ISpeechVoice, Action> voices = new();
    private readonly ConcurrentQueue<ISpeechVoice> completed = new();
    private bool disposed;

    public SpeechAnnouncements(Func<ISpeechVoice> create, Action<Exception> report)
    { this.create = create; this.report = report; }

    public bool Start(string message)
    {
        if (disposed) return false;
        Drain();
        if (voices.Count >= MaximumVoices) return false;
        var voice = create();
        Action handler = () => completed.Enqueue(voice);
        voices.Add(voice, handler);
        try
        {
            voice.Completed += handler;
            voice.Start(message);
            return true;
        }
        catch (Exception ex)
        {
            try { Release(voice, true); }
            catch (Exception cleanup) { throw new AggregateException(ex, cleanup); }
            throw;
        }
    }

    public void Drain()
    {
        while (completed.TryDequeue(out var voice))
        {
            try { Release(voice, false); }
            catch (Exception ex)
            {
                try { report(ex); }
                catch { /* Never throw from completion processing because logging failed. */ }
            }
        }
    }

    private void Release(ISpeechVoice voice, bool cancel)
    {
        if (!voices.Remove(voice, out var handler)) return;
        var cleanup = new CleanupSequence();
        cleanup.Run("speech completion handler", () => voice.Completed -= handler);
        if (cancel) cleanup.Run("cancel speech", voice.Cancel);
        cleanup.Run("dispose speech", voice.Dispose);
        cleanup.ThrowIfFailed();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var cleanup = new CleanupSequence();
        foreach (var voice in new List<ISpeechVoice>(voices.Keys))
            cleanup.Run("speech voice", () => Release(voice, true));
        completed.Clear();
        cleanup.ThrowIfFailed();
    }
}
