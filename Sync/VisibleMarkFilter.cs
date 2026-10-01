using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;

public enum VisibleMarkScope
{
    Any,
    CurrentWorld,
    CurrentDataCenter,
}

public sealed class VisibleMarkRule
{
    public bool Enabled { get; set; } = true;
    public VisibleMarkScope Scope { get; set; }
    // Fixed world and data-centre selections intersect, including with a dynamic scope.
    public List<uint> Worlds { get; set; } = new();
    public List<uint> DataCenters { get; set; } = new();
    public List<string> Ranks { get; set; } = new() { "B", "A", "S", "SS" };
    // All includes future expansions; otherwise an empty selection includes nothing.
    public bool AllExpansions { get; set; } = true;
    public List<string> Expansions { get; set; } = new();
}

public sealed class VisibleMarkOptions
{
    public bool ShowDataCenter { get; set; }
    public bool IncludeOwn { get; set; } = true;
    public bool IncludeCommunity { get; set; } = true;
    public bool Alive { get; set; } = true;
    public bool Dead { get; set; } = true;
    public bool Pulled { get; set; } = true;
    public bool NotPulled { get; set; } = true;
    public bool UnknownCombat { get; set; } = true;
    // Missing rules retain the legacy filters until the first edit. An empty list means none.
    public List<VisibleMarkRule>? Rules { get; set; }
    public List<string> Ranks { get; set; } = new() { "B", "A", "S", "SS" };
    // Empty selection means no restriction.
    public List<uint> Worlds { get; set; } = new();
    public List<uint> DataCenters { get; set; } = new();
    public List<string> Expansions { get; set; } = new();
}
public static class VisibleMarkFilter
{
    public static bool Fresh(VisibleMark row, DateTime now) => now < (row.DisplayUntil ?? row.Mark.SeenAt.AddSeconds(3));
    public static bool Matches(VisibleMark row, VisibleMarkOptions options, string self, DateTime now, uint dc, string expansion,
        uint currentWorld = 0, uint currentDc = 0)
    {
        var mark=row.Mark;
        if (!Fresh(row, now) || !float.IsFinite(mark.HpPercent)
            || mark.HpPercent < 0 || mark.HpPercent > 100) return false;
        if (!row.ObserverIds.Any(id => options.IncludeOwn || id != self)) return false;
        if (!MatchesScope(options, mark.Rank, mark.WorldId, dc, expansion, currentWorld, currentDc)) return false;
        if (mark.HpPercent == 0) return options.Dead;
        return options.Alive && (mark.InCombat switch { true => options.Pulled, false => options.NotPulled, null => options.UnknownCombat });
    }
    public static string CombatLabel(SyncSighting mark) => mark.HpPercent == 0 ? "—"
        : mark.InCombat switch { true => "Pulled", false => "Not pulled", null => "Unknown" };

    public static bool MatchesScope(VisibleMarkOptions options, string rank, uint world, uint dc, string expansion,
        uint currentWorld = 0, uint currentDc = 0)
    {
        if (options.Rules is { } rules)
            return rules.Any(rule => rule is not null && MatchesRule(rule, rank, world, dc, expansion, currentWorld, currentDc));
        // Keep old saved world/DC/expansion intersections unchanged until explicitly migrated.
        return options.Ranks?.Contains(rank) == true
            && (options.Worlds is not { Count: > 0 } || options.Worlds.Contains(world))
            && (options.DataCenters is not { Count: > 0 } || options.DataCenters.Contains(dc))
            && (options.Expansions is not { Count: > 0 } || options.Expansions.Contains(expansion));
    }

    private static bool MatchesRule(VisibleMarkRule rule, string rank, uint world, uint dc, string expansion,
        uint currentWorld, uint currentDc)
        => rule.Enabled && rule.Ranks?.Contains(rank) == true
            && (rule.Scope switch
            {
                VisibleMarkScope.Any => true,
                VisibleMarkScope.CurrentWorld => currentWorld != 0 && world == currentWorld,
                VisibleMarkScope.CurrentDataCenter => currentDc != 0 && dc == currentDc,
                _ => false,
            })
            && (rule.Worlds is not { Count: > 0 } || world != 0 && rule.Worlds.Contains(world))
            && (rule.DataCenters is not { Count: > 0 } || dc != 0 && rule.DataCenters.Contains(dc))
            && (rule.AllExpansions || rule.Expansions?.Contains(expansion) == true);

    public static List<VisibleMarkRule> MigrateLegacy(VisibleMarkOptions options)
    {
        options.Rules ??= new()
        {
            new()
            {
                Ranks = options.Ranks?.ToList() ?? new(),
                Worlds = options.Worlds?.ToList() ?? new(),
                DataCenters = options.DataCenters?.ToList() ?? new(),
                AllExpansions = options.Expansions is not { Count: > 0 },
                Expansions = options.Expansions?.ToList() ?? new(),
            },
        };
        // Configuration can contain explicit nulls. Invalid rules include nothing.
        options.Rules.RemoveAll(rule => rule is null);
        foreach (var rule in options.Rules) Normalize(rule);
        return options.Rules;
    }

    public static void Normalize(VisibleMarkRule rule)
    {
        rule.Worlds ??= new();
        rule.DataCenters ??= new();
        rule.Ranks ??= new();
        rule.Expansions ??= new();
    }

    public static VisibleMarkRule Clone(VisibleMarkRule rule) => new()
    {
        Enabled = rule.Enabled,
        Scope = rule.Scope,
        Worlds = rule.Worlds?.ToList() ?? new(),
        DataCenters = rule.DataCenters?.ToList() ?? new(),
        Ranks = rule.Ranks?.ToList() ?? new(),
        AllExpansions = rule.AllExpansions,
        Expansions = rule.Expansions?.ToList() ?? new(),
    };
}

public sealed record ActiveMarkRow(SyncSighting Mark, VisibleMark? Visible, SyncSRankStatus? Status, BearMark? Bear = null, bool BearHealthKnown = false, bool BearHealthStale = false)
{
    public bool HealthKnown => Visible is not null || BearHealthKnown;
    public bool HealthStale => Visible is null && BearHealthStale;
    public bool HasPosition => float.IsFinite(Mark.X) && float.IsFinite(Mark.Y) && Mark.X >= 1 && Mark.X <= 100 && Mark.Y >= 1 && Mark.Y <= 100;
}
public static class ActiveMarkRows
{
    public static string FaloopAge(SyncSRankStatus? status, DateTime serverNow)
    {
        if (status?.FaloopActiveAt is not { } released || status.FaloopActiveUntil is not { } until
            || until <= serverNow) return string.Empty;
        var age = serverNow > released ? serverNow - released : TimeSpan.Zero;
        return $"{(int)age.TotalHours:00}:{age.Minutes:00}:{age.Seconds:00}";
    }

    // Observers overlap: use the broadest fresh count, never add their totals.
    public static SyncSighting MergeObservation(SyncSighting local, SyncSighting remote)
    {
        var latest = local.SeenAt >= remote.SeenAt ? local : remote;
        var count = new[] { local.NearbyPlayers, remote.NearbyPlayers }.Max();
        return latest.WithNearbyPlayers(count);
    }

    public static List<ActiveMarkRow> Merge(IEnumerable<VisibleMark> visible, IEnumerable<SyncSRankStatus> statuses, DateTime now)
        => Merge(visible, statuses.ToDictionary(s => s.Key), now);

    public static List<ActiveMarkRow> Merge(IEnumerable<VisibleMark> visible,
        IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), SyncSRankStatus> states, DateTime now,
        DateTime? serverNow = null)
    {
        var rows=visible.Where(v=>VisibleMarkFilter.Fresh(v,now))
            .Where(v => v.Mark.HpPercent == 0
                || ActiveSRankFilter.DeathEvidenceAt(states.GetValueOrDefault(v.Mark.Key)) is not { } death
                || v.Mark.SeenAt > death)
            .ToDictionary(v=>v.Mark.LiveKey,v=>new ActiveMarkRow(v.Mark,v,states.GetValueOrDefault(v.Mark.Key)));
        var represented = rows.Values.Select(r => r.Mark.Key).ToHashSet();
        foreach(var status in states.Values)
        {
            if(represented.Contains(status.Key) || ActiveSRankFilter.Status(status,false,serverNow ?? now) is null
                || !SRankTimerData.ByNameId.TryGetValue(status.NameId,out var mark)) continue;
            rows[(status.NameId,status.Instance,status.WorldId,0,0)]=new(new SyncSighting { NameId=status.NameId,WorldId=status.WorldId,Instance=status.Instance,
                TerritoryId=mark.TerritoryId,Name=mark.Name,Rank="S",X=status.SpawnX??float.NaN,Y=status.SpawnY??float.NaN },null,status);
        }
        return rows.Values.ToList();
    }
    public static List<ActiveMarkRow> MergeBear(List<ActiveMarkRow> rows, IEnumerable<BearMark> reports,
        DateTime serverNow, IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), SyncSRankStatus>? states = null,
        IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), DateTime>? pluginDeaths = null)
    {
        var byKey = rows.GroupBy(r => r.Mark.Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var bear in reports)
        {
            byKey.TryGetValue(bear.Key, out var existing);
            // Any fresh scout observation wins, including a freshly observed corpse.
            if (existing?.Any(r => r.Visible is not null) == true) continue;
            var status = existing?.FirstOrDefault()?.Status ?? states?.GetValueOrDefault(bear.Key);
            // Faloop is lower priority than a fresh Bear report. Only plugin/group evidence can veto it.
            var death = string.Equals(status?.KillSource, "Faloop", StringComparison.OrdinalIgnoreCase) ? null : status?.KilledAt;
            if (pluginDeaths?.TryGetValue(bear.Key, out var recorded) == true && (death is null || recorded > death)) death = recorded;
            if (!bear.Report.Maintenance && !bear.Report.Uncertain && bear.HasDeath(serverNow) && status?.SpawnedAt is { } spawn && spawn <= bear.Report.KilledAt)
                byKey.Remove(bear.Key);
            var active = bear.IsActive(serverNow) && (death is null || bear.Report.SeenAt > death);
            var dead = !active && bear.RecentDeath(serverNow) && (death is null || bear.Report.KilledAt >= death);
            if (!active && !dead) continue;
            byKey[bear.Key] = new() { new(bear.Sighting(serverNow, dead), null, status, bear,
                dead || bear.HealthFresh(serverNow), !dead && bear.HealthStale(serverNow)) };
        }
        return byKey.Values.SelectMany(group => group).ToList();
    }

    // A current direct report proves the world/instance exists even if Faloop metadata lags behind.
    public static bool SuppressedByFaloop(ActiveMarkRow row, bool offline, bool currentInstance) =>
        row.Visible is null && row.Bear is null && (offline || !currentInstance);

    public static bool MatchesTab(string rank,string tab) => tab=="All" || rank==tab || (tab=="S" && rank=="SS");
    public static bool Matches(ActiveMarkRow row,VisibleMarkOptions options,string self,DateTime now,uint dc,string expansion,
        uint currentWorld=0,uint currentDc=0)
    {
        if(row.Visible is { } visible) return VisibleMarkFilter.Matches(visible,options,self,now,dc,expansion,currentWorld,currentDc);
        var m=row.Mark;
        return options.IncludeCommunity && (row.HealthKnown && m.HpPercent == 0 ? options.Dead : options.Alive && options.UnknownCombat)
            && VisibleMarkFilter.MatchesScope(options,m.Rank,m.WorldId,dc,expansion,currentWorld,currentDc);
    }
}
