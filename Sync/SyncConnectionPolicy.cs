using System;
namespace HuntHelperEvolved.Sync;

// Owns the coordinator's settings equality and stop/validate/start sequence.
public sealed class SyncConnectionPolicy
{
    public sealed record Settings(bool Enabled, string Url, string Password, string Alias,
        bool ShareTrain, bool AllowPlaintext);
    private Settings? _applied;
    public void Apply(Settings wanted, bool force, Action stopAndForget,
        Action<string> setError, Action<Uri> start)
    {
        if (!force && wanted == _applied) return;
        _applied = wanted;
        stopAndForget();
        setError(string.Empty);
        if (!wanted.Enabled) return;
        if (!SyncEndpoint.TryBuild(wanted.Url, out var uri, out var error, wanted.AllowPlaintext))
        {
            setError(error);
            return;
        }
        start(uri);
    }
}
