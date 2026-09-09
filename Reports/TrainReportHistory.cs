using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

/// <summary>Native report history retains removed dead marks without merging different worlds.</summary>
internal sealed class TrainReportHistory
{
    private readonly Dictionary<(uint, uint, uint), TrackedMark> _marks = new();
    public void Update(IEnumerable<TrackedMark> current)
    {
        var keys = new HashSet<(uint,uint,uint)>();
        foreach (var mark in current) { keys.Add(mark.Key); _marks[mark.Key] = mark.Copy(); }
        foreach (var key in _marks.Where(pair => !pair.Value.Dead && !keys.Contains(pair.Key)).Select(pair => pair.Key).ToList())
            _marks.Remove(key);
    }
    public void Restore(IEnumerable<TrackedMark> saved)
    {
        foreach (var mark in saved) _marks[mark.Key] = mark.Copy();
    }
    public Dictionary<(uint,uint,uint),TrackedMark> Snapshot() => _marks.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());

    /// <summary>
    /// Drops rows this history is deliberately not keeping.
    ///
    /// Retaining removed dead marks is the whole job of this class - it is why
    /// "Remove Dead" cannot quietly lose kills out of the next report. That is
    /// exactly wrong for marks a report has just gone out for: keeping those
    /// would put the same kills in the NEXT report too, when the train's
    /// remaining legs are finished. Reporting is the one removal that means
    /// forget rather than retain.
    /// </summary>
    public void Forget(IEnumerable<(uint,uint,uint)> keys)
    {
        foreach (var key in keys) _marks.Remove(key);
    }

    public void Clear() => _marks.Clear();
}
