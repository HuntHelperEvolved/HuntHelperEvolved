using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private bool _selectActiveMarksSettings;

    private void DrawWindowMenu()
    {
        if (!ImGui.BeginMenuBar()) return;
        if (ImGui.BeginMenu("Windows"))
        {
            if (ImGui.MenuItem("Train", "/hht", _trainPopoutVisible)) _trainPopoutVisible = !_trainPopoutVisible;
            if (ImGui.MenuItem("A-rank timers", "/hha", _config.ARankWindowOpen)) _arankWindow.Toggle();
            if (ImGui.MenuItem("S-rank timers", "/hhs", _srankWindow.Visible)) _srankWindow.Toggle();
            if (ImGui.MenuItem("S-rank counters", "/hhc", _counterPopoutVisible)) _counterPopoutVisible = !_counterPopoutVisible;
            if (ImGui.MenuItem("Active Marks", "/hhv", _config.ActiveSRankWindowOpen)) _activeMarksWindow.Toggle();
            ImGui.Separator();
            if (ImGui.MenuItem("Lifetime tally", "/hhtally", _tallyWindow.IsOpen)) ToggleTallyWindow();
            ImGui.EndMenu();
        }
        ImGui.EndMenuBar();
    }

    private void DrawSRankWorkspace()
    {
        if (!ImGui.BeginTabBar("S-rank views")) return;
        if (ImGui.BeginTabItem("Timers & mapping"))
        {
            if (ImGui.BeginChild("Embedded S-rank board", Vector2.Zero, false)) _srankWindow.DrawContents();
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Counters"))
        {
            DrawCountersTab();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
}
