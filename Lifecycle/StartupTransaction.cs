using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

/// <summary>Owns startup side effects until construction has fully succeeded.</summary>
internal sealed class StartupTransaction
{
    private readonly Stack<Action> undo = new();
    public void Add(Action rollback) => undo.Push(rollback);
    public void Commit() => undo.Clear();
    public Exception Rollback(Exception failure)
    {
        var errors = new List<Exception> { failure };
        while (undo.TryPop(out var action))
        {
            try { action(); }
            catch (Exception ex) { errors.Add(ex); }
        }
        return new StartupFailureException(errors);
    }
}

internal sealed class StartupFailureException : AggregateException
{
    public bool RollbackFailed { get; }
    public StartupFailureException(IReadOnlyCollection<Exception> errors)
        : base("HHE startup failed; rollback was attempted for all acquired resources.", errors)
    {
        RollbackFailed = errors.Count > 1 || errors.OfType<StartupFailureException>().Any(e => e.RollbackFailed);
    }
}
