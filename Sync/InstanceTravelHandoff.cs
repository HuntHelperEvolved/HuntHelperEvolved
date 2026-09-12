using System;
namespace HuntHelperEvolved.Sync;

internal enum InstanceTravelAction { Wait, Change, Complete, Unavailable, TimedOut }

/// <summary>Wait for arrival before issuing exactly one instance request.</summary>
internal sealed class InstanceTravelHandoff(uint world, uint territory, uint instance, DateTime started)
{
    private DateTime? _readySince;
    private bool _requested;
    public InstanceTravelAction Step(DateTime now, uint currentWorld, uint currentTerritory,
        uint currentInstance, int instanceCount, bool canChange, bool busy, bool ready)
    {
        if (now >= started.AddMinutes(2)) return InstanceTravelAction.TimedOut;
        if (!ready || busy || currentWorld != world || currentTerritory != territory)
        { _readySince = null; return InstanceTravelAction.Wait; }
        _readySince ??= now;
        if (now < _readySince.Value.AddSeconds(2)) return InstanceTravelAction.Wait;
        if (currentInstance == instance) return InstanceTravelAction.Complete;
        if (instanceCount > 0 && instance > instanceCount) return InstanceTravelAction.Unavailable;
        if (_requested || !canChange || instanceCount == 0) return InstanceTravelAction.Wait;
        _requested = true;
        return InstanceTravelAction.Change;
    }
}
