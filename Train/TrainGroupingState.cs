using System.Collections.Generic;

namespace HuntHelperEvolved;

internal sealed class TrainGroupingState
{
    private readonly record struct MarkState(uint Name, uint Instance, uint World, int Order, string Zone);
    private readonly List<MarkState> _marks = new();
    private readonly List<(uint World, string? Expansion, bool Default)> _preferences = new();
    private bool _initialized, _grouped, _shared;

    internal bool Changed(IReadOnlyList<DetectedMark> marks, bool grouped, bool shared,
        IReadOnlyList<string> fallback, IReadOnlyDictionary<uint, List<string>> worldOrders)
    {
        var changed = !_initialized || grouped != _grouped || shared != _shared || marks.Count != _marks.Count;
        _initialized = true; _grouped = grouped; _shared = shared;
        for (var i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            var state = new MarkState(m.NameId, m.Instance, m.WorldId, m.Order, m.ZoneName);
            if (i == _marks.Count) _marks.Add(state);
            else { changed |= _marks[i] != state; _marks[i] = state; }
        }
        if (_marks.Count > marks.Count) _marks.RemoveRange(marks.Count, _marks.Count - marks.Count);
        var index = 0;
        if (grouped && !shared)
        {
            ComparePreference((0, null, true), ref index, ref changed);
            foreach (var expansion in fallback) ComparePreference((0, expansion, true), ref index, ref changed);
            foreach (var (world, order) in worldOrders)
            {
                ComparePreference((world, null, false), ref index, ref changed);
                foreach (var expansion in order) ComparePreference((world, expansion, false), ref index, ref changed);
            }
        }
        if (index != _preferences.Count) changed = true;
        if (index < _preferences.Count) _preferences.RemoveRange(index, _preferences.Count - index);
        return changed;
    }

    private void ComparePreference((uint World, string? Expansion, bool Default) value, ref int index, ref bool changed)
    {
        if (index == _preferences.Count) { _preferences.Add(value); changed = true; }
        else { changed |= _preferences[index] != value; _preferences[index] = value; }
        index++;
    }
}
