using System;
using System.Speech.Synthesis;
using Dalamud.Plugin.Services;

namespace HuntHelperEvolved;

internal sealed class SystemSpeechVoice : ISpeechVoice
{
    private readonly SpeechSynthesizer voice = new();
    public event Action? Completed;

    public SystemSpeechVoice(Configuration config, IPluginLog log)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(config.TtsVoiceName))
            {
                try { voice.SelectVoice(config.TtsVoiceName); }
                catch (Exception ex) { log.Warning(ex, "Configured speech voice unavailable; using the default."); }
            }
            voice.Volume = Math.Clamp(config.TtsVolume, 0, 100);
            voice.SpeakCompleted += OnCompleted;
        }
        catch (Exception ex)
        {
            try { voice.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(ex, cleanup); }
            throw;
        }
    }

    // No native cleanup or plugin service calls on the synthesis callback thread.
    private void OnCompleted(object? sender, SpeakCompletedEventArgs args) => Completed?.Invoke();
    public void Start(string message) => voice.SpeakAsync(message);
    public void Cancel() => voice.SpeakAsyncCancelAll();
    public void Dispose()
    {
        voice.SpeakCompleted -= OnCompleted;
        voice.Dispose();
    }
}
