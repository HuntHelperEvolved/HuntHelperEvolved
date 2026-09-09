using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;

public sealed class VisibleMarkOptions
{
    public bool IncludeOwn { get; set; } = true;
    public bool IncludeCommunity { get; set; } = true;
    public bool Alive { get; set; } = true;
    public bool Dead { get; set; } = true;
    public bool Pulled { get; set; } = true;
    public bool NotPulled { get; set; } = true;
    public bool UnknownCombat { get; set; } = true;
    public List<string> Ranks { get; set; } = new() { "B", "A", "S", "SS" };
    // Empty selection means no restriction.
    public List<uint> Worlds { get; set; } = new();
    public List<uint> DataCenters { get; set; } = new();
    public List<string> Expansions { get; set; } = new();
}
public static class VisibleMarkFilter
{
    public static bool Fresh(VisibleMark row, DateTime now) => now < (row.DisplayUntil ?? row.Mark.SeenAt.AddSeconds(3));
    public static bool Matches(VisibleMark row, VisibleMarkOptions options, string self, DateTime now, uint dc, string expansion)
    {
        var mark=row.Mark;
        if (!Fresh(row, now) || !float.IsFinite(mark.HpPercent)
            || mark.HpPercent < 0 || mark.HpPercent > 100) return false;
        if (!row.ObserverIds.Any(id => options.IncludeOwn || id != self)) return false;
        if (!options.Ranks.Contains(mark.Rank)) return false;
        if (options.Worlds.Count > 0 && !options.Worlds.Contains(mark.WorldId)) return false;
        if (options.DataCenters.Count > 0 && !options.DataCenters.Contains(dc)) return false;
        if (options.Expansions.Count > 0 && !options.Expansions.Contains(expansion)) return false;
        if (mark.HpPercent == 0) return options.Dead;
        return options.Alive && (mark.InCombat switch { true => options.Pulled, false => options.NotPulled, null => options.UnknownCombat });
    }
    public static string CombatLabel(SyncSighting mark) => mark.HpPercent == 0 ? "—"
        : mark.InCombat switch { true => "Pulled", false => "Not pulled", null => "Unknown" };
}

public sealed record ActiveMarkRow(SyncSighting Mark, VisibleMark? Visible, SyncSRankStatus? Status)
{
    public bool HealthKnown => Visible is not null;
    public bool HasPosition => float.IsFinite(Mark.X) && float.IsFinite(Mark.Y) && Mark.X >= 1 && Mark.X <= 100 && Mark.Y >= 1 && Mark.Y <= 100;
}
public static class ActiveMarkRows
{
    public static List<ActiveMarkRow> Merge(IEnumerable<VisibleMark> visible, IEnumerable<SyncSRankStatus> statuses, DateTime now)
    {
        var states=statuses.ToDictionary(s=>s.Key);
        var rows=visible.Where(v=>VisibleMarkFilter.Fresh(v,now))
            .ToDictionary(v=>v.Mark.Key,v=>new ActiveMarkRow(v.Mark,v,states.GetValueOrDefault(v.Mark.Key)));
        foreach(var status in states.Values)
        {
            if(rows.ContainsKey(status.Key) || ActiveSRankFilter.Status(status,false,now) is null
                || !SRankTimerData.ByNameId.TryGetValue(status.NameId,out var mark)) continue;
            rows[status.Key]=new(new SyncSighting { NameId=status.NameId,WorldId=status.WorldId,Instance=status.Instance,
                TerritoryId=mark.TerritoryId,Name=mark.Name,Rank="S",X=status.SpawnX??float.NaN,Y=status.SpawnY??float.NaN },null,status);
        }
        return rows.Values.ToList();
    }
    public static bool MatchesTab(string rank,string tab) => tab=="All" || rank==tab || (tab=="S" && rank=="SS");
    public static bool Matches(ActiveMarkRow row,VisibleMarkOptions options,string self,DateTime now,uint dc,string expansion)
    {
        if(row.Visible is { } visible) return VisibleMarkFilter.Matches(visible,options,self,now,dc,expansion);
        var m=row.Mark;
        return options.IncludeCommunity && options.Alive && options.UnknownCombat && options.Ranks.Contains(m.Rank)
            && (options.Worlds.Count==0 || options.Worlds.Contains(m.WorldId))
            && (options.DataCenters.Count==0 || options.DataCenters.Contains(dc))
            && (options.Expansions.Count==0 || options.Expansions.Contains(expansion));
    }
}
