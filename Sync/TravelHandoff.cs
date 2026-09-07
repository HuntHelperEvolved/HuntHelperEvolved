using System;
namespace HuntHelperEvolved.Sync;

internal sealed class TravelHandoff(uint world, DateTime started)
{
    private DateTime? _readySince, _firstRefusal;
    private DateTime _nextAttempt;
    public bool Expired(DateTime now) => now >= started.AddMinutes(15)
        || (_firstRefusal is { } refused && now >= refused.AddSeconds(30));
    public bool ShouldAttempt(DateTime now, uint currentWorld, bool busy, bool ready)
    {
        if (Expired(now)) return false;
        if (currentWorld != world || busy || !ready) { _readySince=null; return false; }
        _readySince ??= now;
        if (now < _readySince.Value.AddSeconds(2) || now < _nextAttempt) return false;
        _nextAttempt=now.AddSeconds(1);
        return true;
    }
    public void Refused(DateTime now) => _firstRefusal ??= now;
}
