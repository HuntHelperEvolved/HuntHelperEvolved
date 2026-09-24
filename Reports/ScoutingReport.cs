using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HuntHelperEvolved;

public static class ScoutingReport
{
    public const int MaxNotesLength = 256;

    public static int NoteCharacterCount(string? notes) => (notes ?? string.Empty).EnumerateRunes().Count();

    /// <summary>Normalizes report notes and caps Unicode code points without splitting surrogate pairs.</summary>
    public static string NormalizeNotes(string? notes, bool preserveOuterWhitespace = false)
    {
        var normalized = (notes ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        if (!preserveOuterWhitespace) normalized = normalized.Trim();
        var text = new StringBuilder();
        var count = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsControl(rune) && rune.Value != '\n' && rune.Value != '\t') continue;
            if (count++ == MaxNotesLength) break;
            text.Append(rune.Value == '\t' ? " " : rune.ToString());
        }
        return preserveOuterWhitespace ? text.ToString() : text.ToString().Trim();
    }

    // These compatibility records have no world or death evidence. Never invent either.
    public static string BuildSummary(List<TrainMobRecord> marks) => BuildSummary(ToNative(marks));

    internal static List<NativeTrainRecord> ToNative(IEnumerable<TrainMobRecord> marks) => marks.Select(m =>
        new NativeTrainRecord(m.Name, m.MobID, m.TerritoryID, m.MapID, m.Instance, 0, string.Empty,
            m.Position, m.Dead, m.LastSeenUTC, null, null)).ToList();

    /// <summary>
    /// Summarizes recorded state by world/expansion, preserving the distinction
    /// between witnessed kills, marked snipes and unknown deaths. Roster absence
    /// means only that a name is not recorded on that world, in any instance.
    /// </summary>
    public static string BuildSummary(IReadOnlyList<NativeTrainRecord> marks)
    {
        var rows = marks.GroupBy(m => (m.WorldId, m.NameId, m.Instance)).Select(g => g.Last()).ToList();
        var blocks = new List<string>();
        foreach (var world in rows.GroupBy(m => m.WorldId))
        {
            var worldName = world.Select(m => m.WorldName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? (world.Key == 0 ? "Unknown world" : $"World {world.Key}");
            var recordedNames = world.Select(m => m.NameId).ToHashSet();
            var expansions = world.Select(m => (Mark: m, Info: ExpansionData.Lookup(m.NameId)))
                .GroupBy(x => x.Info?.Expansion ?? "Other recorded marks")
                .OrderBy(g => g.Min(x => x.Info?.Order ?? int.MaxValue));
            foreach (var expansion in expansions)
            {
                var ordered = expansion.OrderBy(x => x.Info?.ZoneOrder ?? int.MaxValue)
                    .ThenBy(x => x.Mark.Name).ThenBy(x => x.Mark.Instance).Select(x => x.Mark).ToList();
                var down = ordered.Where(m => m.Dead).ToList();
                var block = new StringBuilder($"**{EscapeText(worldName)} / {EscapeText(expansion.Key)}** — {ordered.Count - down.Count}/{ordered.Count}");
                AppendExceptions(block, "Marked sniped", down.Where(m => m.SnipedAtUtc.HasValue));
                AppendExceptions(block, "Killed", down.Where(m => !m.SnipedAtUtc.HasValue && m.DeathObservedAtUtc.HasValue));
                AppendExceptions(block, "Down — time unknown", down.Where(m => !m.SnipedAtUtc.HasValue && !m.DeathObservedAtUtc.HasValue));

                var absent = ExpansionData.ModelIdToMark
                    .Where(p => p.Value.Expansion == expansion.Key && !recordedNames.Contains(p.Key))
                    .OrderBy(p => p.Value.ZoneOrder).ThenBy(p => p.Value.Name)
                    .Select(p => EscapeText(p.Value.Name)).ToList();
                if (absent.Count > 0)
                    block.Append($"\nNot recorded: {string.Join(", ", absent)} (instances unknown)");
                blocks.Add(block.ToString());
            }
        }
        return blocks.Count > 0 ? string.Join("\n\n", blocks) : "No recorded marks in the current scout.";
    }

    private static void AppendExceptions(StringBuilder text, string label, IEnumerable<NativeTrainRecord> marks)
    {
        var names = marks.Select(m => EscapeText(string.IsNullOrWhiteSpace(m.Name)
            ? ExpansionData.Lookup(m.NameId)?.Name ?? $"Mark {m.NameId}" : m.Name) + ExpansionData.InstanceGlyph(m.Instance)).ToList();
        if (names.Count > 0) text.Append($"\n{label}: {string.Join(", ", names)}");
    }

    /// <summary>Displays user-supplied report text literally, outside the import code block.</summary>
    internal static string EscapeText(string text)
    {
        var escaped = new StringBuilder();
        foreach (var character in text)
        {
            if ("\\`*_~|[]<>#+-.!".Contains(character)) escaped.Append('\\');
            escaped.Append(character);
        }
        return escaped.ToString();
    }
}
