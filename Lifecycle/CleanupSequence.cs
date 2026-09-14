using System;
using System.Collections.Generic;

namespace HuntHelperEvolved;

/// <summary>Attempts every cleanup action, reporting failures after all resources were visited.</summary>
internal sealed class CleanupSequence
{
    private readonly List<Exception> errors = new();
    public void Run(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { errors.Add(new InvalidOperationException($"Cleanup failed: {name}.", ex)); }
    }

    public void ThrowIfFailed()
    {
        if (errors.Count > 0) throw new AggregateException("HHE did not shut down cleanly. See the cleanup errors for affected components.", errors);
    }
}
