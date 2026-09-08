using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelperEvolved.Sync;

public sealed class ARankWindow
{
    private readonly Configuration _config;
    private readonly SyncCoordinator _sync;
    private readonly WorldData _worldData;
    private readonly MarkDetector _detector;
    private DateTime _nextCapture;
    private sealed record Row(uint NameId, uint World, uint Instance, MarkInfo Info, ARankKill? Kill,
        DateTime? Opens, DateTime? Ends, bool Up, bool AfterMaintenance, int State, double Percent);
    private static readonly string[] Expansions = ExpansionData.ModelIdToMark.Values.OrderBy(m => m.Order).Select(m => m.Expansion).Distinct().ToArray();
    public ARankWindow(Configuration config, SyncCoordinator sync, WorldData worlds, MarkDetector detector)
    { _config = config; _sync = sync; _worldData = worlds; _detector = detector; }
    public void Toggle() { _config.ARankWindowOpen = !_config.ARankWindowOpen; _config.Save(); }

    public void Draw()
    {
        var now = DateTime.UtcNow;
        // Capture while closed too, so clearing the train does not erase its known kill times.
        if (now >= _nextCapture)
        {
            _nextCapture = now.AddSeconds(1);
            var changed = false;
            foreach (var mark in _detector.Marks.Values)
            {
                if (!mark.Dead || mark.IsCustom || ExpansionData.Lookup(mark.NameId) is null) continue;
                var at = mark.SnipedAtUtc ?? mark.DeathObservedAtUtc;
                if (at is null || mark.WorldId == 0 || now - at.Value > TimeSpan.FromDays(14)) continue;
                changed |= ARankHistory.Merge(_config.ARankKills, new[] { new ARankKill { NameId = mark.NameId,
                    WorldId = mark.WorldId, Instance = mark.Instance, At = at.Value, LastAliveAt = mark.SnipedAtUtc is not null ? mark.LastSeenUtc : null, Uncertain = mark.SnipedAtUtc is not null } }, now);
            }
            if (_config.ARankKills.RemoveAll(k => now - k.At > TimeSpan.FromDays(14)) > 0) changed = true;
            if (changed) _config.Save();
        }
        if (!_config.ARankWindowOpen) return;
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(880,520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520,240),new Vector2(float.MaxValue,float.MaxValue));
        if (ImGui.Begin("A Ranks", ref open)) DrawContents(now);
        ImGui.End();
        if (!open) { _config.ARankWindowOpen = false; _config.Save(); }
    }
    private void DrawContents(DateTime now)
    {
        ImGui.TextDisabled("Respawn windows from local and shared kill history.");
        var worlds = DrawWorldPicker();
        ImGui.SameLine(); DrawExpansionFilter();
        var available = _config.ARankWindowAvailableOnly;
        if (ImGui.Checkbox("Available to spawn only", ref available)) { _config.ARankWindowAvailableOnly = available; _config.Save(); }
        ImGui.SameLine(); var search = _config.ARankWindowSearch; ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("##asearch", "Search mark or zone", ref search,100)) { _config.ARankWindowSearch = search; _config.Save(); }
        if (ImGui.CollapsingHeader("Timer information"))
            ImGui.TextWrapped("Each world and instance has its own timer. Sniped ranges run from last seen alive to found missing; missing evidence stays unknown. Elapsed windows do not confirm a spawn. No community A-rank kill feed.");
        var rows = new List<Row>();
        foreach (var world in worlds)
        foreach (var entry in ExpansionData.ModelIdToMark.OrderBy(e => e.Value.Order).ThenBy(e => e.Value.ZoneOrder))
        {
            var info = entry.Value;
            if (!_config.ARankWindowExpansions.Contains(info.Expansion) || (!string.IsNullOrWhiteSpace(search) && !(info.Name+" "+info.Location).Contains(search,StringComparison.OrdinalIgnoreCase))) continue;
            var kills = _config.ARankKills.Where(k => k.NameId == entry.Key && k.WorldId == world).ToList();
            // Instances belong to the zone, not just a mark currently in sight.
            // Include living train rows so scouting I1/I2 creates both timers before kills.
            var zoneMarks = ExpansionData.ModelIdToMark.Where(e => e.Value.Location == info.Location)
                .Select(e => e.Key).ToHashSet();
            var zoneTerritories = SRankTimerData.All.Where(s => s.Zone == info.Location).Select(s => s.TerritoryId).ToHashSet();
            var zoneInstances = _detector.Marks.Values.Where(m => m.WorldId == world && zoneMarks.Contains(m.NameId)).Select(m => m.Instance)
                .Concat(_config.ARankKills.Where(k => k.WorldId == world && zoneMarks.Contains(k.NameId)).Select(k => k.Instance))
                .Concat(_detector.OtherRanks.Values.Concat(_sync.RemoteSightings.Values)
                    .Where(s => s.WorldId == world && (zoneMarks.Contains(s.NameId) || zoneTerritories.Contains(s.TerritoryId))).Select(s => s.Instance))
                .Concat(_sync.SRankStatuses.Values.Where(s => s.WorldId == world && zoneTerritories.Contains(s.TerritoryId)).Select(s => s.Instance));
            if (world == _detector.CurrentWorldId() && zoneTerritories.Contains(_detector.CurrentTerritoryId))
                zoneInstances = zoneInstances.Append(MarkDetector.GetCurrentInstance());
            var instances = ARankInstances.Resolve(zoneInstances, kills.Select(k => k.Instance));
            foreach (var instance in instances)
            {
                var kill = kills.FirstOrDefault(k => k.Instance == instance);
                var up = _sync.IsSeenUp(entry.Key,world,instance);
                var restart = _sync.SRankStatuses.Values.Where(s => s.WorldId == world && s.Maintenance).Select(s => s.KilledAt).Max();
                var (opens, end) = ARankHistory.Window(kill, info.MinHours, info.MaxHours, restart);
                var known = opens is not null;
                if (available && (up || opens is null || now < opens)) continue;
                var afterMaintenance=restart is not null && (kill is null || kill.At <= restart || kill.LastAliveAt <= restart);
                var state=up ? 0 : !known ? 5 : now >= end ? 1 : now >= opens ? 2 : 4;
                var percent=known ? Math.Clamp((now-opens!.Value).TotalSeconds/(end!.Value-opens.Value).TotalSeconds*100,0,100) : 0;
                rows.Add(new(entry.Key,world,instance,info,kill,opens,end,up,afterMaintenance,state,percent));
            }
        }
        rows=rows.OrderBy(r=>r.State).ThenByDescending(r=>r.Percent).ThenBy(r=>r.Opens??DateTime.MaxValue)
            .ThenBy(r=>r.Info.Name).ThenBy(r=>r.World).ThenBy(r=>r.Instance).ToList();
        ImGui.TextDisabled($"{rows.Count} marks across {worlds.Count} selected worlds.");
        ImGui.TextDisabled("Headers: click to sort, right-click for columns. A third click restores automatic order.");
        var showInstances = rows.Any(row => row.Instance > 0);
        if (!ImGui.BeginTable("aranksUnified",9,TimerTableUi.Flags)) return;
        ImGui.TableSetupScrollFreeze(0,1);
        ImGui.TableSetupColumn("Mark",ImGuiTableColumnFlags.WidthStretch,1.6f);
        ImGui.TableSetupColumn("World",ImGuiTableColumnFlags.WidthStretch,1.1f);
        ImGui.TableSetupColumn("Instance",ImGuiTableColumnFlags.WidthStretch | (showInstances ? ImGuiTableColumnFlags.None : ImGuiTableColumnFlags.Disabled),0.5f);
        ImGui.TableSetupColumn("Zone",ImGuiTableColumnFlags.WidthStretch,1.6f);
        ImGui.TableSetupColumn("Expansion",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide,1f);
        ImGui.TableSetupColumn("Status",ImGuiTableColumnFlags.WidthStretch,1.4f);
        ImGui.TableSetupColumn("Opens",ImGuiTableColumnFlags.WidthStretch,0.9f);
        ImGui.TableSetupColumn("Ready by",ImGuiTableColumnFlags.WidthStretch,0.9f);
        ImGui.TableSetupColumn("Killed",ImGuiTableColumnFlags.WidthStretch,1.5f);
        ImGui.TableHeadersRow();
        rows=TimerTableUi.Sort(rows,(row,column)=>column switch
        {
            0=>row.Info.Name, 1=>_worldData.NameOf(row.World), 2=>row.Instance,
            3=>row.Info.Location, 4=>row.Info.Order, 5=>(row.State,-row.Percent),
            6=>row.Opens, 7=>row.Ends, 8=>row.Kill?.At, _=>null
        });
        foreach (var row in rows) DrawRow(row,now);
        ImGui.EndTable();
    }
    private void DrawRow(Row row,DateTime now)
    {
        ImGui.PushID($"{row.World}_{row.NameId}_{row.Instance}");
        ImGui.TableNextRow(); // A-rank UP rows intentionally keep the normal alternating background.
        ImGui.TableNextColumn();
        ImGui.TextColored(row.Up || row.Opens <= now ? TimerTableUi.Up : TimerTableUi.Cooldown,
            row.Info.Name+ExpansionData.InstanceGlyph(row.Instance));
        if(ImGui.IsItemHovered()) ImGui.SetTooltip($"{row.Info.Expansion} · {row.Info.Location}\nRespawn range: {row.Info.MinHours:0.#}–{row.Info.MaxHours:0.#} hours after death.");
        ImGui.TableNextColumn();ImGui.TextDisabled(_worldData.NameOf(row.World));
        ImGui.TableNextColumn();ImGui.TextDisabled(row.Instance==0 ? "" : $"I{row.Instance}");
        ImGui.TableNextColumn();ImGui.TextDisabled(row.Info.Location);
        ImGui.TableNextColumn();ImGui.TextDisabled(row.Info.Expansion);
        ImGui.TableNextColumn();
        if(row.Up) ImGui.TextColored(TimerTableUi.Up,"UP");
        else if(row.Opens is null) ImGui.TextColored(TimerTableUi.Unknown,
            row.AfterMaintenance ? "after maintenance / unknown" : row.Kill?.Uncertain==true ? "sniped / unknown" : "no kill recorded");
        else if(now < row.Opens) ImGui.TextColored(TimerTableUi.Cooldown,"opens in "+TimerTableUi.Duration(row.Opens.Value-now));
        else if(now >= row.Ends) ImGui.TextColored(TimerTableUi.Up,"READY");
        else TimerTableUi.Progress(row.Percent);
        if(ImGui.IsItemHovered()) ImGui.SetTooltip(row.Up ? "Currently reported alive." :
            (row.Kill?.Uncertain==true ? "Sniped: bounded by last seen alive and found missing. " : "")+
            "Elapsed portion of the respawn window, not a spawn probability or confirmation that the mark is alive.");
        ImGui.TableNextColumn();ImGui.TextUnformatted(Time(row.Opens));
        ImGui.TableNextColumn();ImGui.TextUnformatted(Time(row.Ends));
        ImGui.TableNextColumn();
        if(row.Kill is { } kill)
        {
            ImGui.TextUnformatted(TimerTableUi.Duration(now-kill.At)+" ago"+(kill.Uncertain ? " ~" : ""));
            if(ImGui.IsItemHovered()) ImGui.SetTooltip(kill.Uncertain
                ? $"Sniped: last seen alive {Time(kill.LastAliveAt)}; found missing {Time(kill.At)}."
                : "Killed "+Time(kill.At));
        }
        else ImGui.TextDisabled("—");
        ImGui.PopID();
    }
    private static string Time(DateTime? at) => at is { } time ? TimerTableUi.Local(time) : "—";
    private List<uint> DrawWorldPicker()
    {
        var current = _config.ARankWindowCurrentWorld;
        if (ImGui.Checkbox("Current world", ref current)) { _config.ARankWindowCurrentWorld = current; _config.Save(); }
        if (current) { ImGui.SameLine(); ImGui.TextDisabled(_detector.CurrentWorldName()); return _detector.CurrentWorldId() == 0 ? new() : new() { _detector.CurrentWorldId() }; }
        ImGui.SameLine(); ImGui.SetNextItemWidth(190);
        if (ImGui.BeginCombo("##arankWorlds", $"Worlds ({_config.ARankWindowWorlds.Count})"))
        {
            foreach (var dc in _worldData.DataCenters)
            {
                if (!ImGui.TreeNode(dc.Name)) continue;
                var worlds = _worldData.WorldsIn(dc.Id);
                var all = worlds.All(w => _config.ARankWindowWorlds.Contains(w.RowId));
                if (ImGui.Checkbox("All##" + dc.Id, ref all))
                {
                    foreach (var world in worlds)
                    { _config.ARankWindowWorlds.Remove(world.RowId); if (all) _config.ARankWindowWorlds.Add(world.RowId); }
                    _config.Save();
                }
                foreach (var world in worlds)
                {
                    var selected = _config.ARankWindowWorlds.Contains(world.RowId);
                    if (ImGui.Checkbox(world.Name, ref selected))
                    { if (selected) _config.ARankWindowWorlds.Add(world.RowId); else _config.ARankWindowWorlds.Remove(world.RowId); _config.Save(); }
                }
                ImGui.TreePop();
            }
            ImGui.EndCombo();
        }
        return _config.ARankWindowWorlds.Distinct().Where(w => _worldData.LocateWorld(w) is not null).ToList();
    }

    private void DrawExpansionFilter()
    {
        var chosen = _config.ARankWindowExpansions!;
        ImGui.SetNextItemWidth(180);
        if (!ImGui.BeginCombo("##arankExpansions", $"Expansions ({chosen.Count})")) return;
        var all = Expansions.All(chosen.Contains);
        if (ImGui.Checkbox("All expansions", ref all))
        { chosen.Clear(); if (all) chosen.AddRange(Expansions); _config.Save(); }
        foreach (var name in Expansions)
        {
            var selected = chosen.Contains(name);
            if (ImGui.Checkbox(name, ref selected))
            { if (selected) chosen.Add(name); else chosen.Remove(name); _config.Save(); }
        }
        ImGui.EndCombo();
    }

}
