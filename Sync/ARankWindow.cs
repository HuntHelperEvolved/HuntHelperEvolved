using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

namespace HuntHelperEvolved.Sync;

public sealed class ARankWindow
{
    private readonly Configuration _config;
    private readonly SyncCoordinator _sync;
    private readonly WorldData _worldData;
    private readonly MarkDetector _detector;
    private readonly LifestreamTravel _travel;
    private readonly IGameGui _gameGui;
    private DateTime _nextCapture;
    private readonly BoardSnapshot<List<Row>> _board = new();
    private sealed record Row(uint NameId, uint World, uint Instance, MarkInfo Info, ARankKill? Kill,
        DateTime? Opens, DateTime? Ends, bool Up, DateTime? SeenAliveAt, bool AfterMaintenance, int State, double Percent,
        uint TerritoryId, ARankLocation? Location);
    private static readonly KeyValuePair<uint, MarkInfo>[] OrderedMarks = ExpansionData.ModelIdToMark.OrderBy(e => e.Value.Order).ThenBy(e => e.Value.ZoneOrder).ToArray();
    private static readonly string[] Expansions = ExpansionData.ModelIdToMark.Values.OrderBy(m => m.Order).Select(m => m.Expansion).Distinct().ToArray();
    public ARankWindow(Configuration config, SyncCoordinator sync, WorldData worlds, MarkDetector detector,
        LifestreamTravel travel, IGameGui gameGui)
    { _config = config; _sync = sync; _worldData = worlds; _detector = detector; _travel = travel; _gameGui = gameGui; }
    public void Toggle() { _board.Invalidate(); _config.ARankWindowOpen = !_config.ARankWindowOpen; _config.DeferWindowStateSave(); }
    public void OnSettingsReset() => _board.Invalidate();

    public void Draw()
    {
        var now = DateTime.UtcNow;
        // Capture while closed too, so clearing the train does not erase its known kill times.
        if (now >= _nextCapture)
        {
            _nextCapture = now.AddSeconds(1);
            var captured = new List<ARankKill>();
            var sightings = new List<ARankSighting>();
            foreach (var mark in _detector.Marks.Values)
            {
                if (mark.IsCustom || ExpansionData.Lookup(mark.NameId) is null) continue;
                if (!mark.Dead)
                {
                    sightings.Add(new() { NameId = mark.NameId, WorldId = mark.WorldId,
                        Instance = mark.Instance, At = mark.LastSeenUtc, Alive = true });
                    continue;
                }
                var at = mark.SnipedAtUtc ?? mark.DeathObservedAtUtc;
                if (at is null || mark.WorldId == 0 || now - at.Value > TimeSpan.FromDays(14)) continue;
                captured.Add(new ARankKill { NameId = mark.NameId,
                    WorldId = mark.WorldId, Instance = mark.Instance, At = at.Value, LastAliveAt = mark.SnipedAtUtc is not null ? mark.LastSeenUtc : null, Uncertain = mark.SnipedAtUtc is not null });
            }
            _sync.RememberARankSightings(sightings);
            if (ARankHistory.Merge(_config.ARankKills, captured, now)) _config.Save();
        }
        if (!_config.ARankWindowOpen) { _board.Invalidate(); return; }
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(1040,520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520,240),new Vector2(float.MaxValue,float.MaxValue));
        if (ImGui.Begin("A Ranks", ref open)) DrawContents(now);
        ImGui.End();
        if (!open) { _config.ARankWindowOpen = false; _config.DeferWindowStateSave(); }
    }
    private void DrawContents(DateTime now)
    {
        var worlds = DrawWorldPicker();
        ImGui.SameLine(); DrawExpansionFilter();
        var available = _config.ARankWindowAvailableOnly;
        if (ImGui.Checkbox("Available to spawn only", ref available)) { _config.ARankWindowAvailableOnly = available; _config.Save(); }
        ImGui.SameLine(); var search = _config.ARankWindowSearch; ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("##asearch", "Search mark or zone", ref search,100)) { _config.ARankWindowSearch = search; _config.Save(); }
        var rows = _board.Get(worlds, _config.ARankWindowExpansions, search, available, _sync.IsConnected,
            System.Diagnostics.Stopwatch.GetTimestamp(), () => BuildRows(worlds, search, available, now));
        if (!string.IsNullOrEmpty(_travel.Status)) ImGui.TextWrapped(_travel.Status);
        var showInstances = rows.Any(row => row.Instance > 0);
        if (!ImGui.BeginTable("aranksUnified",10,TimerTableUi.Flags)) return;
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
        ImGui.TableSetupColumn("Last known location",ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort,
            ImGui.CalcTextSize("Last known location").X);
        ImGui.TableHeadersRow();
        rows=TimerTableUi.Sort(rows,(row,column)=>column switch
        {
            0=>row.Info.Name, 1=>_worldData.NameOf(row.World), 2=>row.Instance,
            3=>row.Info.Location, 4=>row.Info.Order, 5=>(row.State,-row.Percent),
            6=>row.Opens, 7=>row.Ends, 8=>row.Kill?.At, _=>null
        });
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(rows.Count);
            while (clipper.Step())
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    DrawRow(rows[i], now);
        }
        finally { clipper.Destroy(); }
        ImGui.EndTable();
    }
    private List<Row> BuildRows(List<uint> worlds, string search, bool available, DateTime now)
    {
        var selectedWorlds = worlds.ToHashSet();
        var killsByMark = _config.ARankKills.Where(k => selectedWorlds.Contains(k.WorldId)).ToLookup(k => (k.WorldId, k.NameId));
        var sightings = _config.ARankSightings.Where(s => selectedWorlds.Contains(s.WorldId))
            .ToDictionary(s => (s.NameId, s.WorldId, s.Instance));
        var locations = _config.ARankLocations.Where(l => selectedWorlds.Contains(l.WorldId) && ARankLocations.IsValid(l, now))
            .ToDictionary(l => (l.NameId, l.WorldId, l.Instance));
        var restarts = _sync.SRankStatuses.Values.Where(s => s.Maintenance && selectedWorlds.Contains(s.WorldId))
            .GroupBy(s => s.WorldId).ToDictionary(g => g.Key, g => g.Max(s => s.KilledAt));
        var zoneInstances = new ARankZoneInstances();
        foreach (var mark in _detector.Marks.Values)
            if (selectedWorlds.Contains(mark.WorldId)) zoneInstances.Add(mark.NameId, 0, mark.WorldId, mark.Instance);
        foreach (var kill in _config.ARankKills)
            if (selectedWorlds.Contains(kill.WorldId)) zoneInstances.Add(kill.NameId, 0, kill.WorldId, kill.Instance);
        foreach (var sighting in sightings.Values)
            zoneInstances.Add(sighting.NameId, 0, sighting.WorldId, sighting.Instance);
        foreach (var location in locations.Values)
            zoneInstances.Add(location.NameId, location.TerritoryId, location.WorldId, location.Instance);
        foreach (var sighting in _detector.OtherRanks.Values)
            if (selectedWorlds.Contains(sighting.WorldId)) zoneInstances.Add(sighting.NameId, sighting.TerritoryId, sighting.WorldId, sighting.Instance);
        foreach (var sighting in _sync.RemoteSightings.Values)
            if (selectedWorlds.Contains(sighting.WorldId)) zoneInstances.Add(sighting.NameId, sighting.TerritoryId, sighting.WorldId, sighting.Instance);
        foreach (var status in _sync.SRankStatuses.Values)
            if (selectedWorlds.Contains(status.WorldId)) zoneInstances.Add(0, status.TerritoryId, status.WorldId, status.Instance);
        var currentWorld = _detector.CurrentWorldId();
        if (selectedWorlds.Contains(currentWorld))
            zoneInstances.Add(0, _detector.CurrentTerritoryId, currentWorld, MarkDetector.GetCurrentInstance());
        var rows = new List<Row>();
        foreach (var world in worlds)
        foreach (var entry in OrderedMarks)
        {
            var info = entry.Value;
            if (!_config.ARankWindowExpansions.Contains(info.Expansion) || (!string.IsNullOrWhiteSpace(search) && !(info.Name+" "+info.Location).Contains(search,StringComparison.OrdinalIgnoreCase))) continue;
            var kills = killsByMark[(world, entry.Key)];
            // A sighting of either mark supplies instances for both marks in its zone.
            // Keep an uninstanced archived location separate when numbered instances are later discovered.
            var historyInstances = kills.Select(k => k.Instance);
            if (locations.ContainsKey((entry.Key, world, 0))) historyInstances = historyInstances.Append(0u);
            var instances = ARankInstances.Resolve(zoneInstances.Get(world, info.Location), historyInstances);
            var territory = ARankZoneInstances.ZoneTerritories.TryGetValue(info.Location, out var territories) ? territories[0] : 0;
            instances = _sync.Faloop.CurrentInstancesInPlace(territory, instances);
            foreach (var instance in instances)
            {
                var kill = kills.FirstOrDefault(k => k.Instance == instance);
                var sighting = sightings.GetValueOrDefault((entry.Key, world, instance));
                var restart = restarts.GetValueOrDefault(world);
                var up = ARankSightings.IsSpawned(sighting, kill, restart, now);
                var (opens, end) = ARankHistory.Window(kill, info.MinHours, info.MaxHours, restart);
                if (up) { opens = null; end = null; }
                var known = opens is not null;
                if (available && (_sync.Faloop.IsOffline(_worldData.NameOf(world)) || up || opens is null || now < opens)) continue;
                var afterMaintenance=restart is not null && (kill is null || kill.At <= restart || kill.LastAliveAt <= restart);
                var state=up ? 0 : !known ? 5 : now >= end ? 1 : now >= opens ? 2 : 4;
                var percent = (ARankSpawnProgress.Fraction(up, opens, end, now) ?? 0) * 100;
                var location = locations.GetValueOrDefault((entry.Key, world, instance));
                rows.Add(new(entry.Key,world,instance,info,kill,opens,end,up,sighting?.At,afterMaintenance,state,percent,
                    territory,location));
            }
        }
        return rows.OrderBy(r=>r.State).ThenByDescending(r=>r.Percent).ThenBy(r=>r.Opens??DateTime.MaxValue)
            .ThenBy(r=>r.Info.Name).ThenBy(r=>r.World).ThenBy(r=>r.Instance).ToList();
    }

    private void DrawRow(Row row,DateTime now)
    {
        ImGui.PushID($"{row.World}_{row.NameId}_{row.Instance}");
        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetTextLineHeight() + 2 * ImGui.GetStyle().CellPadding.Y);
        ImGui.TableNextColumn();
        var offline = _sync.Faloop.IsOffline(_worldData.NameOf(row.World));
        ImGui.TextColored(!offline && (row.Up || row.Opens <= now) ? TimerTableUi.Up : TimerTableUi.Cooldown,
            row.Info.Name+ExpansionData.InstanceGlyph(row.Instance));
        if (offline) TimerTableUi.StrikeLastItem();
        if (ImGui.IsItemHovered() && ImGui.GetIO().KeyCtrl)
        {
            var position = TravelPosition(row);
            var destination = TeleportHelper.NearestTo(row.TerritoryId, position);
            var travelHint = offline ? "Travel unavailable while this world is offline."
                : !_travel.Available ? "Enable Lifestream for Ctrl-click travel."
                : _travel.Busy ? "Lifestream is already travelling."
                : destination is null ? "No eligible aetheryte. Check Settings > Travel."
                : $"Ctrl-click to travel to {destination.Value.Name} on {_worldData.NameOf(row.World)}"
                    + (row.Instance == 0 ? "." : $", instance {row.Instance}.");
            ImGui.SetTooltip(travelHint);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && destination is not null && _travel.Available && !_travel.Busy && !offline)
                _travel.Start(row.World, row.TerritoryId, position, row.Instance);
        }
        ImGui.TableNextColumn();ImGui.TextDisabled(_worldData.NameOf(row.World));
        ImGui.TableNextColumn();ImGui.TextDisabled(row.Instance==0 ? "" : $"I{row.Instance}");
        ImGui.TableNextColumn();ImGui.TextDisabled(row.Info.Location);
        ImGui.TableNextColumn();ImGui.TextDisabled(row.Info.Expansion);
        ImGui.TableNextColumn();
        if(offline) ImGui.TextDisabled("OFFLINE / MAINTENANCE");
        else if(row.Up) ImGui.TextColored(TimerTableUi.Up, "100% spawned");
        else if(row.Opens is null) ImGui.TextColored(TimerTableUi.Unknown,
            row.AfterMaintenance ? "after maintenance / unknown" : row.Kill?.Uncertain==true ? "sniped / unknown" : "no kill recorded");
        else if(now < row.Opens) ImGui.TextColored(TimerTableUi.Cooldown,"opens in "+TimerTableUi.Duration(row.Opens.Value-now));
        else if(now >= row.Ends) ImGui.TextColored(TimerTableUi.Up,"READY");
        else TimerTableUi.Progress(row.Percent);
        if (row.Up && ImGui.IsItemHovered()) ImGui.SetTooltip($"Seen alive {Time(row.SeenAliveAt)}.");
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
        ImGui.TableNextColumn();
        DrawLastLocation(row);
        ImGui.PopID();
    }

    private Vector2 TravelPosition(Row row)
    {
        if (row.Location is { } location) return new(location.X, location.Y);
        // A zone destination is still available before any train has recorded this mark.
        return new(21.5f, 21.5f);
    }

    private void DrawLastLocation(Row row)
    {
        var location = row.Location;
        ImGui.BeginDisabled(location is null);
        var label = location is null ? "Unavailable" : $"Flag ({location.X:F1}, {location.Y:F1})";
        if (ImGui.Selectable(label + "##lastLocation", false) && location is not null)
            MapFlagHelper.FlagPosition(_gameGui, location.TerritoryId, location.MapId, location.Instance, location.X, location.Y);
        ImGui.EndDisabled();
        if (location is not null && ImGui.IsItemHovered())
            ImGui.SetTooltip($"Last seen: {location.SeenAt.ToLocalTime():yyyy-MM-dd HH:mm}.");
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
