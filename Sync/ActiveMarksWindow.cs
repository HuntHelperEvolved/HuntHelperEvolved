using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace HuntHelperEvolved.Sync;

public sealed class ActiveMarksWindow(Configuration config, SyncCoordinator sync, WorldData worlds,
    MarkDetector detector, IGameGui gameGui, LifestreamTravel travel, Action openSettings)
{
    private string _search = string.Empty;
    public void Toggle() { config.ActiveSRankWindowOpen = !config.ActiveSRankWindowOpen; config.Save(); }
    public void Draw()
    {
        if (!config.ActiveSRankWindowOpen) return;
        var open=true;
        ImGui.SetNextWindowSize(new Vector2(460,300),ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Active Marks###ActiveMarksCompact",ref open))
        {
            ImGui.SetNextItemWidth(Math.Max(80,ImGui.GetContentRegionAvail().X-70));
            ImGui.InputTextWithHint("##activeSearch","Search marks…",ref _search,100);
            ImGui.SameLine();
            if (ImGui.SmallButton("Filters")) openSettings();
            if (!config.SyncEnabled || !sync.IsConnected) ImGui.TextWrapped("Reports unavailable. " + sync.Status);
            else
            {
                if (!sync.SupportsVisibleMarks) ImGui.TextWrapped("Update the server to 0.3.11 for live A/B/S observations, health and combat state.");
                if (!string.IsNullOrEmpty(travel.Status)) ImGui.TextWrapped(travel.Status);
                if (travel.Busy && ImGui.SmallButton("Cancel travel")) travel.Cancel();
                if (ImGui.BeginTabBar("activeMarkRanks"))
                {
                    foreach(var tab in new[] {"All","S","A","B"})
                        if(ImGui.BeginTabItem(tab)) { DrawRows(tab); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
            }
        }
        ImGui.End();
        if (!open) { config.ActiveSRankWindowOpen=false; config.Save(); }
    }
    private void DrawRows(string tab)
    {
        var now=DateTime.UtcNow;
        var visible=sync.VisibleMarks.ToDictionary(v=>v.Mark.Key);
        if(config.VisibleMarkFilters.IncludeOwn)
            foreach(var local in detector.VisibleMarks.Where(v=>now-v.LastSeenUtc<TimeSpan.FromSeconds(1)))
            {
                var previous=visible.GetValueOrDefault(local.Key);
                visible[local.Key]=new VisibleMark
                {
                    Mark=new SyncSighting { NameId=local.NameId,WorldId=local.WorldId,Instance=local.Instance,
                        TerritoryId=local.TerritoryId,MapId=local.MapId,Name=local.Name,Rank=previous?.Mark.Rank??local.Rank.ToString(),
                        X=local.MapPosition.X,Y=local.MapPosition.Y,HpPercent=local.HealthPercent,InCombat=local.InCombat,SeenAt=local.LastSeenUtc },
                    ObserverIds=(previous?.ObserverIds??new List<string>()).Append(sync.ClientId).Distinct().ToList(),
                    Observers=(previous?.Observers??new List<string>()).Append("You").Distinct().ToList()
                };
            }
        var rows=ActiveMarkRows.Merge(visible.Values,sync.SRankStatuses.Values,now).Select(row =>
        {
            var dc=worlds.LocateWorld(row.Mark.WorldId) is { } loc ? worlds.DataCenters[loc.DcIndex] : default;
            var expansion=SRankTimerData.ForTerritory(row.Mark.TerritoryId)?.Expansion ?? "Unknown";
            return (Row:row,Dc:dc,Expansion:expansion,World:worlds.NameOf(row.Mark.WorldId),Zone:detector.GetZoneName(row.Mark.TerritoryId));
        }).Where(r => ActiveMarkRows.MatchesTab(r.Row.Mark.Rank,tab) && ActiveMarkRows.Matches(r.Row,config.VisibleMarkFilters,sync.ClientId,now,r.Dc.Id,r.Expansion))
          .Where(r => (r.Row.Mark.Name+" "+r.World+" "+r.Zone+" "+r.Dc.Name+" "+string.Join(" ",r.Row.Visible?.Observers??new List<string>())).Contains(_search,StringComparison.OrdinalIgnoreCase))
          .OrderBy(r => r.Row.HealthKnown && r.Row.Mark.HpPercent==0).ThenByDescending(r => r.Row.Mark.InCombat==true)
          .ThenBy(r => r.World).ThenBy(r => r.Zone).ThenBy(r => r.Row.Mark.Name).ThenBy(r => r.Row.Mark.Instance).ToList();
        if(rows.Count==0) { ImGui.TextDisabled("No matching marks."); return; }
        if(ImGui.BeginChild("activeMarkLines",Vector2.Zero,false,ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach(var r in rows)
            {
                var row=r.Row; var m=row.Mark; var dead=row.HealthKnown && m.HpPercent==0;
                var colour=!row.HealthKnown ? new Vector4(0.35f,0.7f,1f,1)
                    : m.InCombat is null ? new Vector4(0.75f,0.75f,0.8f,1)
                    : m.InCombat==true ? new Vector4(1,0.65f,0.15f,1) : new Vector4(0.35f,0.95f,0.4f,1);
                if(dead) colour=new Vector4(1,0.3f,0.3f,1);
                var hp=row.HealthKnown ? $"{m.HpPercent:0.#}%" : "?%";
                var instance=m.Instance>0 ? $" i{m.Instance}" : string.Empty;
                var label=$"{(tab=="All" ? m.Rank+": " : string.Empty)}{m.Name} - {hp} [{r.World}{instance}]";
                ImGui.PushID($"{m.WorldId}:{m.Instance}:{m.NameId}");
                ImGui.PushStyleColor(ImGuiCol.Text,colour);
                var clicked=ImGui.Selectable(label+"###mark",false,ImGuiSelectableFlags.None,
                    new Vector2(Math.Max(ImGui.GetContentRegionAvail().X,ImGui.CalcTextSize(label).X),0));
                ImGui.PopStyleColor();
                var hovered=ImGui.IsItemHovered();
                var canTravel=travel.Available && !travel.Busy && !dead && row.HasPosition;
                if(clicked)
                {
                    if(ImGui.GetIO().KeyCtrl) { if(canTravel) travel.Start(m.WorldId,m.TerritoryId,new(m.X,m.Y)); }
                    else if(row.HasPosition) Flag(m);
                }
                if(ImGui.BeginPopupContextItem("markActions"))
                {
                    ImGui.TextUnformatted(m.Name+" — "+r.World+instance);
                    ImGui.BeginDisabled(!row.HasPosition);
                    if(ImGui.MenuItem("Open map")) Flag(m);
                    ImGui.EndDisabled();
                    if(travel.Available)
                    {
                        ImGui.BeginDisabled(!canTravel);
                        if(ImGui.MenuItem("Teleport to mark")) travel.Start(m.WorldId,m.TerritoryId,new(m.X,m.Y));
                        ImGui.EndDisabled();
                    }
                    if(ImGui.MenuItem("Filters / settings")) openSettings();
                    ImGui.EndPopup();
                }
                if(hovered)
                {
                    var state=dead ? "Dead" : !row.HealthKnown ? "Community report — health and combat unknown"
                        : m.InCombat is null ? "Alive — combat unknown" : m.InCombat==true ? "Alive — pulled" : "Alive — not pulled";
                    var detail=$"{state}\n{r.World}{instance} · {r.Dc.Name??"Unknown DC"}\n{r.Zone}";
                    if(row.HasPosition) detail+=$" ({m.X:0.0}, {m.Y:0.0})";
                    if(!dead && row.Status?.SpawnedAt is { } spawned)
                    {
                        var age=now-spawned;
                        detail+=$"\nActive for {(int)Math.Max(0,age.TotalHours):00}:{Math.Max(0,age.Minutes):00}:{Math.Max(0,age.Seconds):00}";
                    }
                    detail+="\n"+(row.Visible is { } observation ? "Seen by: "+string.Join(", ",observation.Observers) : "Faloop report");
                    detail+=row.HasPosition ? "\nClick: map" : "\nLocation not reported";
                    if(canTravel) detail+=" · Ctrl-click: teleport";
                    detail+="\nRight-click for actions";
                    ImGui.SetTooltip(detail);
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }
    private void Flag(SyncSighting mark) => MapFlagHelper.FlagPosition(gameGui,mark.TerritoryId,
        mark.MapId==0 ? detector.GetMapId(mark.TerritoryId) : mark.MapId,mark.Instance,mark.X,mark.Y);
    private void Option(string label,bool value,Action<bool> save)
    { if (ImGui.Checkbox(label,ref value)) { save(value); config.Save(); } }
    private void Select<T>(string label,T value,List<T> selected)
    {
        var enabled=selected.Contains(value);
        if (ImGui.Checkbox(label,ref enabled)) { if(enabled) selected.Add(value); else selected.Remove(value); config.Save(); }
    }
    public void DrawSettings()
    {
        if (!ImGui.CollapsingHeader("Active Marks window filters",ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("visibleSettings");
        if (ImGui.Button("Open Active Marks (/hhsa or /hhv)")) Toggle();
        ImGui.TextColored(new Vector4(0.35f,0.95f,0.4f,1),"Green: alive, not pulled");
        ImGui.SameLine(); ImGui.TextColored(new Vector4(1,0.65f,0.15f,1),"Orange: pulled");
        ImGui.SameLine(); ImGui.TextColored(new Vector4(1,0.3f,0.3f,1),"Red: dead");
        ImGui.TextColored(new Vector4(0.35f,0.7f,1f,1),"Blue: Faloop report, no live feedback");
        ImGui.TextDisabled("Grey: live report with unknown combat state. ?%: health unknown. Hover for details; click for map; Ctrl-click or right-click for travel.");
        var o=config.VisibleMarkFilters;
        Option("Include community S-rank reports",o.IncludeCommunity,v=>o.IncludeCommunity=v);
        Option("Include marks seen only by me",o.IncludeOwn,v=>o.IncludeOwn=v);
        foreach (var rank in new[] {"B","A","S","SS"}) { Select(rank+" ranks",rank,o.Ranks); ImGui.SameLine(); }
        ImGui.NewLine();
        Option("Alive",o.Alive,v=>o.Alive=v); ImGui.SameLine(); Option("Dead (visible corpses)",o.Dead,v=>o.Dead=v);
        Option("Pulled",o.Pulled,v=>o.Pulled=v); ImGui.SameLine(); Option("Not pulled",o.NotPulled,v=>o.NotPulled=v);
        ImGui.SameLine(); Option("Unknown combat status",o.UnknownCombat,v=>o.UnknownCombat=v);
        ImGui.TextDisabled("Combat filters apply to living marks. Empty world/DC/expansion selections include all.");
        if (ImGui.TreeNode("Expansions"))
        {
            foreach(var expansion in SRankTimerData.Expansions.Append("Unknown")) Select(expansion,expansion,o.Expansions);
            if(ImGui.SmallButton("All expansions")) { o.Expansions.Clear(); config.Save(); }
            ImGui.TreePop();
        }
        if (ImGui.TreeNode("Data centres"))
        {
            foreach(var dc in worlds.DataCenters) Select(dc.Name,dc.Id,o.DataCenters);
            if(ImGui.SmallButton("All data centres")) { o.DataCenters.Clear(); config.Save(); }
            ImGui.TreePop();
        }
        if (ImGui.TreeNode("Worlds"))
        {
            foreach(var dc in worlds.DataCenters)
                if(ImGui.TreeNode(dc.Name))
                {
                    foreach(var world in worlds.WorldsIn(dc.Id)) Select(world.Name,world.RowId,o.Worlds);
                    ImGui.TreePop();
                }
            if(ImGui.SmallButton("All worlds")) { o.Worlds.Clear(); config.Save(); }
            ImGui.TreePop();
        }
        if(ImGui.SmallButton("Reset window filters")) { config.VisibleMarkFilters=new(); config.Save(); }
        ImGui.PopID();
    }
}
