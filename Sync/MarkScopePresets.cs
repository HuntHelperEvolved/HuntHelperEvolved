using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed class MarkScopePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<VisibleMarkRule> Rules { get; set; } = new();
}

/// <summary>Named snapshots; callers keep Active Marks and S-rank relay libraries separate.</summary>
public static class MarkScopePresets
{
    public const int MaxNameLength = 64;

    public static bool TryCreate(List<MarkScopePreset>? library, string? name, IEnumerable<VisibleMarkRule>? rules,
        bool sOnly, [NotNullWhen(true)] out MarkScopePreset? preset, out string error)
    {
        preset = null;
        if (library is null) { error = "Preset library is unavailable."; return false; }
        if (!ValidateName(library, name, null, out var trimmed, out error)) return false;
        preset = new() { Name = trimmed, Rules = Copy(rules, sOnly) };
        library.Add(preset);
        return true;
    }

    public static bool TryUpdate(List<MarkScopePreset>? library, string? id, IEnumerable<VisibleMarkRule>? rules,
        bool sOnly, out string error)
    {
        var preset = Find(library, id);
        if (preset is null) { error = "Preset not found."; return false; }
        preset.Rules = Copy(rules, sOnly);
        error = string.Empty;
        return true;
    }

    public static bool TryRename(List<MarkScopePreset>? library, string? id, string? name, out string error)
    {
        var preset = Find(library, id);
        if (preset is null) { error = "Preset not found."; return false; }
        if (!ValidateName(library!, name, id, out var trimmed, out error)) return false;
        preset.Name = trimmed;
        return true;
    }

    public static bool Delete(List<MarkScopePreset>? library, string? id)
    {
        var preset = Find(library, id);
        return preset is not null && library!.Remove(preset);
    }

    public static List<VisibleMarkRule> Apply(MarkScopePreset? preset, bool sOnly = false) => Copy(preset?.Rules, sOnly);

    public static bool SnapshotMatches(MarkScopePreset? preset, IEnumerable<VisibleMarkRule>? rules, bool sOnly = false)
    {
        if (preset is null) return false;
        var saved = Copy(preset.Rules, sOnly);
        var active = Copy(rules, sOnly);
        // Inclusion is a union: reordered or duplicate rules do not change a snapshot's selections.
        return saved.All(rule => active.Any(other => SameRule(rule, other)))
            && active.All(rule => saved.Any(other => SameRule(rule, other)));
    }

    private static List<VisibleMarkRule> Copy(IEnumerable<VisibleMarkRule>? rules, bool sOnly)
        => sOnly ? RelayScopeFilter.CopyRules(rules) ?? new()
            : rules?.Where(rule => rule is not null).Select(VisibleMarkFilter.Clone).ToList() ?? new();

    private static MarkScopePreset? Find(IEnumerable<MarkScopePreset>? library, string? id)
        => string.IsNullOrWhiteSpace(id) ? null : library?.FirstOrDefault(preset => preset is not null && preset.Id == id);

    private static bool ValidateName(IEnumerable<MarkScopePreset> library, string? name, string? exceptId,
        out string trimmed, out string error)
    {
        trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) { error = "Enter a preset name."; return false; }
        if (name!.Any(char.IsControl)) { error = "Preset names cannot contain control characters."; return false; }
        if (trimmed.Contains("##", StringComparison.Ordinal)) { error = "Preset names cannot contain two consecutive # characters."; return false; }
        if (trimmed.Length > MaxNameLength) { error = $"Preset names can be up to {MaxNameLength} characters."; return false; }
        var normalizedName = trimmed;
        if (library.Any(preset => preset is not null && (exceptId is null || preset.Id != exceptId)
            && string.Equals(preset.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)))
        { error = "A preset with that name already exists."; return false; }
        error = string.Empty;
        return true;
    }

    private static bool SameRule(VisibleMarkRule left, VisibleMarkRule right)
        => left.Enabled == right.Enabled && left.Scope == right.Scope && left.AllExpansions == right.AllExpansions
            && left.Worlds.ToHashSet().SetEquals(right.Worlds)
            && left.DataCenters.ToHashSet().SetEquals(right.DataCenters)
            && left.Ranks.ToHashSet(StringComparer.Ordinal).SetEquals(right.Ranks)
            && left.Expansions.ToHashSet(StringComparer.Ordinal).SetEquals(right.Expansions);
}
