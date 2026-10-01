using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

/// <summary>Independent inclusion rules for the existing timed S-rank chat relays.</summary>
public static class RelayScopeFilter
{
    public static List<VisibleMarkRule> MigrateLegacy(bool currentDc, IEnumerable<uint>? dataCenters)
    {
        if (currentDc)
            return new() { new() { Scope = VisibleMarkScope.CurrentDataCenter, Ranks = new() { "S" } } };

        var selected = dataCenters?.Where(dc => dc != 0).Distinct().ToList() ?? new();
        // The old relay selector's empty DC list means none, unlike Active Marks filters.
        return selected.Count == 0 ? new()
            : new() { new() { DataCenters = selected, Ranks = new() { "S" } } };
    }

    public static bool Matches(IReadOnlyList<VisibleMarkRule>? rules, bool legacyCurrentDc,
        IEnumerable<uint>? legacyDataCenters, string rank, uint world, uint dc, string expansion,
        uint currentWorld = 0, uint currentDc = 0)
    {
        // Copying a broad Active Marks rule must not enable new relay ranks.
        if (rank != "S") return false;
        var options = new VisibleMarkOptions { Rules = rules?.ToList() ?? MigrateLegacy(legacyCurrentDc, legacyDataCenters) };
        return VisibleMarkFilter.MatchesScope(options, rank, world, dc, expansion, currentWorld, currentDc);
    }

    /// <summary>Copies only S-rank inclusions; null stays legacy and no S rules becomes an explicit empty list.</summary>
    public static List<VisibleMarkRule>? CopyRules(IEnumerable<VisibleMarkRule>? rules)
        => rules?.Where(rule => rule is not null && rule.Ranks?.Contains("S") == true)
            .Select(rule =>
            {
                var copy = VisibleMarkFilter.Clone(rule);
                copy.Ranks = new() { "S" };
                return copy;
            }).ToList();

    public static List<VisibleMarkRule> FromActiveMarks(VisibleMarkOptions options)
    {
        if (options.Rules is { } rules) return CopyRules(rules)!;
        // Migrate a separate options object: copying must not edit or migrate the /hhv settings.
        var legacy = new VisibleMarkOptions
        {
            Ranks = options.Ranks?.ToList() ?? new(),
            Worlds = options.Worlds?.ToList() ?? new(),
            DataCenters = options.DataCenters?.ToList() ?? new(),
            Expansions = options.Expansions?.ToList() ?? new(),
        };
        return CopyRules(VisibleMarkFilter.MigrateLegacy(legacy))!;
    }
}
