using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using HuntHelperEvolved.Sync;
using System;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private enum WorkspacePage { Train, ActiveMarks, ARanks, SRanks }
    private WorkspacePage _workspacePage;
    private bool _focusWorkspace;
    private Vector2? _workspaceNextPosition;
    private bool _workspaceHelpVisible;
    private bool _preferencesVisible;
    private bool _focusPreferences;

    private static string WorkspacePageLabel(WorkspacePage page) => page switch
    {
        WorkspacePage.ActiveMarks => "Active Marks",
        WorkspacePage.ARanks => "A-rank Timers",
        WorkspacePage.SRanks => "S-ranks",
        _ => "Train"
    };

    private void OpenWorkspace(WorkspacePage page)
    {
        _workspacePage = page;
        _focusWorkspace = true;
        _configWindowVisible = true;
    }

    private void OpenTrainWorkspace(TrainWorkspacePage page)
    {
        OpenWorkspace(WorkspacePage.Train);
        SelectTrainPage(page);
    }

    private void OpenPreferences(SettingsPage page)
    {
        _settingsPage = page;
        _settingsSearch = string.Empty;
        _preferencesVisible = true;
        _focusPreferences = true;
    }

    private void DrawWorkspace()
    {
        DrawWorkspaceHeader();
        HuntUi.FillBand(ImGui.GetFrameHeight() + 8 * ImGuiHelpers.GlobalScale);
        foreach (var page in Enum.GetValues<WorkspacePage>())
        {
            if (page != WorkspacePage.Train)
            {
                ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < HuntUi.ButtonWidth(WorkspacePageLabel(page)) + 8 * ImGuiHelpers.GlobalScale)
                    ImGui.NewLine();
            }
            if (HuntUi.UnderlineTab(WorkspacePageLabel(page), _workspacePage == page)) _workspacePage = page;
        }
        if (ImGui.BeginChild("Workflow content##" + _workspacePage, Vector2.Zero, false))
        {
            switch (_workspacePage)
            {
                case WorkspacePage.Train: DrawTrainWorkspace(popout: false); break;
                case WorkspacePage.ActiveMarks: _activeMarksWindow.DrawContents(); break;
                case WorkspacePage.ARanks: _arankWindow.DrawContents(); break;
                case WorkspacePage.SRanks:
                    _srankWindow.DrawWorkspaceContents(DrawSelectedSRankCounters,
                        DrawSelectedSRankTrainWatch, DrawCountersContents);
                    break;
            }
        }
        ImGui.EndChild();
    }

    private void DrawWorkspaceHeader()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetFrameHeight();
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var utilityWidth = HuntUi.ButtonWidth("Tally", FontAwesomeIcon.ChartBar) + height * 4 + gap * 5;
        HuntUi.FillBand(height + ImGui.GetStyle().ItemSpacing.Y);
        var start = ImGui.GetCursorScreenPos();
        var dragWidth = Math.Max(height, ImGui.GetContentRegionAvail().X - utilityWidth);
        HuntUi.Button("Workspace drag", string.Empty, quiet: true, size: new Vector2(dragWidth, height));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            _workspaceNextPosition = ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start + new Vector2(0, 5 * scale), start + new Vector2(3 * scale, height - 5 * scale),
            ImGui.GetColorU32(HuntTheme.Telemetry));
        draw.PushClipRect(start, start + new Vector2(dragWidth, height), true);
        var brand = ImGui.CalcTextSize("Hunt Helper Evolved").X + 11 * scale <= dragWidth
            ? "Hunt Helper Evolved" : "HHE";
        draw.AddText(start + new Vector2(11 * scale, (height - ImGui.GetFontSize()) / 2),
            ImGui.GetColorU32(ImGuiCol.Text), brand);
        draw.PopClipRect();
        ImGui.SameLine();
        if (HuntUi.Button("Tally", "Tally", FontAwesomeIcon.ChartBar, quiet: true))
        {
            _tallyWindow.IsOpen = true;
            _tallyWindow.BringToFront();
        }
        ImGui.SameLine();
        ConnectionUi.Draw("workspace-connection", _config, _sync, () => OpenPreferences(SettingsPage.Sharing),
            ConnectionUi.Width(compact: true));
        ImGui.SameLine();
        if (HuntUi.IconButton("Settings", FontAwesomeIcon.SlidersH, "Settings")) OpenPreferences(_settingsPage);
        ImGui.SameLine();
        if (HuntUi.IconButton("More", FontAwesomeIcon.EllipsisH, "Windows and help")) ImGui.OpenPopup("Workspace tools");
        DrawWindowMenu();
        ImGui.SameLine();
        if (HuntUi.IconButton("Close workspace", FontAwesomeIcon.Times, "Close Hunt Helper Evolved")) _configWindowVisible = false;
    }

    private void DrawPreferencesWindow()
    {
        if (!_preferencesVisible) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(680, 580) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(480, 320) * scale, new Vector2(float.MaxValue));
        if (_focusPreferences)
        {
            ImGui.SetNextWindowFocus();
            _focusPreferences = false;
        }
        ImGui.PushStyleColor(ImGuiCol.WindowBg, HuntTheme.Panel);
        if (ImGui.Begin("HHE Settings", ref _preferencesVisible)) DrawSettingsTab();
        ImGui.End();
        ImGui.PopStyleColor();
    }

    private static void WorkspaceSameLine(string label, bool button = false)
    {
        ImGui.SameLine();
        var width = ImGui.CalcTextSize(label).X + (button ? ImGui.GetStyle().FramePadding.X * 2 : 0);
        if (ImGui.GetContentRegionAvail().X < width) ImGui.NewLine();
    }

    private void DrawWorkspaceHelp()
    {
        if (!_workspaceHelpVisible) return;
        ImGui.SetNextWindowSize(new Vector2(720, 520), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("HHE Help", ref _workspaceHelpVisible)) DrawHelpPage();
        ImGui.End();
    }

    private void DrawWindowMenu()
    {
        if (!ImGui.BeginPopup("Workspace tools")) return;
        ImGui.TextDisabled("Windows");
        if (ImGui.MenuItem("Train", "/hht", _trainPopoutVisible)) _trainPopoutVisible = !_trainPopoutVisible;
        if (ImGui.MenuItem("A-rank timers", "/hha", _config.ARankWindowOpen)) _arankWindow.Toggle();
        if (ImGui.MenuItem("S-rank timers", "/hhs", _srankWindow.Visible)) _srankWindow.Toggle();
        if (ImGui.MenuItem("S-rank counters", "/hhc", _counterPopoutVisible)) _counterPopoutVisible = !_counterPopoutVisible;
        if (ImGui.MenuItem("Active Marks", "/hhv", _config.ActiveSRankWindowOpen)) _activeMarksWindow.Toggle();
        if (ImGui.MenuItem("Lifetime tally", "/hhtally", _tallyWindow.IsOpen)) ToggleTallyWindow();
        ImGui.Separator();
        if (ImGui.MenuItem("Help")) _workspaceHelpVisible = !_workspaceHelpVisible;
        ImGui.EndPopup();
    }


}
