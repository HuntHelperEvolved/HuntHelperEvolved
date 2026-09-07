namespace HuntHelperEvolved;

/// <summary>One report submission at a time. Shared trains are never implicitly cleared by an HTTP result.</summary>
internal sealed class TrainCompletionGuard
{
    private string? _submitted;
    public bool IsBusy => _submitted is not null;
    public bool TryBegin(string snapshot)
    {
        if (IsBusy) return false;
        _submitted = snapshot;
        return true;
    }
    public bool CanClear(string current, bool shared) => IsBusy && !shared && _submitted == current;
    public void Finish() => _submitted = null;
}
