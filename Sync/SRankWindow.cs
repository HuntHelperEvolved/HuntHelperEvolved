using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// The S-rank board: every timed S on a world, where it is in its cycle,
/// when its window opens and closes, who last saw it, and how many spawn
/// points are still possible. The in-game version of what the trackers
/// show, fed by the group's own server rather than the whole data centre.
/// </summary>
public sealed class SRankWindow
{
    private static Vector4 UpColour => HuntTheme.Success;
    private static Vector4 ForcedColour => HuntTheme.Danger;
    private static Vector4 WindowColour => HuntTheme.Warning;

    private readonly LifestreamTravel _travel;
    private readonly Configuration _config;
    private readonly SyncCoordinator _sync;
    private readonly WorldData _worldData;
    private readonly MarkDetector _detector;

    private readonly Dictionary<(uint, DateTime?), ConditionWindow?> _conditionWindows = new();
    private readonly BoardSnapshot<List<(Row Row, uint World)>> _board = new();
    private readonly BoardSnapshot<List<(Row Row, uint World)>> _workspaceBoard = new();
    private ViewState? _workspaceView;
    private sealed class ViewState
    {
        public bool CurrentWorld;
        public List<uint> Worlds = new();
        public List<string> Expansions = new();
        public bool AvailableOnly;
        public bool HideUnmetConditions;
        public string Search = string.Empty;
        public int KilledMinutesAgo;
        public int MappingSource;
        public bool OpenFilters;
    }
    private int _killedMinutesAgo;
    private int _standaloneMappingSource;
    private uint _mappingWorld;
    private uint _mappingNameId;
    private uint _mappingInstance;
    private bool _mappingWorkspaceRequested;
    private enum DetailPage { Overview, Mapping, Counters, TrainWatch }
    private DetailPage _detailPage;
    private bool _showAllCounters;
    private bool _hasSelection;
    private bool _focusWindow;
    public Action<uint, uint, float, float>? FlagMappingPoint { get; set; }
    public Action? OpenConnectionSettings { get; set; }

    public bool ConsumeMappingWorkspaceRequest()
    {
        var requested = _mappingWorkspaceRequested;
        _mappingWorkspaceRequested = false;
        return requested;
    }

    public SRankWindow(Configuration config, SyncCoordinator sync, WorldData worldData, MarkDetector detector, LifestreamTravel travel)
    {
        _travel = travel;
        _config = config;
        _sync = sync;
        _worldData = worldData;
        _detector = detector;
        if (_config.SRankWindowExpansions is null)
        {
            _config.SRankWindowExpansions = _config.SRankWindowExpansion >= 0
                ? SRankTimerData.All.Where(t => t.ExpansionOrder == _config.SRankWindowExpansion).Select(t => t.Expansion).Distinct().ToList()
                : SRankTimerData.Expansions.ToList();
            _config.Save();
        }
    }

    public bool Visible
    {
        get => _config.SRankWindowOpen;
        set
        {
            if (_config.SRankWindowOpen == value) return;
            _board.Invalidate();
            _config.SRankWindowOpen = value;
            _config.DeferWindowStateSave();
        }
    }

    public void Toggle() => Visible = !Visible;
    public void OnSettingsReset()
    {
        _board.Invalidate();
        _workspaceBoard.Invalidate();
        if (_workspaceView is { } previous)
        {
            _workspaceView = ReadView();
            _workspaceView.KilledMinutesAgo = previous.KilledMinutesAgo;
            _workspaceView.MappingSource = previous.MappingSource;
        }
    }

    public void Draw()
    {
        if (!Visible) return;

        var open = true;
        ImGui.SetNextWindowSize(new Vector2(880, 520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 240), new Vector2(float.MaxValue, float.MaxValue));
        if (_focusWindow) { ImGui.SetNextWindowFocus(); _focusWindow = false; }
        if (ImGui.Begin("S Ranks", ref open))
        {
            DrawStandaloneContents();
        }
        ImGui.End();

        if (!open) Visible = false;
    }

    public void DrawContents()
    {
        try { DrawBoard(_workspaceView ??= ReadView(), _workspaceBoard, persist: false); }
        catch (Exception ex) { ImGui.TextColored(ForcedColour, $"The S-rank board hit an error: {ex.Message}"); }
    }

    public void DrawWorkspaceContents(Action<SRankTimer, uint, uint> drawCounters,
        Action<SRankTimer, uint, uint> drawTrainWatch, Action drawAllCounters)
    {
        using var compact = HuntTheme.PushCompact();
        var view = _workspaceView ??= ReadView();
        try
        {
            if (_showAllCounters)
            {
                if (HuntUi.Button("backToSRanks", "S-ranks", FontAwesomeIcon.ArrowLeft, quiet: true)) _showAllCounters = false;
                ImGui.Separator();
                drawAllCounters();
                return;
            }

            var worlds = DrawWorkspaceToolbar(view);
            if (!_config.SyncEnabled)
                ImGui.TextDisabled("Sync off. Shared timers and mapping are not updating.");
            if (!string.IsNullOrEmpty(_travel.Status)) ImGui.TextWrapped(_travel.Status);
            if (_travel.Busy && ImGui.SmallButton("Cancel travel")) _travel.Cancel();

            var now = DateTime.UtcNow;
            var rows = _workspaceBoard.Get(worlds, view.Expansions, view.Search, view.AvailableOnly, _sync.IsConnected,
                System.Diagnostics.Stopwatch.GetTimestamp(), () => BuildBoardRows(worlds, now, view));
            if (rows.Count == 0)
            {
                ImGui.TextDisabled(worlds.Count == 0 ? "No worlds selected." : "No marks match these filters.");
                if (view.Search.Length > 0 && HuntUi.Button("clearSSearch", "Clear search", FontAwesomeIcon.Times, quiet: true))
                { view.Search = string.Empty; _workspaceBoard.Invalidate(); }
                if (WorkspaceFilterSummary(view).Length > 0 && HuntUi.Button("reviewSFilters", "Review filters", FontAwesomeIcon.Filter, quiet: true))
                    view.OpenFilters = true;
            }
            var availableHeight = Math.Max(0, ImGui.GetContentRegionAvail().Y - TimerTableUi.FooterHeight);
            var contentHeight = ImGui.GetFrameHeightWithSpacing() + rows.Count *
                (ImGui.GetFrameHeight() + ImGui.GetStyle().CellPadding.Y * 2);
            var boardHeight = Math.Min(contentHeight, _hasSelection
                ? Math.Clamp(availableHeight * 0.43f, 110, ImGui.GetFontSize() * 18)
                : Math.Max(110, availableHeight - ImGui.GetFrameHeightWithSpacing()));
            if (ImGui.BeginTable("workspaceSRanksFocused", 5, TimerTableUi.Flags,
                new Vector2(0, Math.Max(ImGui.GetFrameHeightWithSpacing(), boardHeight))))
            {
                ImGui.TableSetupScrollFreeze(1, 1);
                ImGui.TableSetupColumn("Mark / zone", ImGuiTableColumnFlags.WidthStretch, 2.6f);
                ImGui.TableSetupColumn("World", worlds.Count == 1 ? ImGuiTableColumnFlags.Disabled : ImGuiTableColumnFlags.WidthStretch, 0.85f);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Condition", ImGuiTableColumnFlags.WidthStretch, 1.3f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 0.8f);
                ImGui.TableHeadersRow();
                rows = TimerTableUi.Sort(rows, (entry, column) => SortValue(entry.Row, entry.World,
                    column switch { 2 => 3, 3 => 4, _ => column }, now));
                var clipper = ImGui.ImGuiListClipper();
                try
                {
                    clipper.Begin(rows.Count);
                    while (clipper.Step())
                        for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                            DrawWorkspaceRow(rows[i].Row, rows[i].World, now);
                }
                finally { clipper.Destroy(); }
                ImGui.EndTable();
            }
            TimerTableUi.Footer("sWorkspaceConnection", $"{rows.Count} marks", _config, _sync, () => OpenConnectionSettings?.Invoke());
            if (_hasSelection) DrawSelectedDetails(view, now, drawCounters, drawTrainWatch);
        }
        catch (Exception ex) { ImGui.TextColored(ForcedColour, $"The S-rank workspace hit an error: {ex.Message}"); }
    }

    private void Select(Row row, uint world, DetailPage page)
    {
        _mappingWorld = world;
        _mappingNameId = row.Timer.NameId;
        _mappingInstance = row.Instance;
        _detailPage = page;
        _hasSelection = true;
        _showAllCounters = false;
    }

    private List<uint> DrawWorkspaceToolbar(ViewState view)
    {
        HuntUi.FillBand(ImGui.GetFrameHeightWithSpacing(), HuntTheme.Panel);
        var worlds = DrawWorldPicker(view, persist: false);
        SameLineIfFits(ImGui.CalcTextSize("Available only").X + ImGui.GetFrameHeight());
        ImGui.BeginDisabled(!_config.SyncEnabled);
        if (ImGui.Checkbox("Available only", ref view.AvailableOnly)) _workspaceBoard.Invalidate();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(!_config.SyncEnabled
            ? "Enable sync to filter shared timers." : "Open respawn windows and marks reported up.");
        var toolsWidth = ImGui.GetFrameHeight() * 3 + ImGui.GetStyle().ItemSpacing.X * 4;
        SameLineIfFits(80 + toolsWidth);
        ImGui.SetNextItemWidth(Math.Max(80, ImGui.GetContentRegionAvail().X - toolsWidth));
        if (ImGui.InputTextWithHint("##workspaceSRankSearch", "Search marks", ref view.Search, 100))
            _workspaceBoard.Invalidate();
        ImGui.SameLine();
        var filterSummary = WorkspaceFilterSummary(view);
        if (HuntUi.IconButton("sFilters", FontAwesomeIcon.Filter,
            filterSummary.Length > 0 ? "Active filters: " + filterSummary : "S-rank filters", selected: filterSummary.Length > 0))
            ImGui.OpenPopup("sRankFilters");
        if (view.OpenFilters) { ImGui.OpenPopup("sRankFilters"); view.OpenFilters = false; }
        if (ImGui.BeginPopup("sRankFilters"))
        {
            DrawExpansionFilter(view, false);
            ImGui.BeginDisabled(!_config.SyncEnabled);
            if (ImGui.Checkbox("Hide unmet conditions", ref view.HideUnmetConditions)) _workspaceBoard.Invalidate();
            ImGui.EndDisabled();
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        if (HuntUi.IconButton("sCounters", FontAwesomeIcon.Calculator, "All stored counters")) _showAllCounters = true;
        ImGui.SameLine();
        if (HuntUi.IconButton("sPopout", FontAwesomeIcon.ExternalLinkAlt, "Open S-rank timers /hhs"))
        {
            Visible = true;
            _focusWindow = true;
        }
        ImGui.Separator();
        return worlds;
    }

    private static string WorkspaceFilterSummary(ViewState view)
    {
        var restrictions = new List<string>();
        if (view.Expansions.Count != SRankTimerData.Expansions.Length || !SRankTimerData.Expansions.All(view.Expansions.Contains))
            restrictions.Add("expansions");
        if (view.HideUnmetConditions) restrictions.Add("unmet conditions");
        return string.Join(", ", restrictions);
    }

    private void DrawWorkspaceRow(Row row, uint world, DateTime now)
    {
        ImGui.PushID($"workspace{world}_{row.Timer.NameId}_{row.Instance}");
        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().CellPadding.Y);
        var selected = _hasSelection && _mappingWorld == world && _mappingNameId == row.Timer.NameId && _mappingInstance == row.Instance;
        var offline = _sync.Faloop.IsOffline(_worldData.NameOf(world));
        DrawRowBackground(row.Window.Phase, offline, selected);
        if (NextTextColumn())
        {
            var width = ImGui.GetContentRegionAvail().X;
            var name = TrainRowPresentation.FitText(row.Timer.Name, width, static text => ImGui.CalcTextSize(text).X,
                ExpansionData.InstanceGlyph(row.Instance));
            var nameState = SRankBoardFilter.NameState(row.Window.Phase, SpawnConditionData.HasTimedCondition(row.Timer.Name), ConditionFor(row, now), now);
            ImGui.PushStyleColor(ImGuiCol.Text, offline ? HuntTheme.Muted : row.Window.Phase == SRankPhase.Up ? HuntTheme.Success : nameState switch
            {
                SRankNameState.Ready => HuntTheme.Success,
                SRankNameState.OpeningSoon => WindowColour,
                SRankNameState.ConditionsUnmet => HuntTheme.Danger,
                _ => HuntTheme.Muted
            });
            if (ImGui.Selectable(name + "##mark", selected, ImGuiSelectableFlags.None,
                new Vector2(Math.Max(1, ImGui.CalcTextSize(name).X), ImGui.GetTextLineHeight())))
            {
                if (ImGui.GetIO().KeyCtrl) TryTravel(row, world);
                else Select(row, world, DetailPage.Overview);
            }
            ImGui.PopStyleColor();
            if (offline) TimerTableUi.StrikeLastItem();
            var hovered = ImGui.IsItemHovered();
            var zone = TrainRowPresentation.FitText(row.Timer.Zone,
                width - ImGui.CalcTextSize(name).X - ImGui.GetStyle().ItemSpacing.X, static text => ImGui.CalcTextSize(text).X);
            if (zone.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(zone);
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    if (ImGui.GetIO().KeyCtrl) TryTravel(row, world);
                    else Select(row, world, DetailPage.Overview);
                }
                hovered |= ImGui.IsItemHovered();
            }
            if (hovered) ImGui.SetTooltip($"{row.Timer.Name} / {_worldData.NameOf(world)} / {row.Timer.Zone}\n{TravelHint(row, world)}");
        }
        if (NextTextColumn()) ImGui.TextDisabled(_worldData.NameOf(world) + (row.Instance == 0 ? string.Empty : $" I{row.Instance}"));
        if (ImGui.TableNextColumn()) DrawStatusCell(row, world, now);
        if (NextTextColumn()) DrawConditionCell(row, now, world);
        if (ImGui.TableNextColumn())
        {
            var height = ImGui.GetFrameHeight();
            var gap = ImGui.GetStyle().ItemSpacing.X;
            var scale = Math.Min(1, Math.Max(1, ImGui.GetContentRegionAvail().X) / (height * 3 + gap * 2));
            var buttonSize = new Vector2(height * scale, height);
            gap *= scale;
            ImGui.BeginDisabled(!_sync.IsConnected);
            if (HuntUi.Button("recordNow", string.Empty, FontAwesomeIcon.Check, quiet: true, size: buttonSize,
                tooltip: _sync.IsConnected ? "Record a kill now for this mark, world and instance." : "Connect to record a shared kill."))
                _sync.ReportManualKill(row.Timer, world, row.Instance, now);
            ImGui.EndDisabled();
            ImGui.SameLine(0, gap);
            DrawTravelButton(row, world, buttonSize);
            ImGui.SameLine(0, gap);
            var count = SpawnPointData.For(row.Timer.TerritoryId).Count(p => p.Ranks.HasFlag(SpawnRanks.S));
            ImGui.BeginDisabled(count == 0);
            if (HuntUi.Button("mapping", string.Empty, FontAwesomeIcon.MapMarkedAlt, quiet: true, size: buttonSize,
                tooltip: $"Mapping: {RemainingPoints(row, world) ?? count} / {count} possible points"))
                Select(row, world, DetailPage.Mapping);
            ImGui.EndDisabled();
        }
        ImGui.PopID();
    }

    private Vector2 TravelTarget(Row row, uint world) => TravelPosition(row, world) ?? SpawnMapping.TravelEstimate(
        SpawnPointData.For(row.Timer.TerritoryId), _sync.ZoneFor(row.Timer.TerritoryId, world, row.Instance),
        SpawnMapping.ReliableCycle(_sync.ZoneFor(row.Timer.TerritoryId, world, row.Instance), row.Status));

    private TravelActionState TravelState(Row row, uint world) => TravelActionPresentation.Evaluate(
        _travel.Available, _travel.Busy, _sync.Faloop.IsOffline(_worldData.NameOf(world)), false, true,
        TeleportHelper.NearestTo(row.Timer.TerritoryId, TravelTarget(row, world))?.Name, _worldData.NameOf(world), row.Instance);

    private bool CanTravel(Row row, uint world) => TravelState(row, world).Enabled;

    private void TryTravel(Row row, uint world)
    {
        if (CanTravel(row, world)) _travel.Start(world, row.Timer.TerritoryId, TravelTarget(row, world), row.Instance);
    }

    private string TravelHint(Row row, uint world)
    {
        var state = TravelState(row, world);
        return state.Tooltip + (state.Enabled ? " Ctrl-click the mark for the same destination." : string.Empty);
    }

    private void DrawTravelButton(Row row, uint world, Vector2 size)
    {
        var state = TravelState(row, world);
        ImGui.BeginDisabled(!state.Enabled);
        if (HuntUi.Button("travel", string.Empty, FontAwesomeIcon.LocationArrow, quiet: true, size: size, tooltip: state.Tooltip))
            TryTravel(row, world);
        ImGui.EndDisabled();
    }

    private void DrawSelectedDetails(ViewState view, DateTime now, Action<SRankTimer, uint, uint> drawCounters,
        Action<SRankTimer, uint, uint> drawTrainWatch)
    {
        var timer = SRankTimerData.All.FirstOrDefault(t => t.NameId == _mappingNameId);
        if (timer is null) { _hasSelection = false; return; }
        var status = _sync.StatusFor(timer.NameId, _mappingWorld, _mappingInstance);
        var seenUp = _sync.IsSeenUp(timer.NameId, _mappingWorld, _mappingInstance)
            || status is not null && ActiveSRankFilter.Status(status, false, now) is not null;
        var row = new Row(timer, _mappingInstance, status, SRankTimerData.Compute(timer, status, now, seenUp), seenUp);
        var edge = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(edge, edge + new Vector2(ImGui.GetContentRegionAvail().X, 0), ImGui.ColorConvertFloat4ToU32(HuntTheme.Accent), 2);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, HuntTheme.Panel);
        if (ImGui.BeginChild("Selected S-rank", new Vector2(0, Math.Max(180, ImGui.GetContentRegionAvail().Y)), false,
            ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            var start = ImGui.GetCursorPos();
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(start.X + width - ImGui.GetFrameHeight());
            if (HuntUi.IconButton("closeSelectedS", FontAwesomeIcon.Times, "Close selected mark")) _hasSelection = false;
            ImGui.SetCursorPos(start);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(timer.Name);
            var scope = $"{_worldData.NameOf(_mappingWorld)} / " + (_mappingInstance == 0 ? "uninstanced" : $"I{_mappingInstance}") + $" / {timer.Zone}";
            SameLineIfFits(ImGui.CalcTextSize(scope).X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X);
            ImGui.TextDisabled(scope);
            ImGui.TextDisabled("Kill cycle:");
            ImGui.SameLine();
            DrawKilledCell(row);
            if (!string.IsNullOrEmpty(status?.KillSource))
            {
                SameLineIfFits(120);
                ImGui.TextDisabled(status.KillSource);
            }
            var hasCounters = HuntCounter.Definitions.Any(d => d.TerritoryId == timer.TerritoryId)
                || SpawnWatchCounters.AppliesTo(timer.TerritoryId);
            var hasTrainWatch = TrainWatchPlanner.Territories.Contains(timer.TerritoryId)
                || _config.Flags.Any(f => SRankWorkspaceScope.MatchesMark(f, timer.Name, timer.TerritoryId));
            if (_detailPage == DetailPage.Counters && !hasCounters) _detailPage = DetailPage.Overview;
            if (_detailPage == DetailPage.TrainWatch && !hasTrainWatch) _detailPage = DetailPage.Overview;
            var first = true;
            foreach (var page in Enum.GetValues<DetailPage>())
            {
                if (page == DetailPage.Counters && !hasCounters || page == DetailPage.TrainWatch && !hasTrainWatch) continue;
                var label = page == DetailPage.TrainWatch ? "Train watch" : page.ToString();
                if (!first) SameLineIfFits(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2);
                if (HuntUi.UnderlineTab(label, _detailPage == page)) _detailPage = page;
                first = false;
            }
            ImGui.Separator();
            ImGui.Spacing();
            switch (_detailPage)
            {
                case DetailPage.Overview: DrawWorkspaceOverview(row, view, now); break;
                case DetailPage.Mapping:
                    DrawMappingDetails(row, _mappingWorld, new Vector2(0, Math.Max(90, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2)), ref view.MappingSource, showScope: false);
                    break;
                case DetailPage.Counters: drawCounters(timer, _mappingWorld, _mappingInstance); break;
                case DetailPage.TrainWatch: drawTrainWatch(timer, _mappingWorld, _mappingInstance); break;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawWorkspaceOverview(Row row, ViewState view, DateTime now)
    {
        var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 34 ? 2 : 1;
        if (!ImGui.BeginTable("S-rank overview", columns, ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn();
        if (ImGui.BeginTable("Cycle evidence", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5.4f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextColumn();
            ImGui.TextDisabled("State");
            ImGui.TableNextColumn();
            DrawStatusCell(row, _mappingWorld, now);
            ImGui.TableNextColumn();
            ImGui.TextDisabled("Timing");
            ImGui.TableNextColumn();
            if (row.Window.OpensAtUtc is { } opens) ImGui.TextUnformatted($"Opens: {Local(opens)}");
            if (row.Window.ForcedAtUtc is { } forced) ImGui.TextUnformatted($"Ready by: {Local(forced)}");
            if (row.Status?.Uncertain == true)
            {
                ImGui.PushTextWrapPos(0);
                ImGui.TextColored(HuntTheme.Warning, "Exact kill time unknown");
                ImGui.PopTextWrapPos();
            }
            if (row.Window.OpensAtUtc is null) ImGui.TextDisabled("No kill recorded");
            ImGui.TableNextColumn();
            ImGui.TextDisabled("Condition");
            ImGui.TableNextColumn();
            DrawConditionState(row, now, _mappingWorld);
            ImGui.TextWrapped(SpawnConditionData.Description(row.Timer.Name));
            ImGui.EndTable();
        }
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Kill record");
        ImGui.Spacing();
        ImGui.BeginDisabled(!_sync.IsConnected);
        if (HuntUi.Button("selectedKillNow", "Killed now", FontAwesomeIcon.Check))
            _sync.ReportManualKill(row.Timer, _mappingWorld, row.Instance, DateTime.UtcNow);
        ImGui.EndDisabled();
        ImGui.SetNextItemWidth(70);
        ImGui.InputInt("##selectedKillMinutes", ref view.KilledMinutesAgo, 0, 0);
        view.KilledMinutesAgo = Math.Clamp(view.KilledMinutesAgo, 0, 60 * 24 * 7);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Minutes since the kill, up to seven days. Entry alone does not submit.");
        SameLineIfFits(HuntUi.ButtonWidth("Minutes ago"));
        ImGui.BeginDisabled(!_sync.IsConnected);
        if (HuntUi.Button("selectedKillAgo", "Minutes ago"))
            _sync.ReportManualKill(row.Timer, _mappingWorld, row.Instance, DateTime.UtcNow.AddMinutes(-view.KilledMinutesAgo));
        ImGui.EndDisabled();
        if (row.Status?.KilledAt is not null)
        {
            ImGui.BeginDisabled(!_sync.IsConnected || !ImGui.GetIO().KeyShift);
            if (HuntUi.Button("selectedClearKill", "Clear", FontAwesomeIcon.Eraser,
                tooltip: "Hold Shift to clear this kill for the group.")) _sync.ClearKill(row.Timer.NameId, _mappingWorld, row.Instance);
            ImGui.EndDisabled();
        }
        if (!_sync.IsConnected) ImGui.TextDisabled("Connect to update shared kills.");
        ImGui.EndTable();
    }

    private ViewState ReadView() => new()
    {
        CurrentWorld = _config.SRankWindowCurrentWorld,
        Worlds = _config.SRankWindowWorlds.ToList(),
        Expansions = _config.SRankWindowExpansions!.ToList(),
        AvailableOnly = _config.SRankWindowAvailableOnly,
        HideUnmetConditions = _config.SRankWindowHideUnmetConditions,
        Search = _config.SRankWindowSearch,
        KilledMinutesAgo = _killedMinutesAgo,
        MappingSource = _standaloneMappingSource
    };

    private void SaveView(ViewState view, bool persist)
    {
        if (!persist) return;
        _config.SRankWindowCurrentWorld = view.CurrentWorld;
        _config.SRankWindowWorlds = view.Worlds.ToList();
        _config.SRankWindowExpansions = view.Expansions.ToList();
        _config.SRankWindowAvailableOnly = view.AvailableOnly;
        _config.SRankWindowHideUnmetConditions = view.HideUnmetConditions;
        _config.SRankWindowSearch = view.Search;
        _config.DeferWindowStateSave();
    }

    private void DrawStandaloneContents()
    {
        var view = ReadView();
        try { DrawBoard(view, _board, persist: true); }
        catch (Exception ex) { ImGui.TextColored(ForcedColour, $"The S-rank board hit an error: {ex.Message}"); }
        finally
        {
            _killedMinutesAgo = view.KilledMinutesAgo;
            _standaloneMappingSource = view.MappingSource;
        }
    }

    public void DrawMappingContents()
    {
        if (!_config.SyncEnabled)
        {
            ImGui.TextDisabled("Enable sync in Settings > Sharing.");
            return;
        }
        if (_mappingWorld == 0) _mappingWorld = _detector.CurrentWorldId();
        ImGui.SetNextItemWidth(Math.Min(240, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("World##mappingWorld", _worldData.NameOf(_mappingWorld)))
        {
            foreach (var dc in _worldData.DataCenters)
            {
                if (!ImGui.TreeNode(dc.Name)) continue;
                foreach (var world in _worldData.WorldsIn(dc.Id))
                    if (ImGui.Selectable(world.Name, world.RowId == _mappingWorld)) _mappingWorld = world.RowId;
                ImGui.TreePop();
            }
            ImGui.EndCombo();
        }
        var timers = SRankTimerData.All.Where(t => SpawnPointData.For(t.TerritoryId)
            .Any(p => p.Ranks.HasFlag(SpawnRanks.S))).ToArray();
        var timer = timers.FirstOrDefault(t => t.NameId == _mappingNameId);
        if (timer is null)
        {
            timer = timers.FirstOrDefault(t => t.TerritoryId == _detector.CurrentTerritoryId) ?? timers.FirstOrDefault();
            if (timer is null) { ImGui.TextDisabled("No S-rank spawn points available."); return; }
            _mappingNameId = timer.NameId;
        }
        ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Mark##mappingMark", $"{timer.Name} / {timer.Zone}"))
        {
            foreach (var candidate in timers)
                if (ImGui.Selectable($"{candidate.Name} / {candidate.Zone}", candidate.NameId == _mappingNameId))
                {
                    _mappingNameId = candidate.NameId;
                    timer = candidate;
                    _mappingInstance = 0;
                }
            ImGui.EndCombo();
        }
        var instances = _sync.SRankStatuses.Keys
            .Where(k => k.WorldId == _mappingWorld && k.NameId == timer.NameId)
            .Select(k => k.Instance).Distinct().ToList();
        if (_mappingWorld == _detector.CurrentWorldId() && timer.TerritoryId == _detector.CurrentTerritoryId)
            instances.Add(MarkDetector.GetCurrentInstance());
        instances = _sync.Faloop.CurrentInstancesInPlace(timer.TerritoryId, instances);
        if (instances.Count == 0) instances.Add(0);
        if (!instances.Contains(_mappingInstance)) _mappingInstance = instances.Min();
        ImGui.SetNextItemWidth(140);
        if (ImGui.BeginCombo("Instance##mappingInstance", _mappingInstance == 0 ? "Uninstanced" : _mappingInstance.ToString()))
        {
            foreach (var instance in instances.Distinct().OrderBy(i => i))
                if (ImGui.Selectable(instance == 0 ? "Uninstanced" : instance.ToString(), _mappingInstance == instance)) _mappingInstance = instance;
            ImGui.EndCombo();
        }
        var now = DateTime.UtcNow;
        var status = _sync.StatusFor(timer.NameId, _mappingWorld, _mappingInstance);
        var seenUp = _sync.IsSeenUp(timer.NameId, _mappingWorld, _mappingInstance)
            || status is not null && ActiveSRankFilter.Status(status, false, now) is not null;
        var row = new Row(timer, _mappingInstance, status, SRankTimerData.Compute(timer, status, now, seenUp), seenUp);
        ImGui.TextWrapped(status?.KilledAt is { } killed ? $"Last recorded kill: {Local(killed)}" : "Last recorded kill: unknown");
        ImGui.Separator();
        DrawMappingDetails(row, _mappingWorld, Vector2.Zero, ref _mappingSource);
    }

    private void DrawBoard(ViewState view, BoardSnapshot<List<(Row Row, uint World)>> board, bool persist)
    {
        using var compact = HuntTheme.PushCompact();
        if (!_config.SyncEnabled)
        {
            ImGui.TextDisabled("Enable sync in Settings > Sharing.");
            ImGui.Dummy(new Vector2(0, Math.Max(0, ImGui.GetContentRegionAvail().Y - TimerTableUi.FooterHeight)));
            TimerTableUi.Footer("sConnection", "Timers unavailable", _config, _sync, () => OpenConnectionSettings?.Invoke());
            return;
        }

        var worlds = DrawWorldPicker(view, persist);
        SameLineIfFits(180);
        DrawExpansionFilter(view, persist);

        SameLineIfFits(ImGui.CalcTextSize("Available only").X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGui.Checkbox("Available only", ref view.AvailableOnly)) SaveView(view, persist);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open respawn windows and marks reported up.");

        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize("Hide unmet conditions").X
            + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) ImGui.NewLine();
        if (ImGui.Checkbox("Hide unmet conditions", ref view.HideUnmetConditions))
        {
            board.Invalidate();
            SaveView(view, persist);
        }
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < 220) ImGui.NewLine();
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputTextWithHint("##srankSearch", "Search mark or zone", ref view.Search, 100)) SaveView(view, persist);
        var now = DateTime.UtcNow;
        var rows = board.Get(worlds, view.Expansions, view.Search, view.AvailableOnly, _sync.IsConnected,
            System.Diagnostics.Stopwatch.GetTimestamp(), () => BuildBoardRows(worlds, now, view));
        if (!string.IsNullOrEmpty(_travel.Status)) ImGui.TextWrapped(_travel.Status);
        if (_travel.Busy && ImGui.SmallButton("Cancel travel")) _travel.Cancel();
        if (rows.Count == 0) ImGui.TextDisabled(worlds.Count == 0 ? "No worlds selected." : "No marks match these filters.");
        var tableHeight = Math.Max(ImGui.GetFrameHeight() * 2, ImGui.GetContentRegionAvail().Y - TimerTableUi.FooterHeight);
        if (!ImGui.BeginTable("sranksConditions", 10, TimerTableUi.Flags, new Vector2(0, tableHeight))) return;

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("Mark", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Conditions", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Opens", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Ready by", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Last kill", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 4);
        ImGui.TableHeadersRow();

        rows=TimerTableUi.Sort(rows,(entry,column)=>SortValue(entry.Row,entry.World,column,now));
        // Keep submitting the owner row while a mapping popup is open.
        if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
        {
            foreach (var row in rows) DrawRow(row.Row, row.World, now, view);
        }
        else
        {
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin(rows.Count);
                while (clipper.Step())
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        DrawRow(rows[i].Row, rows[i].World, now, view);
            }
            finally { clipper.Destroy(); }
        }

        ImGui.EndTable();
        var scope = worlds.Count == 1 ? _worldData.NameOf(worlds[0]) : $"{worlds.Count} worlds";
        TimerTableUi.Footer("sConnection", $"{rows.Count} marks / {scope}", _config, _sync, () => OpenConnectionSettings?.Invoke());
    }

    // Header controls

    private static void SameLineIfFits(float width)
    {
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < width) ImGui.NewLine();
    }

    private List<uint> DrawWorldPicker(ViewState view, bool persist)
    {
        var preview = view.CurrentWorld ? _detector.CurrentWorldName()
            : view.Worlds.Count == 1 ? _worldData.NameOf(view.Worlds[0]) : $"Worlds ({view.Worlds.Count})";
        ImGui.SetNextItemWidth(Math.Min(160, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##srankWorlds", preview))
        {
            if (ImGui.Checkbox("Current world", ref view.CurrentWorld)) SaveView(view, persist);
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
                    view.CurrentWorld = false;
                    SaveView(view, persist);
                }
                foreach (var world in worlds)
                {
                    var selected = !view.CurrentWorld && view.Worlds.Contains(world.RowId);
                    if (ImGui.Checkbox(world.Name, ref selected))
                    {
                        view.Worlds.Remove(world.RowId);
                        if (selected) view.Worlds.Add(world.RowId);
                        view.CurrentWorld = false;
                        SaveView(view, persist);
                    }
                }
                ImGui.TreePop();
            }
            ImGui.EndCombo();
        }
        if (view.CurrentWorld) return _detector.CurrentWorldId() == 0 ? new() : new() { _detector.CurrentWorldId() };
        return view.Worlds.Distinct().Where(w => _worldData.LocateWorld(w) is not null).ToList();
    }

    private void DrawExpansionFilter(ViewState view, bool persist)
    {
        var chosen = view.Expansions!;
        ImGui.SetNextItemWidth(Math.Min(150, ImGui.GetContentRegionAvail().X));
        var preview = chosen.Count == 1 ? chosen[0] : chosen.Count == SRankTimerData.Expansions.Length
            ? "All expansions" : $"Expansions ({chosen.Count})";
        if (!ImGui.BeginCombo("##srankExpansions", preview)) return;
        var all = SRankTimerData.Expansions.All(chosen.Contains);
        if (ImGui.Checkbox("All expansions", ref all))
        { chosen.Clear(); if (all) chosen.AddRange(SRankTimerData.Expansions); SaveView(view, persist); }
        foreach (var name in SRankTimerData.Expansions)
        {
            var selected = chosen.Contains(name);
            if (ImGui.Checkbox(name, ref selected))
            { if (selected) chosen.Add(name); else chosen.Remove(name); SaveView(view, persist); }
        }
        ImGui.EndCombo();
    }

    // Rows

    private sealed record Row(SRankTimer Timer, uint Instance, SyncSRankStatus? Status, SRankCycle Window, bool SeenUp);

    private List<(Row Row, uint World)> BuildBoardRows(List<uint> worlds, DateTime now, ViewState view)
    {
        var selectedWorlds = worlds.ToHashSet();
        var instancesByMark = _sync.SRankStatuses.Keys.Where(k => selectedWorlds.Contains(k.WorldId))
            .ToLookup(k => (k.WorldId, k.NameId), k => k.Instance);
        return worlds.SelectMany(world => BuildRows(world, now, instancesByMark, view).Select(row => (Row:row, World:world)))
            .OrderBy(r => r.Row.Window.Phase switch { SRankPhase.Up => 0, SRankPhase.Forced => 1, SRankPhase.Window => 2, SRankPhase.Uncertain => 3, SRankPhase.Cooldown => 4, _ => 5 })
            .ThenByDescending(r => r.Row.SeenUp ? r.Row.Status?.SpawnedAt ?? now : DateTime.MinValue)
            .ThenByDescending(r => r.Row.Window.Percent).ThenBy(r => r.Row.Window.OpensAtUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Row.Timer.Name).ThenBy(r => _worldData.NameOf(r.World)).ToList();
    }

    private List<Row> BuildRows(uint worldId, DateTime now, ILookup<(uint WorldId, uint NameId), uint> instancesByMark, ViewState view)
    {
        var rows = new List<Row>();

        foreach (var timer in SRankTimerData.All)
        {
            if (!view.Expansions.Contains(timer.Expansion)) continue;
            if (!string.IsNullOrWhiteSpace(view.Search)
                && !(timer.Name + " " + timer.Zone).Contains(view.Search, StringComparison.OrdinalIgnoreCase)) continue;

            // One row per instance the server knows about, else instance 0.
            var instances = instancesByMark[(worldId, timer.NameId)]
                .OrderBy(i => i)
                .ToList();
            if (worldId == _detector.CurrentWorldId() && timer.TerritoryId == _detector.CurrentTerritoryId
                && !instances.Contains(MarkDetector.GetCurrentInstance()))
                instances.Add(MarkDetector.GetCurrentInstance());
            instances = _sync.Faloop.CurrentInstancesInPlace(timer.TerritoryId, instances);
            if (instances.Count == 0) instances.Add(0);

            foreach (var instance in instances)
            {
                var status = _sync.StatusFor(timer.NameId, worldId, instance);
                var seenUp = _sync.IsSeenUp(timer.NameId, worldId, instance);
                seenUp |= status is not null && ActiveSRankFilter.Status(status,seenUp,now) is not null;
                var cycle = SRankTimerData.Compute(timer, status, now, seenUp);
                if (_config.SyncEnabled && view.AvailableOnly && (_sync.Faloop.IsOffline(_worldData.NameOf(worldId)) || !seenUp && !SRankBoardFilter.Available(cycle.Phase))) continue;
                var row = new Row(timer, instance, status, cycle, seenUp);
                if (_config.SyncEnabled && view.HideUnmetConditions && !_sync.Faloop.IsOffline(_worldData.NameOf(worldId))
                    && !SRankBoardFilter.MatchesConditions(true, cycle.Phase, SpawnConditionData.HasTimedCondition(timer.Name), ConditionFor(row, now), now)) continue;
                rows.Add(row);
            }
        }

        // What can be acted on first: up, then forced, then the window by
        // how far along it is, then cooldowns by how soon they open, then
        // the ones nobody knows anything about.
        return rows
            .OrderBy(r => r.Window.Phase switch
            {
                SRankPhase.Up => 0,
                SRankPhase.Forced => 1,
                SRankPhase.Window => 2,
                SRankPhase.Uncertain => 3,
                SRankPhase.Cooldown => 4,
                _ => 5,
            })
            .ThenByDescending(r => r.Window.Percent)
            .ThenBy(r => r.Window.OpensAtUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Timer.ExpansionOrder)
            .ThenBy(r => r.Timer.Name)
            .ToList();
    }

    private Vector2? TravelPosition(Row row, uint world)
    {
        var key=(row.Timer.NameId,row.Instance,world);
        if (_detector.OtherRanks.TryGetValue((key.Item1,key.Item2,key.Item3,0,0),out var local) && DateTime.UtcNow-local.LastSeenUtc < TimeSpan.FromSeconds(2)) return local.MapPosition;
        if (_sync.IsSeenUp(row.Timer.NameId,world,row.Instance) && _sync.RemoteSightings.TryGetValue((key.Item1,key.Item2,key.Item3,0,0),out var remote)) return remote.MapPosition;
        if (row.SeenUp && row.Status?.SpawnX is { } x && row.Status.SpawnY is { } y
            && float.IsFinite(x) && float.IsFinite(y) && x >= 1 && x <= 100 && y >= 1 && y <= 100) return new(x,y);
        return null;
    }

    private IComparable? SortValue(Row row, uint world, int column, DateTime now)
    {
        var w=row.Window;
        return column switch
        {
            0 => row.Timer.Name,
            1 => _worldData.NameOf(world),
            2 => row.Timer.Zone,
            3 => (w.Phase switch { SRankPhase.Up=>0, SRankPhase.Forced=>1, SRankPhase.Window=>2, SRankPhase.Uncertain=>3, SRankPhase.Cooldown=>4, _=>5 },-w.Percent),
            4 => !SpawnConditionData.HasTimedCondition(row.Timer.Name) ? DateTime.MinValue : ConditionFor(row,now)?.Start,
            5 => w.OpensAtUtc,
            6 => w.ForcedAtUtc,
            7 => row.Status?.KilledAt,
            8 => RemainingPoints(row,world),
            _ => null
        };
    }
    private int? RemainingPoints(Row row, uint world)
    {
        var zone=_sync.ZoneFor(row.Timer.TerritoryId,world,row.Instance);
        if(zone is null) return null;
        var capable=SpawnPointData.For(row.Timer.TerritoryId).Select((p,i)=>(p,i))
            .Where(p=>p.p.Ranks.HasFlag(SpawnRanks.S)).ToList();
        return capable.Count==0 ? null : capable.Count(p=>!zone.IsRuledOut(p.i));
    }

    private void DrawRow(Row row, uint worldId, DateTime now, ViewState view)
    {
        var timer = row.Timer;
        var window = row.Window;
        ImGui.PushID($"{worldId}_{timer.NameId}_{row.Instance}");
        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().CellPadding.Y);
        var offline = _sync.Faloop.IsOffline(_worldData.NameOf(worldId));
        DrawRowBackground(window.Phase, offline);

        if (NextTextColumn())
        {
            var nameState=SRankBoardFilter.NameState(window.Phase, SpawnConditionData.HasTimedCondition(timer.Name), ConditionFor(row,now),now);
            var nameColour=window.Phase == SRankPhase.Up ? HuntTheme.Success : nameState switch { SRankNameState.Ready => HuntTheme.Success,
                SRankNameState.OpeningSoon => WindowColour,
                SRankNameState.ConditionsUnmet => HuntTheme.Danger, _ => HuntTheme.Muted };
            ImGui.TextColored(offline ? HuntTheme.Muted : nameColour, TrainRowPresentation.FitText(timer.Name,
                ImGui.GetContentRegionAvail().X, static text => ImGui.CalcTextSize(text).X, ExpansionData.InstanceGlyph(row.Instance)));
            if (offline) TimerTableUi.StrikeLastItem();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{timer.Name} / {_worldData.NameOf(worldId)} / {timer.Zone}\n{TravelHint(row, worldId)}");
                if (ImGui.GetIO().KeyCtrl && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) TryTravel(row, worldId);
            }
        }

        if (NextTextColumn()) ImGui.TextDisabled(_worldData.NameOf(worldId));
        if (NextTextColumn())
        {
            ImGui.TextDisabled(TrainRowPresentation.FitText(timer.Zone, ImGui.GetContentRegionAvail().X,
                static text => ImGui.CalcTextSize(text).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(timer.Zone);
        }
        if (ImGui.TableNextColumn()) DrawStatusCell(row, worldId, now);
        if (NextTextColumn()) DrawConditionCell(row, now, worldId);
        if (NextTextColumn()) ImGui.Text(window.OpensAtUtc is { } opens ? Local(opens) : "—");
        if (NextTextColumn()) ImGui.Text(window.ForcedAtUtc is { } forced ? Local(forced) : "—");
        if (NextTextColumn()) DrawKilledCell(row);
        if (ImGui.TableNextColumn()) DrawPointsCell(row, worldId, view);
        if (ImGui.TableNextColumn()) DrawRecordCell(row, worldId, view);

        ImGui.PopID();
    }

    private static void DrawRowBackground(SRankPhase phase, bool offline, bool selected = false)
    {
        if (phase == SRankPhase.Up && !offline)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(HuntTheme.ReportedUpRow));
        else if (selected)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(HuntTheme.Accent with { W = 0.12f }));
    }

    private static bool NextTextColumn()
    {
        if (!ImGui.TableNextColumn()) return false;
        ImGui.AlignTextToFramePadding();
        return true;
    }

    private ConditionWindow? ConditionFor(Row row, DateTime now)
    {
        if (!SpawnConditionData.HasTimedCondition(row.Timer.Name)) return null;
        var gate=row.Window.OpensAtUtc;
        var key=(row.Timer.NameId,gate);
        if(!_conditionWindows.TryGetValue(key,out var window) || window is null || window.Value.End<=now)
        {
            window=SpawnConditionData.Next(row.Timer.Name,gate is { } opens && opens>now ? opens : now);
            _conditionWindows[key]=window;
        }
        return window;
    }
    private void DrawConditionCell(Row row, DateTime now, uint worldId)
    {
        DrawConditionState(row, now, worldId);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(SpawnConditionData.Description(row.Timer.Name));
    }

    private void DrawConditionState(Row row, DateTime now, uint worldId)
    {
        if (_sync.Faloop.IsOffline(_worldData.NameOf(worldId))) { ImGui.TextDisabled("Offline"); return; }
        var gate=row.Window.OpensAtUtc;
        var reliable=row.Status?.KilledAt is not null && !row.Status.Uncertain;
        if(row.Window.Phase==SRankPhase.Up) { ImGui.TextDisabled("Already reported up"); return; }
        if(!SpawnConditionData.HasTimedCondition(row.Timer.Name))
        {
            if(reliable && gate is { } opens && opens>now) ImGui.TextColored(ForcedColour,"Opens in "+Countdown(opens-now));
            else ImGui.TextDisabled("No timed restriction");
            return;
        }
        var window=ConditionFor(row,now);
        if(window is not { } w) { ImGui.TextDisabled("Forecast unavailable"); return; }
        var start=gate is { } g && g>w.Start ? g : w.Start;
        if(now<start) ImGui.TextColored(SRankBoardFilter.Available(row.Window.Phase) && SRankBoardFilter.OpensSoon(start,now)
            ? WindowColour : ForcedColour,"In "+Countdown(start-now));
        else ImGui.TextColored(reliable ? UpColour : WindowColour,(reliable ? "Open: " : "Condition: ")+Countdown(w.End-now));
    }
    private static string Countdown(TimeSpan span) => $"{(int)Math.Max(0,span.TotalHours):00}:{Math.Max(0,span.Minutes):00}:{Math.Max(0,span.Seconds):00}";

    private void DrawStatusCell(Row row, uint worldId, DateTime now)
    {
        var w = row.Window;
        var hp = row.SeenUp ? LiveHp(row, worldId) : null;
        var upLabel = hp is { } h ? $"UP — {h:F0}%" : row.SeenUp ? "UP" : "REPORTED UP";
        var status = row.Status;
        var evidence = w.Phase == SRankPhase.Up
            ? status?.SpawnedAt is { } spawned ? $"Seen since {Local(spawned)}. Previous kill timing no longer describes this spawn." : null
            : status?.KilledAt is { } killed ? $"Last kill: {Local(killed)} / {status.KillSource ?? "unknown"}." : "No kill recorded.";
        if (w.Phase != SRankPhase.Up && status?.Uncertain == true)
            evidence += status.KilledAtLatest is { } latest
                ? $"\nSniped: exact kill time unknown. Estimated kill range ends {Local(latest)}."
                : "\nSniped: exact kill time unknown. Timing is only a lower bound.";
        TimerTableUi.Status(w.Phase, w.Percent, w.OpensAtUtc, w.ForcedAtUtc, now,
            _sync.Faloop.IsOffline(_worldData.NameOf(worldId)), upLabel, evidence: evidence);
    }

    private float? LiveHp(Row row, uint worldId)
    {
        var key = (row.Timer.NameId, row.Instance, worldId);
        if (_detector.OtherRanks.TryGetValue((key.Item1,key.Item2,key.Item3,0,0), out var local)) return local.HealthPercent;
        if (_sync.RemoteSightings.TryGetValue((key.Item1,key.Item2,key.Item3,0,0), out var remote)) return remote.HealthPercent;
        return null;
    }

    private void DrawKilledCell(Row row)
    {
        var status = row.Status;
        if (status?.KilledAt is not { } killed)
        {
            ImGui.TextDisabled("—");
            return;
        }

        var ago = Duration(DateTime.UtcNow - killed);
        var what = status.Maintenance ? "maintenance" : status.KillSource ?? "unknown";

        // Faloop and a member disagree by more than a few minutes: say so
        // rather than let either quietly win.
        var disagreement = status.FaloopKilledAt is { } faloopAt
                           && status.KillSource is "observed" or "manual"
                           && (faloopAt - killed).Duration() > TimeSpan.FromMinutes(5);

        var label = status.Uncertain ? "Sniped" : Local(killed);
        if (disagreement) ImGui.TextColored(ForcedColour, label + " !");
        else ImGui.TextUnformatted(label);

        if (ImGui.IsItemHovered())
        {
            var who = string.IsNullOrEmpty(status.KillReporter) ? string.Empty : $" by {status.KillReporter}";
            var tip = $"{Local(killed)} / {ago} ago — {what}{who}";
            if (status.Uncertain) tip += status.KilledAtLatest is { } latest
                ? $"\nSniped: estimated kill range ends {latest.ToLocalTime():g}."
                : "\nEarliest possible; it died unreported some time after this.";
            if (disagreement && status.FaloopKilledAt is { } f)
                tip += $"\nFaloop has {Local(f)} instead. The member's report is being used.";
            ImGui.SetTooltip(tip);
        }
    }

    private void DrawPointsCell(Row row, uint worldId, ViewState view)
    {
        var points = SpawnPointData.For(row.Timer.TerritoryId);
        var capable = new List<int>();
        for (var i = 0; i < points.Length; i++)
            if (points[i].Ranks.HasFlag(SpawnRanks.S)) capable.Add(i);

        if (capable.Count == 0)
        {
            ImGui.TextDisabled("—");
            return;
        }

        var zone = _sync.ZoneFor(row.Timer.TerritoryId, worldId, row.Instance);
        var left = capable.Count(i => zone is null || !zone.IsRuledOut(i));
        if (ImGui.SmallButton($"{left}/{capable.Count}##mapping")) ImGui.OpenPopup("mapping");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{left} of {capable.Count} possible S-rank points. Open mapping evidence for this world and instance.");
        if (ImGui.BeginPopup("mapping"))
        {
            if (ImGui.SmallButton("Open in S-ranks"))
            {
                Select(row, worldId, DetailPage.Mapping);
                _mappingSource = view.MappingSource;
                (_workspaceView ??= ReadView()).MappingSource = view.MappingSource;
                _mappingWorkspaceRequested = true;
                ImGui.CloseCurrentPopup();
            }
            DrawMappingDetails(row, worldId, new Vector2(420, 280), ref view.MappingSource);
            ImGui.EndPopup();
        }
    }
    private int _mappingSource;

    private void DrawMappingDetails(Row row, uint worldId, Vector2 size, ref int source, bool showScope = true)
    {
        if (!showScope)
        {
            DrawMappingGrid(row, worldId, size, ref source);
            return;
        }
        var points = SpawnPointData.For(row.Timer.TerritoryId);
        var zone = _sync.ZoneFor(row.Timer.TerritoryId, worldId, row.Instance);
        if (showScope)
            ImGui.TextWrapped($"{row.Timer.Name} / {_worldData.NameOf(worldId)}" + (row.Instance > 0 ? $" / instance {row.Instance}" : ""));
        var total = points.Count(p => p.Ranks.HasFlag(SpawnRanks.S));
        ImGui.TextDisabled($"{RemainingPoints(row, worldId) ?? total} / {total} possible points for this kill cycle");
        if (!_sync.SupportsManualMapping) ImGui.TextWrapped("Manual mapping requires an updated sync server.");
        ImGui.SetNextItemWidth(160);
        ImGui.Combo("Source", ref source, "Manual\0Faloop\0Bear\0Other\0");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Attribution for exclusions you enter. This does not import another service's map.");
        if (!string.IsNullOrEmpty(_sync.LastError)) ImGui.TextWrapped(_sync.LastError);
        if (ImGui.BeginChild("mappingPoints", size, true))
            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].Ranks.HasFlag(SpawnRanks.S)) continue;
                var manual = zone?.Eliminated.FirstOrDefault(e => e.Index == i && e.Rank == "Manual");
                var automatic = zone?.LastSDeathIndex == i || zone?.Eliminated.Any(e => e.Index == i && e.Rank != "Manual") == true;
                var possible = zone is null || !zone.IsRuledOut(i);
                ImGui.BeginDisabled(!_sync.IsConnected || !_sync.SupportsManualMapping || (automatic && manual is null) || zone?.SCurrentIndex == i);
                if (ImGui.Checkbox($"#{i + 1}  ({points[i].X:0.0}, {points[i].Y:0.0})", ref possible))
                    _sync.SetManualMapping(row.Timer.TerritoryId, worldId, row.Instance, i, !possible, new[] { "Manual", "Faloop", "Bear", "Other" }[source]);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(!_sync.IsConnected ? "Connect to update shared mapping."
                        : !_sync.SupportsManualMapping ? "This server does not support manual mapping."
                        : zone?.SCurrentIndex == i ? "Confirmed current S-rank location."
                        : automatic ? "Ruled out by sighting evidence. Automatic evidence is preserved."
                        : "Checked: still possible. Uncheck to share a manual exclusion for this kill cycle.");
                if (manual is not null)
                {
                    var attribution = $"{manual.Source ?? "Manual"} / {manual.Reporter ?? "Unknown"}";
                    SameLineIfFits(ImGui.CalcTextSize(attribution).X);
                    ImGui.PushTextWrapPos(0);
                    ImGui.TextDisabled(attribution);
                    ImGui.PopTextWrapPos();
                    ImGui.BeginDisabled(!_sync.IsConnected || !_sync.SupportsManualMapping);
                    if (automatic && ImGui.SmallButton($"Undo manual exclusion##{i}"))
                        _sync.SetManualMapping(row.Timer.TerritoryId, worldId, row.Instance, i, false);
                    ImGui.EndDisabled();
                }
            }
        ImGui.EndChild();
    }

    private void DrawMappingGrid(Row row, uint world, Vector2 size, ref int source)
    {
        var points = SpawnPointData.For(row.Timer.TerritoryId);
        var zone = _sync.ZoneFor(row.Timer.TerritoryId, world, row.Instance);
        var candidates = points.Select((point, index) => (Point: point, Index: index))
            .Where(p => p.Point.Ranks.HasFlag(SpawnRanks.S)).ToList();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{RemainingPoints(row, world) ?? candidates.Count} / {candidates.Count} possible points");
        SameLineIfFits(210);
        ImGui.SetNextItemWidth(145);
        ImGui.Combo("Source", ref source, "Manual\0Faloop\0Bear\0Other\0");
        source = Math.Clamp(source, 0, 3);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Attribution for manual exclusions, not an import from another service.");
        if (!_sync.SupportsManualMapping) ImGui.TextDisabled("Manual mapping requires an updated sync server.");
        if (!string.IsNullOrEmpty(_sync.LastError)) ImGui.TextWrapped(_sync.LastError);
        if (ImGui.BeginChild("Mapping candidates", size, false))
        {
            var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (ImGui.GetFontSize() * 16)), 1, 3);
            if (ImGui.BeginTable("Mapping grid", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerH))
            {
                foreach (var candidate in candidates)
                {
                    ImGui.TableNextColumn();
                    ImGui.PushID(candidate.Index);
                    var manual = zone?.Eliminated.FirstOrDefault(e => e.Index == candidate.Index && e.Rank == "Manual");
                    var evidence = zone?.Eliminated.FirstOrDefault(e => e.Index == candidate.Index && e.Rank != "Manual");
                    var previous = zone?.LastSDeathIndex == candidate.Index;
                    var current = zone?.SCurrentIndex == candidate.Index;
                    var automatic = previous || evidence is not null;
                    var possible = zone is null || !zone.IsRuledOut(candidate.Index);
                    var start = ImGui.GetCursorPos();
                    var width = ImGui.GetContentRegionAvail().X;
                    ImGui.BeginDisabled(!_sync.IsConnected || !_sync.SupportsManualMapping || automatic && manual is null || current);
                    if (ImGui.Checkbox("##possible", ref possible))
                        _sync.SetManualMapping(row.Timer.TerritoryId, world, row.Instance, candidate.Index, !possible,
                            new[] { "Manual", "Faloop", "Bear", "Other" }[source]);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(!_sync.IsConnected ? "Connect to update shared mapping."
                            : !_sync.SupportsManualMapping ? "This server does not support manual mapping."
                            : current ? "Confirmed current S-rank location."
                            : automatic ? "Automatic sighting evidence is preserved."
                            : "Checked: possible. Uncheck to share a manual exclusion for this cycle.");
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted($"#{candidate.Index + 1} / {candidate.Point.X:0.0}, {candidate.Point.Y:0.0}");
                    ImGui.SetCursorPos(new Vector2(start.X + width - ImGui.GetFrameHeight(), start.Y));
                    ImGui.BeginDisabled(FlagMappingPoint is null);
                    if (HuntUi.IconButton("flagPoint", FontAwesomeIcon.MapMarkerAlt, $"Flag point {candidate.Index + 1}"))
                        FlagMappingPoint?.Invoke(row.Timer.TerritoryId, row.Instance, candidate.Point.X, candidate.Point.Y);
                    ImGui.EndDisabled();
                    ImGui.SetCursorPos(new Vector2(start.X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X, start.Y + ImGui.GetFrameHeightWithSpacing()));
                    var reason = current ? "Current S location" : previous ? "Previous S spawn"
                        : evidence is not null ? $"{evidence.Rank}-rank sighting"
                        : manual is not null ? $"{manual.Source ?? "Manual"} exclusion" : "Possible";
                    ImGui.TextDisabled(reason);
                    if (manual is not null && ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{manual.Source ?? "Manual"} / {manual.Reporter ?? "Unknown"}");
                    if (manual is not null && automatic)
                    {
                        ImGui.BeginDisabled(!_sync.IsConnected || !_sync.SupportsManualMapping);
                        if (HuntUi.Button("undoExclusion", "Undo manual exclusion", FontAwesomeIcon.Undo, quiet: true))
                            _sync.SetManualMapping(row.Timer.TerritoryId, world, row.Instance, candidate.Index, false);
                        ImGui.EndDisabled();
                    }
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawRecordCell(Row row, uint worldId, ViewState view)
    {
        var connected = _sync.IsConnected;
        var shift = ImGui.GetIO().KeyShift;
        var height = ImGui.GetFrameHeight();
        var inputWidth = ImGui.CalcTextSize("10080").X + ImGui.GetStyle().FramePadding.X * 2;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var naturalWidth = height * 4 + inputWidth + gap * 4;
        var scale = Math.Min(1, Math.Max(1, ImGui.GetContentRegionAvail().X) / naturalWidth);
        var buttonSize = new Vector2(height * scale, height);
        gap *= scale;

        ImGui.BeginDisabled(!connected);
        if (HuntUi.Button("recordNow", string.Empty, FontAwesomeIcon.Check, quiet: true, size: buttonSize,
            tooltip: connected ? "Record a kill now for this mark, world and instance. Updates the group's timer." : "Connect to record a shared kill."))
            _sync.ReportManualKill(row.Timer, worldId, row.Instance, DateTime.UtcNow);
        ImGui.EndDisabled();

        ImGui.SameLine(0, gap);
        ImGui.SetNextItemWidth(inputWidth * scale);
        ImGui.InputInt("##ago", ref view.KilledMinutesAgo, 0, 0);
        view.KilledMinutesAgo = Math.Clamp(view.KilledMinutesAgo, 0, 60 * 24 * 7);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Minutes since the kill, up to seven days. Entry alone does not submit.");
        ImGui.SameLine(0, gap);
        ImGui.BeginDisabled(!connected);
        if (HuntUi.Button("recordMinutesAgo", string.Empty, FontAwesomeIcon.Clock, quiet: true, size: buttonSize,
            tooltip: connected ? $"Record a kill {view.KilledMinutesAgo} minutes ago for this mark, world and instance." : "Connect to record a shared kill."))
            _sync.ReportManualKill(row.Timer, worldId, row.Instance, DateTime.UtcNow.AddMinutes(-view.KilledMinutesAgo));
        ImGui.EndDisabled();

        ImGui.SameLine(0, gap);
        var hasKill = row.Status?.KilledAt is not null;
        ImGui.BeginDisabled(!connected || !shift || !hasKill);
        if (HuntUi.Button("clearRecordedKill", string.Empty, FontAwesomeIcon.Eraser, quiet: true, size: buttonSize,
            tooltip: !hasKill ? "No kill recorded to clear."
                : connected ? "Hold Shift and click to clear this kill for everyone." : "Connect to clear a shared kill."))
            _sync.ClearKill(row.Timer.NameId, worldId, row.Instance);
        ImGui.EndDisabled();
        ImGui.SameLine(0, gap);
        DrawTravelButton(row, worldId, buttonSize);
    }

    // Formatting

    private static string Local(DateTime utc) => TimerTableUi.Local(utc);
    private static string Duration(TimeSpan span) => TimerTableUi.Duration(span);
}
