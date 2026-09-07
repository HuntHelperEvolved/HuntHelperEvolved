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
        ImGui.SetNextWindowSize(new Vector2(900,520), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("A Ranks", ref open)) DrawContents(now);
        ImGui.End();
        if (!open) { _config.ARankWindowOpen = false; _config.Save(); }
    }
    private void DrawContents(DateTime now)
    {
        ImGui.TextWrapped("Windows from local kills and server kill history, retrieved automatically on connection. No community A-rank kill feed. Sniped windows use last-seen-alive to found-missing bounds when known; elapsed windows do not confirm a mark is alive.");
        var worlds = DrawWorldPicker();
        ImGui.SameLine(); DrawExpansionFilter();
        var available = _config.ARankWindowAvailableOnly;
        if (ImGui.Checkbox("Open windows only", ref available)) { _config.ARankWindowAvailableOnly = available; _config.Save(); }
        ImGui.SameLine(); var search = _config.ARankWindowSearch; ImGui.SetNextItemWidth(200);
        if (ImGui.InputTextWithHint("##asearch", "Search mark or zone", ref search,100)) { _config.ARankWindowSearch = search; _config.Save(); }
        ImGui.TextWrapped("Each instance has its own kill time. Instance rows are learned from scouting in that zone; a missing kill stays unknown.");
        ImGui.TextDisabled("Right-click a column header to show/hide columns. Percent is elapsed window, not spawn probability.");
        var rows = new List<(uint Instance, string[] Values)>();
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
                var text = up ? "UP" : restart is not null && (kill is null || kill.At <= restart) ? "After maintenance / unknown" : kill?.Uncertain == true && !known ? "Sniped / unknown" : opens is null ? "No kill recorded" : now < opens ? "Cooldown" : now >= end ? "Window elapsed" : $"{Math.Clamp((now-opens.Value).TotalHours/(end!.Value-opens.Value).TotalHours*100,0,100):0}% window";
                if (known && kill!.Uncertain && !up) text += " (sniped range)";
                rows.Add((instance, new[]{info.Name+ExpansionData.InstanceGlyph(instance),_worldData.NameOf(world),instance == 0 ? "" : $"I{instance}",info.Location,info.Expansion,text,Time(opens),Time(end),known ? (kill!.Uncertain ? Time(kill.LastAliveAt) + " → " + Time(kill.At) : Time(kill.At)) : "—"}));
            }
        }
        var showInstances = rows.Any(row => row.Instance > 0);
        if (!ImGui.BeginTable("aranks",9,ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable | ImGuiTableFlags.BordersInnerH)) return;
        ImGui.TableSetupScrollFreeze(0,1);
        foreach (var label in new[]{"Mark","World","Instance","Zone","Expansion","Status","Opens","Window end","Killed"})
            ImGui.TableSetupColumn(label, label == "Instance" && !showInstances ? ImGuiTableColumnFlags.Disabled : ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            foreach (var value in row.Values)
                if (ImGui.TableNextColumn()) ImGui.TextUnformatted(value);
        }
        ImGui.EndTable();
    }
    private static string Time(DateTime? at) => at?.ToLocalTime().ToString("ddd HH:mm") ?? "—";
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
