using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

/// <summary>Tracks completed legs across frames, using the full route before Hide dead filters it.</summary>
internal sealed class TrainExpansionProgress
{
    private readonly HashSet<string> _finished = new();
    private bool _seeded;

    public void Reset()
    {
        _finished.Clear();
        _seeded = false;
    }

    public IReadOnlyList<string> Update(IEnumerable<(string Expansion, bool Dead)> marks)
    {
        // Custom flags remain route stops; callers include them even though headings exclude them.
        var blocks = marks.GroupBy(mark => mark.Expansion)
            .Select(group => (Expansion: group.Key, Up: group.Any(mark => !mark.Dead))).ToList();
        if (blocks.Count == 0) { Reset(); return System.Array.Empty<string>(); }
        if (!_seeded)
        {
            _seeded = true;
            foreach (var block in blocks.Where(block => !block.Up)) _finished.Add(block.Expansion);
            return System.Array.Empty<string>();
        }
        _finished.RemoveWhere(expansion => !blocks.Any(block => block.Expansion == expansion));
        var opened = new List<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Up) { _finished.Remove(blocks[i].Expansion); continue; }
            if (!_finished.Add(blocks[i].Expansion)) continue;
            for (var next = i + 1; next < blocks.Count; next++)
            {
                if (!blocks[next].Up) continue;
                if (!opened.Contains(blocks[next].Expansion)) opened.Add(blocks[next].Expansion);
                break;
            }
        }
        return opened;
    }
}
