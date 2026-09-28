using Dalamud.Bindings.ImGui;
using HuntHelperEvolved.Sync;
using System;
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
        DrawServerMenu();
        ImGui.EndMenuBar();
    }

    private void DrawServerMenu()
    {
        if (!ImGui.BeginMenu("Server")) return;

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetFontSize() * 24);
        if (_config.SyncEnabled && !_sync.IsConnected)
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _sync.Status);
        else
            ImGui.TextUnformatted($"Sync: {_sync.Status}");

        if (_sync.IsConnected && !string.IsNullOrEmpty(_sync.LastError))
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), $"Server said: {_sync.LastError}");

        var faloop = _sync.Faloop;
        if (faloop.Enabled)
        {
            ImGui.Separator();
            ImGui.TextUnformatted(FaloopFreshness(faloop));
            if (!string.IsNullOrWhiteSpace(faloop.Status)) ImGui.TextDisabled(faloop.Status);
            ImGui.TextDisabled($"Live feed: {(faloop.LiveConnected ? "connected" : "disconnected")}");
            ImGui.TextDisabled($"Server feed: {(faloop.DataCenters.Count > 0 ? string.Join(", ", faloop.DataCenters) : "none reported")}");
        }
        else if (_sync.IsConnected)
            ImGui.TextDisabled("Faloop: off");
        ImGui.PopTextWrapPos();

        ImGui.Separator();
        if (ImGui.MenuItem("Sharing settings")) _selectSyncTab = true;
        ImGui.EndMenu();
    }

    private static string FaloopFreshness(SyncFaloopStatus faloop)
    {
        if (!faloop.Connected) return "Faloop: not reachable";
        if (faloop.LastSyncAt is not { } at) return "Faloop: waiting for the first read";
        var age = DateTime.UtcNow - at;
        return age.TotalMinutes < 1 ? "Faloop: read just now" : $"Faloop: read {TimerTableUi.Duration(age)} ago";
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
