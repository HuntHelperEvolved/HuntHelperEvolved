using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;

namespace HuntHelperEvolved.Sync;

public sealed class ARankWindow
{
    public Action? OpenConnectionSettings { get; set; }
    private readonly Configuration _config;
    private readonly SyncCoordinator _sync;
    private readonly WorldData _worldData;
    private readonly MarkDetector _detector;
    private readonly LifestreamTravel _travel;
    private readonly IGameGui _gameGui;
    private DateTime _nextCapture;
    private bool _focusWindow;
    private readonly BoardSnapshot<List<Row>> _board = new();
    private readonly BoardSnapshot<List<Row>> _workspaceBoard = new();
    private ViewState? _workspaceView;
    private sealed class ViewState
    {
        public bool CurrentWorld;
        public List<uint> Worlds = new();
        public List<string> Expansions = new();
        public bool AvailableOnly;
        public string Search = string.Empty;
    }
    private sealed record Row(uint NameId, uint World, uint Instance, MarkInfo Info, ARankKill? Kill,
        DateTime? Opens, DateTime? Ends, bool Up, DateTime? SeenAliveAt, bool AfterMaintenance, int State, double Percent,
        uint TerritoryId, ARankLocation? Location);
    private static readonly KeyValuePair<uint, MarkInfo>[] OrderedMarks = ExpansionData.ModelIdToMark.OrderBy(e => e.Value.Order).ThenBy(e => e.Value.ZoneOrder).ToArray();
    private static readonly string[] Expansions = ExpansionData.ModelIdToMark.Values.OrderBy(m => m.Order).Select(m => m.Expansion).Distinct().ToArray();
    public ARankWindow(Configuration config, SyncCoordinator sync, WorldData worlds, MarkDetector detector,
        LifestreamTravel travel, IGameGui gameGui)
    { _config = config; _sync = sync; _worldData = worlds; _detector = detector; _travel = travel; _gameGui = gameGui; }
    public void Toggle() { _board.Invalidate(); _config.ARankWindowOpen = !_config.ARankWindowOpen; _config.DeferWindowStateSave(); }
    public void OnSettingsReset()
    {
        _board.Invalidate();
        _workspaceBoard.Invalidate();
        _workspaceView = null;
    }

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
        if (!_config.ARankWindowOpen) return;
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(1040,520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520,240),new Vector2(float.MaxValue,float.MaxValue));
        if (_focusWindow) { ImGui.SetNextWindowFocus(); _focusWindow=false; }
        if (ImGui.Begin("A Ranks", ref open)) DrawContents(now);
        ImGui.End();
        if (!open) { _config.ARankWindowOpen = false; _config.DeferWindowStateSave(); }
    }
    public void DrawContents() => DrawContents(DateTime.UtcNow,
        _workspaceView ??= ReadView(), _workspaceBoard, persist: false);

    private ViewState ReadView() => new()
    {
        CurrentWorld = _config.ARankWindowCurrentWorld,
        Worlds = _config.ARankWindowWorlds.ToList(),
        Expansions = _config.ARankWindowExpansions.ToList(),
        AvailableOnly = _config.ARankWindowAvailableOnly,
        Search = _config.ARankWindowSearch
    };

    private void SaveView(ViewState view, bool persist)
    {
        if (!persist) return;
        _config.ARankWindowCurrentWorld = view.CurrentWorld;
        _config.ARankWindowWorlds = view.Worlds.ToList();
        _config.ARankWindowExpansions = view.Expansions.ToList();
        _config.ARankWindowAvailableOnly = view.AvailableOnly;
        _config.ARankWindowSearch = view.Search;
        _config.DeferWindowStateSave();
    }

    private void DrawContents(DateTime now) => DrawContents(now, ReadView(), _board, persist: true);

    private void DrawContents(DateTime now, ViewState view, BoardSnapshot<List<Row>> board, bool persist)
    {
        using var compact=HuntTheme.PushCompact();
        var worlds = DrawWorldPicker(view, persist);
        SameLineIfFits(150);
        DrawExpansionFilter(view, persist);
        SameLineIfFits(ImGui.CalcTextSize("Available only").X+ImGui.GetFrameHeight()+ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGui.Checkbox("Available only", ref view.AvailableOnly)) SaveView(view, persist);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open respawn windows only. Marks already observed up are excluded.");
        var trailingWidth=persist ? 0 : ImGui.GetFrameHeight()+ImGui.GetStyle().ItemSpacing.X;
        SameLineIfFits(120+trailingWidth);
        ImGui.SetNextItemWidth(Math.Min(220, Math.Max(1,ImGui.GetContentRegionAvail().X-trailingWidth)));
        if (ImGui.InputTextWithHint("##asearch", "Search mark or zone", ref view.Search, 100)) SaveView(view, persist);
        if (!persist)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(),ImGui.GetWindowContentRegionMax().X-ImGui.GetFrameHeight()));
            if (HuntUi.IconButton("popout",FontAwesomeIcon.ExternalLinkAlt,"Open A-rank timers (/hha)"))
            {
                if (!_config.ARankWindowOpen) Toggle();
                _focusWindow=true;
            }
        }
        var rows = board.Get(worlds, view.Expansions, view.Search, view.AvailableOnly, _sync.IsConnected,
            System.Diagnostics.Stopwatch.GetTimestamp(), () => BuildRows(worlds, view.Search, view.AvailableOnly, now, view.Expansions));
        if (!string.IsNullOrEmpty(_travel.Status)) ImGui.TextWrapped(_travel.Status);
        if (_travel.Busy && ImGui.SmallButton("Cancel travel")) _travel.Cancel();
        var multipleWorlds=worlds.Count>1;
        if (rows.Count == 0) ImGui.TextDisabled(worlds.Count == 0 ? "No worlds selected." : "No marks match these filters.");
        var footerHeight=TimerTableUi.FooterHeight;
        var tableHeight=Math.Max(ImGui.GetFrameHeight()*2,ImGui.GetContentRegionAvail().Y-footerHeight);
        if (!ImGui.BeginTable("aranksFocused", 11, TimerTableUi.Flags, new Vector2(0,tableHeight))) return;
        ImGui.TableSetupScrollFreeze(1,1);
        ImGui.TableSetupColumn("Mark / zone",ImGuiTableColumnFlags.WidthStretch,2.6f);
        ImGui.TableSetupColumn("World",ImGuiTableColumnFlags.WidthStretch | (multipleWorlds ? ImGuiTableColumnFlags.None : ImGuiTableColumnFlags.Disabled),1f);
        ImGui.TableSetupColumn("Status",ImGuiTableColumnFlags.WidthStretch,1.6f);
        ImGui.TableSetupColumn("Last kill",ImGuiTableColumnFlags.WidthStretch,1f);
        ImGui.TableSetupColumn("Actions",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort,0.8f);
        ImGui.TableSetupColumn("Expansion",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide,1f);
        ImGui.TableSetupColumn("Opens",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide,0.9f);
        ImGui.TableSetupColumn("Ready by",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide,0.9f);
        ImGui.TableSetupColumn("Last known location",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.DefaultHide,1.6f);
        ImGui.TableSetupColumn("Zone",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide,1.6f);
        ImGui.TableSetupColumn("Instance",ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultHide,0.5f);
        ImGui.TableHeadersRow();
        rows=TimerTableUi.Sort(rows,(row,column)=>column switch
        {
            0=>row.Info.Name, 1=>(_worldData.NameOf(row.World),row.Instance), 2=>(row.State,-row.Percent),
            3=>row.Kill?.At, 5=>row.Info.Order, 6=>row.Opens, 7=>row.Ends, 9=>row.Info.Location, 10=>row.Instance, _=>null
        });
        if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
            foreach (var row in rows) DrawRow(row,now);
        else
        {
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin(rows.Count);
                while (clipper.Step())
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        DrawRow(rows[i], now);
            }
            finally { clipper.Destroy(); }
        }
        ImGui.EndTable();
        var scope=worlds.Count==1 ? _worldData.NameOf(worlds[0]) : $"{worlds.Count} worlds";
        TimerTableUi.Footer("aConnection",$"{rows.Count} marks / {scope}",_config,_sync,()=>OpenConnectionSettings?.Invoke());
    }
    private List<Row> BuildRows(List<uint> worlds, string search, bool available, DateTime now, IReadOnlyCollection<string> expansions)
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
            if (!expansions.Contains(info.Expansion) || (!string.IsNullOrWhiteSpace(search) && !(info.Name+" "+info.Location).Contains(search,StringComparison.OrdinalIgnoreCase))) continue;
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
        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight()+2*ImGui.GetStyle().CellPadding.Y);
        var offline = _sync.Faloop.IsOffline(_worldData.NameOf(row.World));
        if (NextTextColumn())
        {
            var width=ImGui.GetContentRegionAvail().X;
            var name=TrainRowPresentation.FitText(row.Info.Name,width,static text=>ImGui.CalcTextSize(text).X,
                ExpansionData.InstanceGlyph(row.Instance));
            ImGui.TextColored(!offline && (row.Up || row.Opens <= now) ? TimerTableUi.Up : TimerTableUi.Cooldown,name);
            if (offline) TimerTableUi.StrikeLastItem();
            var hovered=ImGui.IsItemHovered();
            var zone=TrainRowPresentation.FitText(row.Info.Location,
                width-ImGui.CalcTextSize(name).X-ImGui.GetStyle().ItemSpacing.X,static text=>ImGui.CalcTextSize(text).X);
            if (zone.Length>0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(zone);
                hovered |= ImGui.IsItemHovered();
            }
            if (hovered)
            {
                var position = TravelPosition(row);
                var travelState=TravelState(row,offline);
                ImGui.SetTooltip($"{row.Info.Name} / {_worldData.NameOf(row.World)} / {row.Info.Location}\n{travelState.Tooltip}"
                    +(travelState.Enabled ? " Ctrl-click to travel." : string.Empty));
                if (travelState.Enabled && ImGui.GetIO().KeyCtrl && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    _travel.Start(row.World, row.TerritoryId, position, row.Instance);
            }
        }
        if (NextTextColumn())
            ImGui.TextDisabled(_worldData.NameOf(row.World)+(row.Instance==0 ? string.Empty : $" I{row.Instance}"));
        if (ImGui.TableNextColumn())
        {
            var phase=row.Up ? SRankPhase.Up : row.Opens is null
                ? !row.AfterMaintenance && row.Kill?.Uncertain==true ? SRankPhase.Uncertain : SRankPhase.Unknown
                : now<row.Opens ? SRankPhase.Cooldown : now>=row.Ends ? SRankPhase.Forced : SRankPhase.Window;
            var evidence=row.Up || row.Opens is null ? WindowEvidence(row,now)
                : row.Kill?.Uncertain==true ? $"Sniped: exact kill time unknown. Last seen alive {Time(row.Kill.LastAliveAt)}; found missing {Time(row.Kill.At)}." : null;
            TimerTableUi.Status(phase,row.Percent,row.Opens,row.Ends,now,offline,
                unknownLabel:row.AfterMaintenance ? "maintenance / unknown" : null,evidence:evidence);
        }
        if (NextTextColumn())
        {
            if(row.Kill is { } kill)
            {
                ImGui.TextUnformatted(kill.Uncertain ? "Sniped" : Time(kill.At));
                if(ImGui.IsItemHovered()) ImGui.SetTooltip(kill.Uncertain
                    ? $"Sniped: last seen alive {Time(kill.LastAliveAt)}; found missing {Time(kill.At)}."
                    : $"Killed {Time(kill.At)} / {TimerTableUi.Duration(now-kill.At)} ago.");
            }
            else ImGui.TextDisabled("—");
        }
        if (ImGui.TableNextColumn()) DrawRowActions(row,now,offline);
        if (NextTextColumn()) ImGui.TextDisabled(row.Info.Expansion);
        if (NextTextColumn()) ImGui.TextUnformatted(Time(row.Opens));
        if (NextTextColumn()) ImGui.TextUnformatted(Time(row.Ends));
        if (NextTextColumn()) DrawLastLocation(row);
        if (NextTextColumn()) ImGui.TextDisabled(row.Info.Location);
        if (NextTextColumn()) ImGui.TextDisabled(row.Instance==0 ? string.Empty : $"I{row.Instance}");
        ImGui.PopID();
    }

    private static bool NextTextColumn()
    {
        if (!ImGui.TableNextColumn()) return false;
        ImGui.AlignTextToFramePadding();
        return true;
    }

    private static string WindowEvidence(Row row,DateTime now)
    {
        if (row.Up) return $"Seen alive {Time(row.SeenAliveAt)}. Previous kill timing no longer describes this spawn.";
        if (row.Opens is null) return row.AfterMaintenance ? "Timing is unknown after maintenance."
            : row.Kill?.Uncertain==true ? $"Kill time is unknown. Last seen alive {Time(row.Kill.LastAliveAt)}; found missing {Time(row.Kill.At)}."
            : "No kill has been recorded for this world and instance.";
        return $"Opens: {Time(row.Opens)}\nReady by: {Time(row.Ends)}"
            +(row.Kill?.Uncertain==true ? $"\nSniped: exact kill time unknown. Last seen alive {Time(row.Kill.LastAliveAt)}; found missing {Time(row.Kill.At)}." : string.Empty)
            +(row.Opens<=now && now<row.Ends ? $"\n{row.Percent:F0}% of the respawn window elapsed; not spawn probability." : string.Empty);
    }

    private void DrawRowActions(Row row,DateTime now,bool offline)
    {
        var height=ImGui.GetFrameHeight();
        var gap=ImGui.GetStyle().ItemSpacing.X;
        var naturalWidth=height*3+gap*2;
        var scale=Math.Min(1,Math.Max(1,ImGui.GetContentRegionAvail().X)/naturalWidth);
        var buttonSize=new Vector2(height*scale,height);
        gap*=scale;
        var location=row.Location;
        ImGui.BeginDisabled(location is null);
        if (HuntUi.Button("flag",string.Empty,FontAwesomeIcon.MapMarkerAlt,quiet:true,size:buttonSize,
                tooltip:location is null ? "Last location unavailable"
                : $"Flag last location ({location.X:F1}, {location.Y:F1})\nSeen {Time(location.SeenAt)}") && location is not null)
            MapFlagHelper.FlagPosition(_gameGui,location.TerritoryId,location.MapId,location.Instance,location.X,location.Y);
        ImGui.EndDisabled();
        ImGui.SameLine(0,gap);
        var position=TravelPosition(row);
        var travelState=TravelState(row,offline);
        ImGui.BeginDisabled(!travelState.Enabled);
        if (HuntUi.Button("travel",string.Empty,FontAwesomeIcon.LocationArrow,quiet:true,size:buttonSize,tooltip:travelState.Tooltip))
            _travel.Start(row.World,row.TerritoryId,position,row.Instance);
        ImGui.EndDisabled();
        ImGui.SameLine(0,gap);
        if (HuntUi.Button("details",string.Empty,FontAwesomeIcon.InfoCircle,quiet:true,size:buttonSize,
                tooltip:"Timer evidence for "+row.Info.Name)) ImGui.OpenPopup("Timer evidence");
        if (ImGui.BeginPopup("Timer evidence"))
        {
            ImGui.TextUnformatted(row.Info.Name);
            ImGui.TextDisabled($"{_worldData.NameOf(row.World)} / {row.Info.Location}"
                +(row.Instance==0 ? " / uninstanced" : $" / instance {row.Instance}"));
            ImGui.Separator();
            ImGui.TextWrapped(WindowEvidence(row,now));
            if (row.Kill is { } kill)
                ImGui.TextWrapped(kill.Uncertain ? $"Found missing: {Time(kill.At)}. Exact kill time unknown."
                    : $"Last kill: {Time(kill.At)} / {TimerTableUi.Duration(now-kill.At)} ago.");
            if (location is not null)
                ImGui.TextUnformatted($"Last location: ({location.X:F1}, {location.Y:F1}) / seen {Time(location.SeenAt)}");
            else ImGui.TextDisabled("Last location unavailable");
            ImGui.EndPopup();
        }
    }

    private Vector2 TravelPosition(Row row)
    {
        if (row.Location is { } location) return new(location.X, location.Y);
        // A zone destination is still available before any train has recorded this mark.
        return new(21.5f, 21.5f);
    }

    private TravelActionState TravelState(Row row,bool offline) => TravelActionPresentation.Evaluate(
        _travel.Available,_travel.Busy,offline,false,true,
        TeleportHelper.NearestTo(row.TerritoryId,TravelPosition(row))?.Name,_worldData.NameOf(row.World),row.Instance);

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
    private static void SameLineIfFits(float width)
    {
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < width) ImGui.NewLine();
    }
    private List<uint> DrawWorldPicker(ViewState view, bool persist)
    {
        var preview=view.CurrentWorld ? _detector.CurrentWorldName()
            : view.Worlds.Count==1 ? _worldData.NameOf(view.Worlds[0]) : $"Worlds ({view.Worlds.Count})";
        ImGui.SetNextItemWidth(Math.Min(160,ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##arankWorlds",preview))
        {
            var current=view.CurrentWorld;
            if (ImGui.Checkbox("Current world",ref current)) { view.CurrentWorld=current; SaveView(view,persist); }
            ImGui.Separator();
            foreach (var dc in _worldData.DataCenters)
            {
                if (!ImGui.TreeNode(dc.Name)) continue;
                var worlds = _worldData.WorldsIn(dc.Id);
                var all = !view.CurrentWorld && worlds.All(w => view.Worlds.Contains(w.RowId));
                if (ImGui.Checkbox("All##" + dc.Id, ref all))
                {
                    foreach (var world in worlds)
                    { view.Worlds.Remove(world.RowId); if (all) view.Worlds.Add(world.RowId); }
                    view.CurrentWorld=false;
                    SaveView(view, persist);
                }
                foreach (var world in worlds)
                {
                    var selected = !view.CurrentWorld && view.Worlds.Contains(world.RowId);
                    if (ImGui.Checkbox(world.Name, ref selected))
                    { view.Worlds.Remove(world.RowId); if (selected) view.Worlds.Add(world.RowId); view.CurrentWorld=false; SaveView(view, persist); }
                }
                ImGui.TreePop();
            }
            ImGui.EndCombo();
        }
        if (view.CurrentWorld) return _detector.CurrentWorldId()==0 ? new() : new() { _detector.CurrentWorldId() };
        return view.Worlds.Distinct().Where(w => _worldData.LocateWorld(w) is not null).ToList();
    }

    private void DrawExpansionFilter(ViewState view, bool persist)
    {
        var chosen = view.Expansions!;
        ImGui.SetNextItemWidth(Math.Min(150, ImGui.GetContentRegionAvail().X));
        var preview=chosen.Count==1 ? chosen[0] : chosen.Count==Expansions.Length ? "All expansions" : $"Expansions ({chosen.Count})";
        if (!ImGui.BeginCombo("##arankExpansions", preview)) return;
        var all = Expansions.All(chosen.Contains);
        if (ImGui.Checkbox("All expansions", ref all))
        { chosen.Clear(); if (all) chosen.AddRange(Expansions); SaveView(view, persist); }
        foreach (var name in Expansions)
        {
            var selected = chosen.Contains(name);
            if (ImGui.Checkbox(name, ref selected))
            { if (selected) chosen.Add(name); else chosen.Remove(name); SaveView(view, persist); }
        }
        ImGui.EndCombo();
    }

}
