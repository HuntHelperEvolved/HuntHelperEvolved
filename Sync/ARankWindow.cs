using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelperEvolved.Sync;

public sealed class ARankKill
{
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public DateTime At { get; set; }
    public bool Uncertain { get; set; }
}

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
                var previous = _config.ARankKills.FirstOrDefault(k => k.NameId == mark.NameId && k.WorldId == mark.WorldId && k.Instance == mark.Instance);
                if (previous is not null && previous.At >= at.Value) continue;
                if (previous is not null) _config.ARankKills.Remove(previous);
                _config.ARankKills.Add(new() { NameId = mark.NameId, WorldId = mark.WorldId, Instance = mark.Instance, At = at.Value, Uncertain = mark.SnipedAtUtc is not null });
                changed = true;
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
        ImGui.TextWrapped("Windows from recorded local/shared train kills. No community A-rank kill feed. Unknown or sniped kill times have no countdown; elapsed windows do not confirm a mark is alive.");
        var worlds = DrawWorldPicker();
        ImGui.SameLine(); DrawExpansionFilter();
        var available = _config.ARankWindowAvailableOnly;
        if (ImGui.Checkbox("Open windows only", ref available)) { _config.ARankWindowAvailableOnly = available; _config.Save(); }
        ImGui.SameLine(); var search = _config.ARankWindowSearch; ImGui.SetNextItemWidth(200);
        if (ImGui.InputTextWithHint("##asearch", "Search mark or zone", ref search,100)) { _config.ARankWindowSearch = search; _config.Save(); }
        ImGui.TextDisabled("Right-click a column header to show/hide columns. Percent is elapsed window, not spawn probability.");
        if (!ImGui.BeginTable("aranks",8,ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable | ImGuiTableFlags.BordersInnerH)) return;
        ImGui.TableSetupScrollFreeze(0,1);
        foreach (var label in new[]{"Mark","World","Zone","Expansion","Status","Opens","Window end","Killed"}) ImGui.TableSetupColumn(label);
        ImGui.TableHeadersRow();
        foreach (var world in worlds)
        foreach (var entry in ExpansionData.ModelIdToMark.OrderBy(e => e.Value.Order).ThenBy(e => e.Value.ZoneOrder))
        {
            var info = entry.Value;
            if (!_config.ARankWindowExpansions.Contains(info.Expansion) || (!string.IsNullOrWhiteSpace(search) && !(info.Name+" "+info.Location).Contains(search,StringComparison.OrdinalIgnoreCase))) continue;
            var kills = _config.ARankKills.Where(k => k.NameId == entry.Key && k.WorldId == world).ToList();
            var instances = kills.Select(k => k.Instance).Concat(_detector.OtherRanks.Values.Concat(_sync.RemoteSightings.Values).Where(s => s.NameId == entry.Key && s.WorldId == world).Select(s => s.Instance)).Distinct().OrderBy(i => i).ToList();
            if (instances.Count == 0) instances.Add(0);
            foreach (var instance in instances)
            {
                var kill = kills.FirstOrDefault(k => k.Instance == instance);
                var up = _sync.IsSeenUp(entry.Key,world,instance);
                var restart = _sync.SRankStatuses.Values.Where(s => s.WorldId == world && s.Maintenance).Select(s => s.KilledAt).Max();
                var known = kill is { Uncertain:false } && (restart is null || kill.At > restart);
                DateTime? opens = known ? kill!.At.AddHours(info.MinHours) : null;
                DateTime? end = known ? kill!.At.AddHours(info.MaxHours) : null;
                if (available && (up || opens is null || now < opens)) continue;
                var text = up ? "UP" : restart is not null && (kill is null || kill.At <= restart) ? "After maintenance / unknown" : kill?.Uncertain == true ? "Sniped / unknown" : opens is null ? "No kill recorded" : now < opens ? "Cooldown" : now >= end ? "Window elapsed" : $"{Math.Clamp((now-opens.Value).TotalHours/(info.MaxHours-info.MinHours)*100,0,100):0}% window";
                ImGui.TableNextRow();
                foreach (var value in new[]{info.Name+ExpansionData.InstanceGlyph(instance),_worldData.NameOf(world),info.Location,info.Expansion,text,Time(opens),Time(end),known ? Time(kill!.At) : "—"})
                { ImGui.TableNextColumn(); ImGui.TextUnformatted(value); }
            }
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
