using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using HuntHelperEvolved.Sync;

namespace HuntHelperEvolved;

internal static class ConnectionUi
{
    private static FontAwesomeIcon Icon(ConnectionDisplayState state) => state switch
    {
        ConnectionDisplayState.Off => FontAwesomeIcon.PowerOff,
        ConnectionDisplayState.Connected => FontAwesomeIcon.Plug,
        ConnectionDisplayState.Connecting or ConnectionDisplayState.Reconnecting => FontAwesomeIcon.SyncAlt,
        _ => FontAwesomeIcon.Unlink
    };

    private static Vector4 Colour(ConnectionDisplayState state) => state switch
    {
        ConnectionDisplayState.Off => HuntTheme.Muted,
        ConnectionDisplayState.Connected => HuntTheme.Success,
        ConnectionDisplayState.Connecting or ConnectionDisplayState.Reconnecting => HuntTheme.Warning,
        _ => HuntTheme.Danger
    };

    public static float Width(bool compact = false)
    {
        var width = ImGui.GetFrameHeight();
        if (!compact)
            foreach (var state in Enum.GetValues<ConnectionDisplayState>())
                width = Math.Max(width, HuntUi.ButtonWidth(ConnectionPresentation.Label(state), Icon(state)));
        return width;
    }

    public static void Draw(string id, Configuration config, SyncCoordinator sync, Action openSettings, float? width = null)
    {
        ImGui.PushID(id);
        var state = ConnectionPresentation.State(config.SyncEnabled, sync.Client.State, sync.Client.FatalError);
        var label = ConnectionPresentation.Label(state);
        var available = Math.Max(ImGui.GetFrameHeight(), ImGui.GetContentRegionAvail().X);
        var desiredWidth = width ?? (Width() <= available ? Width() : Width(compact: true));
        var tooltip = $"{label}\n{sync.Status}\n{ConnectionPresentation.TrainScope(config.SyncEnabled, sync.IsConnected, config.SyncShareTrain)}";
        if (sync.IsConnected && !string.IsNullOrWhiteSpace(sync.LastError)) tooltip += "\nServer said: " + sync.LastError;
        ImGui.PushStyleColor(ImGuiCol.Text, Colour(state));
        var clicked = HuntUi.Button("connection", desiredWidth > ImGui.GetFrameHeight() ? label : string.Empty,
            Icon(state), quiet: true, size: new Vector2(desiredWidth, ImGui.GetFrameHeight()), tooltip: tooltip);
        ImGui.PopStyleColor();
        if (clicked) ImGui.OpenPopup("Connection details");
        if (ImGui.BeginPopup("Connection details"))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetFontSize() * 24);
            DrawDetails(config, sync);
            ImGui.PopTextWrapPos();
            ImGui.Separator();
            if (ImGui.MenuItem("Connection settings")) openSettings();
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    public static void DrawDetails(Configuration config, SyncCoordinator sync)
    {
        var state = ConnectionPresentation.State(config.SyncEnabled, sync.Client.State, sync.Client.FatalError);
        ImGui.PushStyleColor(ImGuiCol.Text, Colour(state));
        ImGui.AlignTextToFramePadding();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextUnformatted(Icon(state).ToIconString());
        ImGui.PopFont();
        ImGui.SameLine(0, 6 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(ConnectionPresentation.Label(state));
        ImGui.PopStyleColor();
        ImGui.TextWrapped(sync.Status);
        ImGui.TextDisabled(ConnectionPresentation.TrainScope(config.SyncEnabled, sync.IsConnected, config.SyncShareTrain));
        if (sync.IsConnected && !string.IsNullOrWhiteSpace(sync.LastError))
            ImGui.TextColored(HuntTheme.Warning, "Server said: " + sync.LastError);
        var endpointValid = SyncCoordinator.TryBuildUri(config.SyncServerUrl, out _, out var problem, config.SyncAllowPlaintext);
        var canReconnect = config.SyncEnabled && endpointValid;
        ImGui.BeginDisabled(!canReconnect);
        if (HuntUi.Button("reconnect", "Reconnect", FontAwesomeIcon.SyncAlt,
            tooltip: !config.SyncEnabled ? "Enable sync to connect." : !endpointValid ? problem : "Reconnect to the configured server."))
            sync.ApplySettings(force: true);
        ImGui.EndDisabled();
        var faloop = sync.Faloop;
        if (faloop.Enabled)
        {
            ImGui.Separator();
            ImGui.TextUnformatted(FaloopFreshness(faloop));
            if (!string.IsNullOrWhiteSpace(faloop.Status)) ImGui.TextDisabled(faloop.Status);
            ImGui.TextDisabled($"Faloop live feed: {(faloop.LiveConnected ? "connected" : "disconnected")}");
            ImGui.TextDisabled($"Server feed: {(faloop.DataCenters.Count > 0 ? string.Join(", ", faloop.DataCenters) : "none reported")}");
        }
        else if (sync.IsConnected) ImGui.TextDisabled("Faloop: off");
        ImGui.Separator();
        ImGui.TextUnformatted("Bear Toolkit S-rank feed");
        if (!config.SyncReceiveBearFeed)
            ImGui.TextDisabled("Off on this client. Enable reception in Settings > Sharing.");
        else
        {
            if (!sync.IsConnected) ImGui.TextDisabled("Waiting for the group server.");
            else if (!sync.SupportsBearFeed) ImGui.TextDisabled("This server does not support Bear reports.");
            else
            {
                var bear = sync.BearStatus;
                ImGui.TextDisabled(!bear.Enabled ? "Bear is off on this server." : bear.Connected ? "Live feed connected" : "Live feed disconnected");
                ImGui.TextWrapped(bear.Status);
                if (bear.DataCenters.Count > 0) ImGui.TextDisabled("Server feed: " + string.Join(", ", bear.DataCenters));
                if (bear.LastMessageAt is { } last)
                    ImGui.TextDisabled("Last Bear event: " + TimerTableUi.Duration(sync.ServerTimeFor(DateTime.UtcNow) - last) + " ago");
                ImGui.TextDisabled($"{sync.BearMarks.Count} reports; HP is fresh for 15 seconds.");
                ImGui.TextDisabled("~HP is the last reported value while the Bear sighting remains active.");
            }
        }
    }

    private static string FaloopFreshness(SyncFaloopStatus faloop)
    {
        if (!faloop.Connected) return "Faloop: not reachable";
        if (faloop.LastSyncAt is not { } at) return "Faloop: waiting for the first read";
        var age = DateTime.UtcNow - at;
        return age.TotalMinutes < 1 ? "Faloop: read just now" : $"Faloop: read {TimerTableUi.Duration(age)} ago";
    }
}
