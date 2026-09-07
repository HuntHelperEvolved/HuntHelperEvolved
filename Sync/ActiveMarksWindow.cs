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
        ImGui.SetNextWindowSize(new Vector2(1150,460),ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Active Marks",ref open))
        {
            if (ImGui.Button("Filters / settings")) openSettings();
            ImGui.SameLine(); ImGui.TextDisabled("/hhsa or /hhv. Click coordinates to open the map.");
            if (!config.SyncEnabled || !sync.IsConnected) ImGui.TextWrapped("Reports unavailable. " + sync.Status);
            else
            {
                if (!sync.SupportsVisibleMarks) ImGui.TextWrapped("Update the server to 0.3.11 for live A/B/S observations, health and combat state.");
                ImGui.SetNextItemWidth(320);
                ImGui.InputTextWithHint("##activeSearch","Search mark, world, DC, zone or scout",ref _search,100);
                ImGui.TextDisabled("Live observations expire after 3 seconds without updates. Community reports have unknown HP and combat status.");
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
        ImGui.TextDisabled($"{rows.Count} marks. Right-click headers to show/hide columns. SS events appear under S.");
        if (!ImGui.BeginTable("activeMarksTable",13,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH|ImGuiTableFlags.Resizable|ImGuiTableFlags.Hideable|ImGuiTableFlags.ScrollY)) return;
        ImGui.TableSetupScrollFreeze(0,1);
        foreach (var label in new[] {"Mark","Rank","World","DC","Zone","Instance","State","Combat","HP","Coordinates","Active for","Teleport","Source / seen by"})
        {
            var flags=label is "Mark" or "Zone" or "Source / seen by" ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed;
            if(label is "DC" or "Source / seen by") flags |= ImGuiTableColumnFlags.DefaultHide;
            if((label=="Instance" && !rows.Any(r=>r.Row.Mark.Instance>0)) || (label=="Teleport" && !travel.Available)) flags |= ImGuiTableColumnFlags.Disabled;
            ImGui.TableSetupColumn(label,flags);
        }
        ImGui.TableHeadersRow();
        foreach (var r in rows)
        {
            var m=r.Row.Mark; var dead=r.Row.HealthKnown && m.HpPercent==0;
            ImGui.PushID($"{m.WorldId}:{m.Instance}:{m.NameId}"); ImGui.TableNextRow();
            Cell(m.Name); Cell(m.Rank); Cell(r.World); Cell(r.Dc.Name??"Unknown"); Cell(r.Zone);
            Cell(m.Instance==0 ? "" : m.Instance.ToString());
            ImGui.TableNextColumn(); ImGui.TextColored(dead ? new Vector4(0.7f,0.7f,0.7f,1) : new Vector4(0.4f,1,0.4f,1),dead ? "Dead" : r.Row.HealthKnown ? "Alive" : "Reported active");
            ImGui.TableNextColumn(); ImGui.TextColored(m.InCombat==true && !dead ? new Vector4(1,0.4f,0.35f,1) : Vector4.One,VisibleMarkFilter.CombatLabel(m));
            Cell(r.Row.HealthKnown ? $"{m.HpPercent:0.0}%" : "—");
            ImGui.TableNextColumn();
            if(r.Row.HasPosition)
            {
                if(ImGui.SmallButton($"{m.X:0.0}, {m.Y:0.0}")) MapFlagHelper.FlagPosition(gameGui,m.TerritoryId,m.MapId==0 ? detector.GetMapId(m.TerritoryId) : m.MapId,m.Instance,m.X,m.Y);
            }
            else ImGui.TextDisabled("—");
            ImGui.TableNextColumn();
            if(!dead && r.Row.Status?.SpawnedAt is { } spawned)
            {
                var age=now-spawned; ImGui.TextUnformatted($"{(int)Math.Max(0,age.TotalHours):00}:{Math.Max(0,age.Minutes):00}:{Math.Max(0,age.Seconds):00}");
            }
            else ImGui.TextDisabled("—");
            ImGui.TableNextColumn();
            if(travel.Available)
            {
                ImGui.BeginDisabled(dead || !r.Row.HasPosition || travel.Busy);
                if(ImGui.SmallButton("Teleport")) travel.Start(m.WorldId,m.TerritoryId,new(m.X,m.Y));
                ImGui.EndDisabled();
                if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(dead ? "This mark is dead." : !r.Row.HasPosition ? "Location not reported yet." : "Travel to this world and nearest eligible aetheryte. Select the instance on arrival.");
            }
            Cell(r.Row.Visible is { } observation ? string.Join(", ",observation.Observers) : "Faloop report");
            ImGui.PopID();
        }
        ImGui.EndTable();
    }
    private static void Cell(string value) { ImGui.TableNextColumn(); ImGui.TextUnformatted(value); }
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
