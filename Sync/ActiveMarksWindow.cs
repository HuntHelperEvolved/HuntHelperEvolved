using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace HuntHelperEvolved.Sync;

public sealed class ActiveMarksWindow(Configuration config, SyncCoordinator sync, WorldData worlds,
    MarkDetector detector, IGameGui gameGui, LifestreamTravel travel, Action openSettings)
{
    public Action? OpenConnectionSettings { get; set; }
    private readonly ActiveMarkGrace _localGrace = new();
    private static readonly string[] RankTabs = { "All", "S", "A", "B" };
    private (uint Zone, uint World, uint Instance) _localScope;
    private sealed class ViewState
    {
        public string Search = string.Empty;
        public string RankTab = "All";
        public bool OpenFilters;
    }
    private ViewState _compactView = new();
    private ViewState _workspaceView = new();
    private bool _focusWindow;
    public void Toggle() { config.ActiveSRankWindowOpen = !config.ActiveSRankWindowOpen; config.DeferWindowStateSave(); }
    public void OnSettingsReset() { _compactView = new(); _workspaceView = new(); }
    public void Draw()
    {
        if (!config.ActiveSRankWindowOpen) return;
        var open=true;
        ImGui.SetNextWindowSize(new Vector2(430,260),ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(300,150),new Vector2(float.MaxValue));
        if (_focusWindow) { ImGui.SetNextWindowFocus(); _focusWindow=false; }
        if (ImGui.Begin("Active Marks###ActiveMarksCompact",ref open))
            DrawContents(compactWindow:true);
        ImGui.End();
        if (!open) { config.ActiveSRankWindowOpen=false; config.DeferWindowStateSave(); }
    }

    public void DrawContents(bool compactWindow = false)
    {
        using var compact = HuntTheme.PushCompact();
        var view = compactWindow ? _compactView : _workspaceView;
        ImGui.PushID("activeMarkContents");
        if (!sync.IsConnected) _localGrace.Clear();
        ImGui.BeginGroup();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(1,ImGui.GetStyle().ItemSpacing.Y));
        foreach (var tab in RankTabs)
        {
            if (tab != RankTabs[0]) ImGui.SameLine();
            if (HuntUi.Button("rank"+tab,tab,selected:view.RankTab==tab,quiet:true,
                    size:new Vector2(Math.Max(ImGui.GetFrameHeight(),HuntUi.ButtonWidth(tab)),ImGui.GetFrameHeight())))
                view.RankTab=tab;
        }
        ImGui.PopStyleVar();
        ImGui.EndGroup();
        var filterWidth = ImGui.GetFrameHeight();
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var trailingWidth = filterWidth+(compactWindow ? 0 : filterWidth+gap);
        SameLineIfFits(ImGui.GetFontSize()*3+trailingWidth+gap);
        var searchWidth=Math.Max(1,ImGui.GetContentRegionAvail().X-trailingWidth-gap);
        ImGui.SetNextItemWidth(compactWindow ? searchWidth : Math.Min(220*ImGuiHelpers.GlobalScale,searchWidth));
        ImGui.InputTextWithHint("##activeSearch",compactWindow ? "Search" : "Search marks",ref view.Search,100);
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(),ImGui.GetWindowContentRegionMax().X-trailingWidth));
        var filterSummary = ActiveMarkPresentation.FilterSummary(config.VisibleMarkFilters);
        if (filterSummary.Length > 0) ImGui.PushStyleColor(ImGuiCol.Text,HuntTheme.Accent);
        if (IconButton(FontAwesomeIcon.Filter, filterSummary.Length > 0
                ? "Active filters: "+filterSummary : "Active Marks filters")) ImGui.OpenPopup("ActiveMarkFilters");
        if (filterSummary.Length > 0) ImGui.PopStyleColor();
        if (!compactWindow)
        {
            ImGui.SameLine();
            if (IconButton(FontAwesomeIcon.ExternalLinkAlt,"Open Active Marks popout (/hhv)"))
            {
                if (!config.ActiveSRankWindowOpen) { config.ActiveSRankWindowOpen=true; config.DeferWindowStateSave(); }
                _focusWindow=true;
            }
        }
        if (view.OpenFilters) { ImGui.OpenPopup("ActiveMarkFilters"); view.OpenFilters = false; }
        var popupSize = Vector2.Min(new Vector2(350,420)*ImGuiHelpers.GlobalScale,
            Vector2.Max(Vector2.One,ImGui.GetMainViewport().WorkSize-new Vector2(20)*ImGuiHelpers.GlobalScale));
        ImGui.SetNextWindowSize(popupSize,ImGuiCond.Appearing);
        if (ImGui.BeginPopup("ActiveMarkFilters"))
        {
            ImGui.TextUnformatted("Active Marks filters");
            ImGui.Separator();
            if (ImGui.BeginChild("filterOptions",new Vector2(0,-ImGui.GetFrameHeightWithSpacing()),false,
                    ImGuiWindowFlags.HorizontalScrollbar)) DrawFilterOptions();
            ImGui.EndChild();
            if (ImGui.Button("All settings")) { openSettings(); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        DrawTravelStatus();
        var summary="Reports unavailable";
        if (!config.SyncEnabled || !sync.IsConnected)
        {
            ImGui.TextWrapped("Reports unavailable. " + sync.Status);
            ImGui.Dummy(new Vector2(0,Math.Max(0,ImGui.GetContentRegionAvail().Y-TimerTableUi.FooterHeight)));
        }
        else
        {
            if (!sync.SupportsVisibleMarks)
                ImGui.TextWrapped("Live health and combat state require server 0.3.11 or later.");
            var count = DrawRows(view,compactWindow);
            if (!compactWindow) DrawStatusLegend();
            summary=$"{count} marks"+(filterSummary.Length>0 ? " / filtered" : string.Empty);
        }
        TimerTableUi.Footer("activeConnection",summary,config,sync,()=>OpenConnectionSettings?.Invoke());
        ImGui.PopID();
    }

    private static bool IconButton(FontAwesomeIcon icon, string tooltip)
        => HuntUi.IconButton(icon.ToString(),icon,tooltip);

    private static void SameLineIfFits(float nextWidth)
    {
        var right = ImGui.GetWindowPos().X+ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+nextWidth <= right) ImGui.SameLine();
    }

    private void DrawTravelStatus()
    {
        if (string.IsNullOrEmpty(travel.Status) && !travel.Busy) return;
        var status = string.IsNullOrEmpty(travel.Status) ? "Travel in progress" : travel.Status;
        var reserved = travel.Busy ? ImGui.GetFrameHeight()+ImGui.GetStyle().ItemSpacing.X : 0;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(TrainRowPresentation.FitText(status,
            Math.Max(1,ImGui.GetContentRegionAvail().X-reserved),static text=>ImGui.CalcTextSize(text).X));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(status);
        if (!travel.Busy) return;
        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Times,"Cancel travel")) travel.Cancel();
    }

    private int DrawRows(ViewState view,bool compactWindow)
    {
        var tab = view.RankTab;
        var now=DateTime.UtcNow;
        var serverNow=sync.ServerTimeFor(now);
        var scope=(detector.CurrentTerritoryId,detector.CurrentWorldId(),MarkDetector.GetCurrentInstance());
        if (scope != _localScope) { _localGrace.Clear(); _localScope=scope; }
        var visible=sync.ActiveMarkDisplay.ToDictionary(v=>v.Mark.LiveKey);
        if(config.VisibleMarkFilters.IncludeOwn)
            foreach(var local in detector.VisibleMarks.Where(v=>now-v.LastSeenUtc<TimeSpan.FromSeconds(1)))
            {
                var previous=visible.GetValueOrDefault(local.LiveKey);
                _localGrace.Update(new VisibleMark
                {
                    Mark=new SyncSighting { EntityId=local.EntityId,NameId=local.NameId,WorldId=local.WorldId,Instance=local.Instance,
                        TerritoryId=local.TerritoryId,MapId=local.MapId,Name=local.Name,Rank=SyncCoordinator.SsEventMobs.Contains(local.NameId) ? "SS" : previous?.Mark.Rank??local.Rank.ToString(),
                        X=local.MapPosition.X,Y=local.MapPosition.Y,HpPercent=local.HealthPercent,NearbyPlayers=local.NearbyPlayers,InCombat=local.InCombat,SeenAt=sync.ServerTimeFor(local.LastSeenUtc) },
                    ObserverIds=new() { sync.ClientId },
                    Observers=new() { sync.DisplayName() }
                }, local.LastSeenUtc);
            }
        if (config.VisibleMarkFilters.IncludeOwn)
        {
            foreach (var local in _localGrace.Snapshot(now))
            {
                if (visible.TryGetValue(local.Mark.LiveKey, out var remote))
                    visible[local.Mark.LiveKey]=new VisibleMark {
                        Mark=ActiveMarkRows.MergeObservation(local.Mark,remote.Mark),
                        DisplayUntil=local.Mark.SeenAt>=remote.Mark.SeenAt ? local.DisplayUntil : remote.DisplayUntil,
                        ObserverIds=local.ObserverIds.Concat(remote.ObserverIds).Distinct().ToList(),
                        Observers=local.Observers.Concat(remote.Observers).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
                else visible[local.Mark.LiveKey]=local;
            }
        }
        else _localGrace.Clear();
        var rows=ActiveMarkRows.MergeBear(ActiveMarkRows.Merge(visible.Values,sync.SRankStatuses,now,serverNow),
            sync.BearMarks.Values, serverNow, sync.SRankStatuses, sync.BearPluginDeaths).Select(row =>
        {
            var dc=worlds.LocateWorld(row.Mark.WorldId) is { } loc ? worlds.DataCenters[loc.DcIndex] : default;
            var expansion=SRankTimerData.ForTerritory(row.Mark.TerritoryId)?.Expansion ?? "Unknown";
            return (Row:row,Dc:dc,Expansion:expansion,World:worlds.NameOf(row.Mark.WorldId),Zone:detector.GetZoneName(row.Mark.TerritoryId));
        }).Where(r => !ActiveMarkRows.SuppressedByFaloop(r.Row, sync.Faloop.IsOffline(r.World),
                sync.Faloop.CurrentInstances(r.Row.Mark.TerritoryId, new[] { r.Row.Mark.Instance }).Contains(r.Row.Mark.Instance)))
          .Where(r => ActiveMarkRows.MatchesTab(r.Row.Mark.Rank,tab) && ActiveMarkRows.Matches(r.Row,config.VisibleMarkFilters,sync.ClientId,now,r.Dc.Id,r.Expansion))
          .Where(r => string.IsNullOrEmpty(view.Search) || (r.Row.Mark.Name+" "+r.World+" "+r.Zone+" "+r.Dc.Name+" "+string.Join(" ",r.Row.Visible?.Observers??new List<string>())).Contains(view.Search,StringComparison.OrdinalIgnoreCase))
          .OrderBy(r => r.Row.HealthKnown && r.Row.Mark.HpPercent==0).ThenByDescending(r => r.Row.Mark.InCombat==true)
          .ThenBy(r => r.World).ThenBy(r => r.Zone).ThenBy(r => r.Row.Mark.Name).ThenBy(r => r.Row.Mark.Instance).ToList();
        var rowHeight = Math.Max(ImGui.GetFrameHeight(),ImGui.GetTextLineHeight());
        var bodyHeight = Math.Max(1,ImGui.GetContentRegionAvail().Y-TimerTableUi.FooterHeight
            -(compactWindow ? 0 : StatusLegendHeight()));
        // Keep a usable list and nearby scrollbar when scaled controls consume the window.
        if (bodyHeight < rowHeight*2)
            bodyHeight = (rowHeight+ImGui.GetStyle().ItemSpacing.Y)*Math.Clamp(rows.Count,1,2)
                +ImGui.GetStyle().ScrollbarSize+ImGui.GetStyle().WindowPadding.Y*2;
        if(ImGui.BeginChild("activeMarkLines",new Vector2(0,bodyHeight),false,ImGuiWindowFlags.HorizontalScrollbar))
        {
            var gap = ImGui.GetStyle().ItemInnerSpacing.X;
            float iconWidth;
            using (ImRaii.PushFont(UiBuilder.IconFont))
                iconWidth = new[] { FontAwesomeIcon.Rss, FontAwesomeIcon.Paw, FontAwesomeIcon.Eye, FontAwesomeIcon.Users }
                    .Max(icon=>ImGui.CalcTextSize(icon.ToIconString()).X);
            var ageWidth = Math.Max(ImGui.CalcTextSize("00:00").X,
                rows.Select(r=>ImGui.CalcTextSize(ActiveMarkPresentation.FaloopAge(r.Row.Status,serverNow)).X).DefaultIfEmpty(0).Max());
            var healthWidth = Math.Max(ImGui.CalcTextSize("100% ?").X,
                rows.Select(r=>ImGui.CalcTextSize(ActiveMarkPresentation.Health(r.Row)).X).DefaultIfEmpty(0).Max());
            var playersWidth = Math.Max(ImGui.CalcTextSize("[999]").X,
                rows.Select(r=>ImGui.CalcTextSize(ActiveMarkPresentation.Players(r.Row)).X).DefaultIfEmpty(0).Max());
            var preferredWorldWidth = rows.Select(r=>ImGui.CalcTextSize(r.World
                +(config.VisibleMarkFilters.ShowDataCenter ? " / "+r.Dc.Name : string.Empty)
                +(r.Row.Mark.Instance > 0 ? $" i{r.Row.Mark.Instance}" : string.Empty)).X).DefaultIfEmpty(0).Max();
            var minimumWorldWidth = ImGui.CalcTextSize(rows.Any(r=>r.Row.Mark.Instance>0) ? "W… i9" : "W…").X;
            var layout = ActiveMarkRowLayout.Create(ImGui.GetContentRegionAvail().X,
                iconWidth+(ageWidth>0 ? gap+ageWidth : 0),healthWidth,playersWidth,preferredWorldWidth,
                minimumWorldWidth,ImGui.CalcTextSize("M…").X,rowHeight,gap);
            DrawColumnHeadings(layout);
            if (rows.Count == 0)
            {
                ImGui.TextDisabled("No matching marks.");
                if (!string.IsNullOrEmpty(view.Search) && ImGui.SmallButton("Clear search")) view.Search=string.Empty;
                if (ActiveMarkPresentation.FilterSummary(config.VisibleMarkFilters).Length > 0
                    && ImGui.SmallButton("Review filters")) view.OpenFilters=true;
            }
            foreach(var r in rows)
            {
                var row=r.Row; var m=row.Mark; var dead=row.HealthKnown && m.HpPercent==0;
                var colour=ActiveMarkPresentation.State(row) switch
                {
                    ActiveMarkState.Community => HuntTheme.Accent,
                    ActiveMarkState.Unpulled => HuntTheme.Success,
                    ActiveMarkState.Pulled => HuntTheme.Warning,
                    ActiveMarkState.StaleHealth => HuntTheme.Muted,
                    ActiveMarkState.Dead => HuntTheme.Danger,
                    _ => HuntTheme.Muted
                };
                var instance=m.Instance>0 ? $" i{m.Instance}" : string.Empty;
                var nearby=ActiveMarkPresentation.Players(row);
                var faloopAge=ActiveMarkPresentation.FaloopAge(row.Status,serverNow);
                var stateLabel = ActiveMarkPresentation.Health(row);
                var dcLabel=config.VisibleMarkFilters.ShowDataCenter && !string.IsNullOrEmpty(r.Dc.Name) ? $" · {r.Dc.Name}" : string.Empty;
                ImGui.PushID($"{m.WorldId}:{m.Instance}:{m.NameId}:{m.TerritoryId}:{m.EntityId}");
                var start = ImGui.GetCursorPos();
                var screenStart = ImGui.GetCursorScreenPos();
                var backgroundOpacity = HuntTheme.IsLight ? .12f : .24f;
                var hoverOpacity = HuntTheme.IsLight ? .13f : .26f;
                ImGui.GetWindowDrawList().AddRectFilled(screenStart,screenStart+new Vector2(layout.Width,rowHeight),
                    ImGui.GetColorU32(colour with { W=backgroundOpacity }));
                ImGui.GetWindowDrawList().AddLine(screenStart+new Vector2(0,rowHeight),screenStart+new Vector2(layout.Width,rowHeight),
                    ImGui.GetColorU32(colour with { W=.35f }));
                var onlyOwn = row.Visible is { ObserverIds.Count: > 0 } seen && seen.ObserverIds.All(id=>id==sync.ClientId);
                var sourceIcon = (row.Bear is not null ? FontAwesomeIcon.Paw : row.Visible is null ? FontAwesomeIcon.Rss : onlyOwn ? FontAwesomeIcon.Eye : FontAwesomeIcon.Users).ToIconString();
                // Composite once so hovering retains the status tint without stacking opacity.
                var hoverBackground = Vector4.Lerp(HuntTheme.Surface,colour,hoverOpacity) with { W=1 };
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered,hoverBackground);
                ImGui.PushStyleColor(ImGuiCol.HeaderActive,hoverBackground);
                var clicked=ImGui.Selectable("##mark",false,ImGuiSelectableFlags.None,new Vector2(layout.ActionX-gap,rowHeight));
                ImGui.PopStyleColor(2);
                var hovered=ImGui.IsItemHovered();
                var destination=row.HasPosition ? TeleportHelper.NearestTo(m.TerritoryId,new(m.X,m.Y)) : null;
                var travelState=TravelActionPresentation.Evaluate(travel.Available,travel.Busy,
                    ActiveMarkRows.SuppressedByFaloop(row,sync.Faloop.IsOffline(r.World),true),
                    dead,row.HasPosition,destination?.Name,r.World,m.Instance);
                var canTravel=travelState.Enabled;
                if(clicked)
                {
                    if(ImGui.GetIO().KeyCtrl) { if(canTravel) travel.Start(m.WorldId,m.TerritoryId,new(m.X,m.Y),m.Instance); }
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
                        if(ImGui.MenuItem("Teleport to mark")) travel.Start(m.WorldId,m.TerritoryId,new(m.X,m.Y),m.Instance);
                        ImGui.EndDisabled();
                    }
                    if(ImGui.MenuItem("Filters")) view.OpenFilters = true;
                    if(ImGui.MenuItem("All settings")) openSettings();
                    ImGui.EndPopup();
                }
                if(hovered)
                {
                    var state=dead ? "Dead" : row.HealthStale ? "Last reported Bear HP (stale) — combat unknown" : !row.HealthKnown ? "Community report — health and combat unknown"
                        : m.InCombat is null ? "Alive — combat unknown" : m.InCombat==true ? "Alive — pulled" : "Alive — not pulled";
                    var detail=$"{m.Rank} {m.Name}\n{state}\n{r.World}{instance} · {r.Dc.Name??"Unknown DC"}\n{r.Zone}";
                    if(row.HasPosition) detail+=$" ({m.X:0.0}, {m.Y:0.0})";
                    detail+="\n"+(row.Visible is { } observation ? "Seen by: "+string.Join(", ",ActiveMarkGrace.ObserverLabels(observation,sync.ClientId,sync.DisplayName())) : row.Bear is not null ? "Bear Toolkit report" : "Faloop report");
                    detail+="\nHealth: "+stateLabel;
                    if (row.Bear is { } bearReport)
                    {
                        detail += "\n" + ActiveMarkPresentation.BearHealthEvidence(row,serverNow);
                        if (dead && bearReport.RecentDeath(serverNow) && bearReport.Report.KilledAt is { } deathAt)
                            detail += $"\nBear death reported {Elapsed(serverNow-deathAt)} ago.";
                        else if (dead)
                            detail += "\nBear reports 0% HP; kill time not confirmed.";
                    }
                    detail+="\nNearby players: "+nearby;
                    var fullAge=ActiveMarkRows.FaloopAge(row.Status,serverNow);
                    detail+=string.IsNullOrEmpty(fullAge) ? "\nNo active Faloop report" : "\nFaloop pull timer: "+fullAge;
                    if (row.Visible is not null) detail+=$"\nObserved {Elapsed(serverNow-m.SeenAt)} ago. Brief report gaps are held for up to 5 seconds.";
                    detail+=row.HasPosition ? "\nClick: map" : "\nLocation not reported";
                    detail+="\n"+travelState.Tooltip+(canTravel ? " Ctrl-click to travel." : string.Empty);
                    detail+="\nRight-click for actions";
                    ImGui.SetTooltip(detail);
                }
                var textY = start.Y+(rowHeight-ImGui.GetTextLineHeight())/2;
                ImGui.SetCursorPos(new Vector2(start.X,textY));
                ImGui.GetWindowDrawList().PushClipRect(screenStart,new Vector2(screenStart.X+layout.SourceWidth,screenStart.Y+rowHeight),true);
                using (ImRaii.PushFont(UiBuilder.IconFont)) ImGui.TextColored(colour,sourceIcon);
                if (!string.IsNullOrEmpty(faloopAge))
                {
                    ImGui.SetCursorPos(new Vector2(start.X+iconWidth+gap,textY));
                    ImGui.TextColored(colour,faloopAge);
                }
                ImGui.GetWindowDrawList().PopClipRect();
                ImGui.SetCursorPos(new Vector2(start.X+layout.NameX,textY));
                ImGui.TextColored(colour,TrainRowPresentation.FitText(ActiveMarkPresentation.Name(row,tab,r.Zone),layout.NameWidth,
                    static value=>ImGui.CalcTextSize(value).X));
                ImGui.SetCursorPos(new Vector2(start.X+layout.WorldX,textY));
                ImGui.TextColored(colour,TrainRowPresentation.FitText(r.World+dcLabel,layout.WorldWidth,
                    static value=>ImGui.CalcTextSize(value).X,instance));
                ImGui.SetCursorPos(new Vector2(start.X+layout.HealthX,textY));
                ImGui.TextColored(colour,stateLabel);
                ImGui.SetCursorPos(new Vector2(start.X+layout.PlayersX,textY));
                ImGui.TextColored(colour,nearby);
                ImGui.SetCursorPos(new Vector2(start.X+layout.ActionX,start.Y));
                ImGui.BeginDisabled(!canTravel);
                var teleport=HuntUi.Button("travel",string.Empty,FontAwesomeIcon.LocationArrow,quiet:true,
                    size:new Vector2(layout.ActionWidth,rowHeight),tooltip:travelState.Tooltip);
                ImGui.EndDisabled();
                if (teleport) travel.Start(m.WorldId,m.TerritoryId,new(m.X,m.Y),m.Instance);
                ImGui.SetCursorPos(new Vector2(start.X,start.Y+rowHeight+ImGui.GetStyle().ItemSpacing.Y));
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        return rows.Count;
    }

    private static void DrawColumnHeadings(ActiveMarkRowLayout layout)
    {
        var start=ImGui.GetCursorPos();
        HuntUi.FillBand(ImGui.GetTextLineHeightWithSpacing(),HuntTheme.Chrome);
        void Heading(float x,float width,string label,string tooltip)
        {
            ImGui.SetCursorPos(new Vector2(start.X+x,start.Y));
            ImGui.TextColored(HuntTheme.Muted,TrainRowPresentation.FitText(label,width,static value=>ImGui.CalcTextSize(value).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        }
        if (layout.SourceWidth >= ImGui.CalcTextSize("Source").X)
            Heading(0,layout.SourceWidth,"Source","Source priority: plugin > Bear > Faloop. Paw: Bear Toolkit. Pull timer always uses Faloop.");
        else
        {
            ImGui.SetCursorPos(start);
            using (ImRaii.PushFont(UiBuilder.IconFont)) ImGui.TextColored(HuntTheme.Muted,FontAwesomeIcon.Eye.ToIconString());
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Observation source");
        }
        Heading(layout.NameX,layout.NameWidth,"Mark","Mark and zone");
        Heading(layout.WorldX,layout.WorldWidth,"World","World and instance");
        Heading(layout.HealthX,layout.HealthWidth,"HP","Health and combat state. ~ marks last reported Bear HP older than 15 seconds; hover the row for its age.");
        ImGui.SetCursorPos(new Vector2(start.X+layout.PlayersX,start.Y));
        using (ImRaii.PushFont(UiBuilder.IconFont)) ImGui.TextColored(HuntTheme.Muted,FontAwesomeIcon.Users.ToIconString());
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Nearby players");
        ImGui.SetCursorPos(new Vector2(start.X,start.Y+ImGui.GetTextLineHeightWithSpacing()));
    }
    private static readonly string[] StatusLabels = { "Unpulled", "Pulled", "Dead", "Community report", "Combat unknown", "Last reported HP (~)" };
    private static float StatusLegendHeight()
    {
        var available=ImGui.GetContentRegionAvail().X;
        var used=0f;
        var lines=1;
        foreach (var label in StatusLabels)
        {
            var width=ImGui.CalcTextSize(label).X;
            if (used>0 && used+ImGui.GetStyle().ItemSpacing.X+width>available) { lines++; used=0; }
            used+=(used>0 ? ImGui.GetStyle().ItemSpacing.X : 0)+width;
        }
        return lines*ImGui.GetTextLineHeightWithSpacing();
    }
    private static void DrawStatusLegend()
    {
        var colours=new[] { HuntTheme.Success,HuntTheme.Warning,HuntTheme.Danger,HuntTheme.Accent,HuntTheme.Muted,HuntTheme.Muted };
        for (var i=0;i<StatusLabels.Length;i++)
        {
            if (i>0) SameLineIfFits(ImGui.CalcTextSize(StatusLabels[i]).X);
            ImGui.TextColored(colours[i],StatusLabels[i]);
        }
    }
    private static string Elapsed(TimeSpan age) => $"{(int)Math.Max(0,age.TotalHours):00}:{Math.Max(0,age.Minutes):00}:{Math.Max(0,age.Seconds):00}";
    private void Flag(SyncSighting mark) => MapFlagHelper.FlagPosition(gameGui,mark.TerritoryId,
        mark.MapId==0 ? detector.GetMapId(mark.TerritoryId) : mark.MapId,mark.Instance,mark.X,mark.Y);
    private void Option(string label,bool value,Action<bool> save)
    { if (HuntUi.WrappedCheckbox(label,ref value)) { save(value); config.Save(); } }
    private void Select<T>(string label,T value,List<T> selected)
    {
        var enabled=selected.Contains(value);
        if (ImGui.Checkbox(label,ref enabled)) { if(enabled) selected.Add(value); else selected.Remove(value); config.Save(); }
    }
    public void DrawSettings()
    {
        if (!ImGui.CollapsingHeader("Active Marks window filters",ImGuiTreeNodeFlags.DefaultOpen)) return;
        DrawFilterOptions();
    }

    private void DrawFilterOptions()
    {
        ImGui.PushID("visibleSettings");
        var o=config.VisibleMarkFilters;
        ImGui.TextUnformatted("Reports");
        Option("Include community S-rank reports",o.IncludeCommunity,v=>o.IncludeCommunity=v);
        Option("Include marks seen only by me",o.IncludeOwn,v=>o.IncludeOwn=v);
        ImGui.Separator();
        ImGui.TextUnformatted("Status");
        Option("Alive",o.Alive,v=>o.Alive=v);
        SameLineIfFits(ImGui.GetFrameHeight()+ImGui.CalcTextSize("Dead (visible corpses)").X+ImGui.GetStyle().ItemInnerSpacing.X);
        Option("Dead (visible corpses)",o.Dead,v=>o.Dead=v);
        Option("Pulled",o.Pulled,v=>o.Pulled=v);
        SameLineIfFits(ImGui.GetFrameHeight()+ImGui.CalcTextSize("Not pulled").X+ImGui.GetStyle().ItemInnerSpacing.X);
        Option("Not pulled",o.NotPulled,v=>o.NotPulled=v);
        Option("Unknown combat status",o.UnknownCombat,v=>o.UnknownCombat=v);
        ImGui.Separator();
        ImGui.TextUnformatted("Ranks");
        foreach (var rank in new[] {"B","A","S","SS"})
        {
            if (rank != "B") SameLineIfFits(ImGui.GetFrameHeight()+ImGui.CalcTextSize(rank).X+ImGui.GetStyle().ItemInnerSpacing.X);
            Select(rank,rank,o.Ranks);
        }
        ImGui.Separator();
        ImGui.TextUnformatted("Scope");
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
        ImGui.Separator();
        Option("Show data centre beside world",o.ShowDataCenter,v=>o.ShowDataCenter=v);
        if (ImGui.TreeNode("Status colours"))
        {
            ImGui.TextColored(HuntTheme.Success,"Alive / not pulled");
            ImGui.TextColored(HuntTheme.Warning,"Alive / pulled");
            ImGui.TextColored(HuntTheme.Danger,"Dead");
            ImGui.TextColored(HuntTheme.Accent,"Community report / health unknown");
            ImGui.TextColored(HuntTheme.Muted,"Live report / combat unknown");
            ImGui.TreePop();
        }
        if(ImGui.SmallButton("Reset window filters")) { config.VisibleMarkFilters=new(); config.Save(); }
        ImGui.PopID();
    }
}
