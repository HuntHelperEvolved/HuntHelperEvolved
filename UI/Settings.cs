using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Textures;
using KamiToolKit;
using Dalamud.Plugin;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using HuntTally;
using HuntTally.Windows;
using HuntHelperEvolved.Sync;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private enum SettingsPage { Train, Map, Notifications, Travel, Sharing, Discord, Tally, About }
    private SettingsPage _settingsPage;

    private static void DrawSettingsHeading(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(title);
        ImGui.Spacing();
    }

    private void DrawSettingsTab()
    {
        var available = ImGui.GetContentRegionAvail();
        var scale = ImGui.GetFontSize() / 17f;
        var sidebar = available.X >= 580 * scale;
        if (sidebar)
        {
            if (ImGui.BeginChild("Settings navigation", new Vector2(130 * scale, 0), true))
                foreach (var page in Enum.GetValues<SettingsPage>())
                    if (ImGui.Selectable(page.ToString(), _settingsPage == page)) _settingsPage = page;
            ImGui.EndChild();
            ImGui.SameLine();
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##Settings category", _settingsPage.ToString()))
            {
                foreach (var page in Enum.GetValues<SettingsPage>())
                    if (ImGui.Selectable(page.ToString(), _settingsPage == page)) _settingsPage = page;
                ImGui.EndCombo();
            }
        }
        if (ImGui.BeginChild("Settings content##" + _settingsPage, new Vector2(0, 0), false))
        {
            ImGui.PushID(_settingsPage.ToString());
            ImGui.TextUnformatted(_settingsPage.ToString());
            ImGui.Separator();
            ImGui.Spacing();
            switch (_settingsPage)
            {
                case SettingsPage.Train:
                    DrawTrainPreferences();
                    DrawSettingsHeading("Trigger counters");
                    DrawCounterPreferences();
                    break;
                case SettingsPage.Map:
                    DrawMapPreferences();
                    DrawSettingsHeading("Shared sightings and S-rank mapping");
                    DrawRemoteMapPreferences();
                    break;
                case SettingsPage.Notifications:
                    DrawDetectionNotificationSettings();
                    DrawSettingsHeading("Community S-rank alerts");
                    DrawCommunityAlertPreferences();
                    break;
                case SettingsPage.Travel: DrawTravelPreferences(); break;
                case SettingsPage.Sharing:
                    DrawConnectionPreferences();
                    DrawSettingsHeading("Share with the group");
                    DrawSharingPreferences();
                    DrawSettingsHeading("Active Marks");
                    _activeMarksWindow.DrawSettings();
                    if (ImGui.Button("Open Active Marks")) _activeMarksWindow.Toggle();
                    ImGui.SameLine();
                    if (ImGui.Button("S-rank timers")) _srankWindow.Toggle();
                    ImGui.SameLine();
                    if (ImGui.Button("A-rank timers")) _arankWindow.Toggle();
                    break;
                case SettingsPage.Discord: DrawDiscordPreferences(); break;
                case SettingsPage.Tally: DrawTallyTab(); break;
                case SettingsPage.About: DrawAboutPreferences(); break;
            }
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private void DrawTrainPreferences()
    {
        var echoClick = _config.EchoOnMarkClick;
        if (ImGui.Checkbox("Echo a mark to chat when its row is clicked", ref echoClick))
        {
            _config.EchoOnMarkClick = echoClick;
            _config.Save();
        }
        ImGui.TextDisabled("Off still flags the mark on your map — it just doesn't post the chat line.");

        var observedDeaths = _config.MarkDeadOnObservedDefeat;
        if (ImGui.Checkbox("Tick a mark dead when the battle log says it died", ref observedDeaths))
        {
            _config.MarkDeadOnObservedDefeat = observedDeaths;
            _config.Save();
        }

        var teleFlags = _config.TeleportAlsoFlags;
        if (ImGui.Checkbox("Teleport also drops the map flag", ref teleFlags))
        {
            _config.TeleportAlsoFlags = teleFlags;
            _config.Save();
        }

        var showAge = _config.ShowMarkAge;
        if (ImGui.Checkbox("Show how long ago each mark was last seen", ref showAge))
        {
            _config.ShowMarkAge = showAge;
            _config.Save();
        }

        var hideDeadSetting = _config.HideDeadMarks;
        if (ImGui.Checkbox("Hide dead marks in the train list", ref hideDeadSetting))
        {
            _config.HideDeadMarks = hideDeadSetting;
            _config.Save();
        }
        ImGui.TextDisabled("Display only — dead marks stay in the train and in reports.");

        var hideZones = _config.HideZonesInPopout;
        if (ImGui.Checkbox("Hide zone names in the train popout", ref hideZones))
        {
            _config.HideZonesInPopout = hideZones;
            _config.Save();
        }

        var spicing = _config.ShowSpicing;
        if (ImGui.Checkbox("Show spicing markers", ref spicing))
        {
            _config.ShowSpicing = spicing;
            _config.Save();
        }
        ImGui.TextDisabled("A scout flagging a mark they'll prep before the train arrives.");

        var autoAdv = _config.AutoAdvance;
        if (ImGui.Checkbox("Auto-advance to the next mark when the current one dies", ref autoAdv))
        {
            _config.AutoAdvance = autoAdv;
            _config.Save();
        }

        if (_config.AutoAdvance)
        {
            var echoAdv = _config.EchoOnAdvance;
            if (ImGui.Checkbox("Echo and flag the mark it advances to", ref echoAdv))
            {
                _config.EchoOnAdvance = echoAdv;
                _config.Save();
            }
        }

        var rowH = _config.TrainRowHeight;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Row height (pixels)", ref rowH))
        {
            _config.TrainRowHeight = Math.Clamp(rowH, 14, 48);
            _config.Save();
        }

        var pollInterval = _config.PollIntervalSeconds;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Detection interval (seconds)", ref pollInterval))
        {
            _config.PollIntervalSeconds = Math.Clamp(pollInterval, 1, 30);
            _config.Save();
        }
        ImGui.TextDisabled("How often marks are scanned for. Lower catches more while flying fast.");
        ImGui.Spacing();
    }

    private void DrawCounterPreferences()
    {
        var myKills = _config.CountOnlyMyKills;
        if (ImGui.Checkbox("Count only kills I land", ref myKills))
        {
            _config.CountOnlyMyKills = myKills;
            _config.Save();
        }

        ImGui.Spacing();
    }

    private void DrawMapPreferences()
    {
        var mapPoints = _config.ShowSpawnPointsOnMap;
        if (ImGui.Checkbox("Show spawn points on the in-game map", ref mapPoints))
        {
            _config.ShowSpawnPointsOnMap = mapPoints;
            _config.Save();
        }
        ImGui.TextDisabled("Where a mark could be. A zone's B-rank points alone can run to sixty dots.");

        var mapMarks = _config.ShowMarksOnMap;
        if (ImGui.Checkbox("Show live marks on the in-game map", ref mapMarks))
        {
            _config.ShowMarksOnMap = mapMarks;
            _config.Save();
        }

        ImGui.TextDisabled(_mapOverlay.Status);

        var bar = _config.ShowMapControlBar;
        if (ImGui.Checkbox("Show a control bar above the map", ref bar))
        {
            _config.ShowMapControlBar = bar;
            _config.Save();
        }
        ImGui.TextDisabled("These same toggles, pinned to the top of the game's map and shown with it. Also /htrm.");

        if (_config.ShowSpawnPointsOnMap || _config.ShowMarksOnMap)
        {
            if (_config.ShowSpawnPointsOnMap)
            {
                var showA = _config.ShowARankPoints;
                if (ImGui.Checkbox("A-rank points", ref showA))
                {
                    _config.ShowARankPoints = showA;
                    _config.Save();
                }
                ImGui.SameLine();
                var showB = _config.ShowBRankPoints;
                if (ImGui.Checkbox("B-rank##points", ref showB))
                {
                    _config.ShowBRankPoints = showB;
                    _config.Save();
                }
                ImGui.SameLine();
                var showS = _config.ShowSRankPoints;
                if (ImGui.Checkbox("S-rank##points", ref showS))
                {
                    _config.ShowSRankPoints = showS;
                    _config.Save();
                }
            }

            if (_config.ShowMarksOnMap)
            {
                var markA = _config.ShowARankMarks;
                if (ImGui.Checkbox("A-rank marks", ref markA))
                {
                    _config.ShowARankMarks = markA;
                    _config.Save();
                }
                ImGui.SameLine();
                var markB = _config.ShowBRankMarks;
                if (ImGui.Checkbox("B-rank##marks", ref markB))
                {
                    _config.ShowBRankMarks = markB;
                    _config.Save();
                }
                ImGui.SameLine();
                var markS = _config.ShowSRankMarks;
                if (ImGui.Checkbox("S-rank##marks", ref markS))
                {
                    _config.ShowSRankMarks = markS;
                    _config.Save();
                }
            }

            var clickFlag = _config.ClickSpawnPointToFlag;
            if (ImGui.Checkbox("Click a spawn point on the map to flag it", ref clickFlag))
            {
                _config.ClickSpawnPointToFlag = clickFlag;
                _config.Save();
            }

            var ssEvent = _config.ShowSsEventOnMap;
            if (ImGui.Checkbox("Mark SS event minion locations", ref ssEvent))
            {
                _config.ShowSsEventOnMap = ssEvent;
                _config.Save();
            }

            ImGui.TextDisabled(_ssEvent.Status);

            var labels = _config.ShowMarkLabelsOnMap;
            if (ImGui.Checkbox("Write mark names and health on the map", ref labels))
            {
                _config.ShowMarkLabelsOnMap = labels;
                _config.Save();
            }

            if (_config.ShowMarkLabelsOnMap)
            {
                var fontSize = _config.MarkLabelFontSize;
                ImGui.SetNextItemWidth(90);
                if (ImGui.InputFloat("Name text size", ref fontSize, 1f))
                {
                    _config.MarkLabelFontSize = Math.Clamp(fontSize, 6f, 48f);
                    _config.Save();
                }
            }

            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Dot colours")) DrawDotColours();
            ImGui.Spacing();

            var dotSize = _config.SpawnDotSize;
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputFloat("Dot size", ref dotSize, 2f))
            {
                _config.SpawnDotSize = Math.Clamp(dotSize, 6f, 48f);
                _config.Save();
            }

        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Player guides")) DrawPlayerGuideSettings();
        ImGui.Spacing();
    }

    private void DrawAboutPreferences()
    {
        ImGui.TextDisabled($"Hunt Helper Evolved {ReleaseNotes.CurrentVersion}");
        ImGui.Spacing();

        // The notes are a window of their own and turn up on their own
        // after an update, so this is the way back to them afterwards.
        if (ImGui.Button("What's new"))
            _releaseNotesVisible = true;

        ImGui.SameLine();
        ImGui.TextDisabled("Changes in this and previous versions, and who to thank.");
        ImGui.Spacing();

        ImGui.TextDisabled("IPC: this train is available through HuntHelperEvolved endpoints.");
        ImGui.Spacing();
    }

    private void DrawTravelPreferences()
    {
        ImGui.TextWrapped("Aetheryte blacklist — never route to these.");
        ImGui.TextDisabled("Affects the teleport button and Next Aetheryte alike.");
        ImGui.Spacing();
        DrawBlacklistPicker();
        ImGui.Spacing();
    }

    private void DrawDiscordPreferences()
    {
        DrawWebhookList();
        ImGui.Spacing();
        if (ImGui.Button("Send test message")) _ = SendTestAsync();
        ImGui.TextDisabled("Posts to enabled webhooks.");
    }

    private void DrawConnectionPreferences()
    {
        ImGui.Spacing();
        var enabled = _config.SyncEnabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            _config.SyncEnabled = enabled;
            _config.Save();
            _sync.ApplySettings();
        }

        ImGui.SetNextItemWidth(Math.Min(360, Math.Max(100, ImGui.GetContentRegionAvail().X - 110)));
        var url = _config.SyncServerUrl;
        if (ImGui.InputTextWithHint("Server URL", "wss://hunts.example.com/ws", ref url, 512))
            _config.SyncServerUrl = url;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _config.Save();
            _sync.ApplySettings();
        }

        ImGui.SetNextItemWidth(Math.Min(360, Math.Max(100, ImGui.GetContentRegionAvail().X - 110)));
        var password = _config.SyncPassword;
        var passwordFlags = _showSyncPassword ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        if (ImGui.InputText("Password", ref password, 256, passwordFlags))
            _config.SyncPassword = password;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _config.Save();
            _sync.ApplySettings();
        }
        ImGui.SameLine();
        ImGui.Checkbox("show", ref _showSyncPassword);

        ImGui.SetNextItemWidth(Math.Min(360, Math.Max(100, ImGui.GetContentRegionAvail().X - 110)));
        var name = _config.SyncDisplayName;
        if (ImGui.InputTextWithHint("Display name", "Anonymous", ref name, 40))
            _config.SyncDisplayName = name;
        if (ImGui.IsItemDeactivatedAfterEdit())
            _config.Save();
        if (ImGui.IsItemDeactivatedAfterEdit()) _sync.ApplySettings();
        ImGui.TextDisabled("Your chosen alias is shared. Blank uses Anonymous.");

        ImGui.Spacing();
        if (_config.SyncEnabled && !_sync.IsConnected && !string.IsNullOrEmpty(_sync.LastError))
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _sync.Status);
        else
            ImGui.TextWrapped($"Status: {_sync.Status}");

        if (_sync.IsConnected && !string.IsNullOrEmpty(_sync.LastError))
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), $"Server said: {_sync.LastError}");

        if (_sync.IsConnected)
        {
            if (_sync.LocalBackupCount > 0 && _config.SyncShareTrain
                && ImGui.Button($"Upload saved local marks ({_sync.LocalBackupCount})"))
                _sync.UploadLocalBackup();
            if (_sync.LocalWatchBackupCount > 0 && _config.SyncShareTrain
                && ImGui.Button($"Upload saved local watches ({_sync.LocalWatchBackupCount})"))
                _sync.UploadWatchBackup();
            ImGui.TextDisabled("Joining uses the server train. Upload saved local marks explicitly if needed.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Reconnect"))
            {
                _sync.ApplySettings(force: true);
            }

            ImGui.Spacing();
            ImGui.TextWrapped("Online now:");
            foreach (var client in _sync.Clients)
            {
                var where = client.TerritoryId != 0
                    ? $" — {_detector.GetZoneName(client.TerritoryId)}{ExpansionData.InstanceGlyph(client.Instance)}"
                    : string.Empty;
                var world = client.WorldId != 0 ? $" [{_worldData.NameOf(client.WorldId)}]" : string.Empty;
                ImGui.BulletText($"{client.Name}{world}{where}");
            }

            var faloop = _sync.Faloop;
            if (faloop.Enabled)
                ImGui.TextDisabled($"Faloop on the server: {faloop.Status} Live feed: {(faloop.LiveConnected ? "connected" : "disconnected")}");
        }

        ImGui.Spacing();
    }

    private void DrawSharingPreferences()
    {
        var train = _config.SyncShareTrain;
        if (ImGui.Checkbox("The train", ref train))
        {
            _config.SyncShareTrain = train;
            _config.Save();
            _sync.ApplySettings();
        }

        var sightings = _config.SyncShareSightings;
        if (ImGui.Checkbox("What I can see", ref sightings))
        {
            _config.SyncShareSightings = sightings;
            _config.Save();
        }

        var kills = _config.SyncReportSRankKills;
        if (ImGui.Checkbox("S-rank kills I witness", ref kills))
        {
            _config.SyncReportSRankKills = kills;
            _config.Save();
        }
        ImGui.TextDisabled("The exact moment an S dies in front of you starts the group's clock for it.");
        if (_standaloneTallyPresent)
            ImGui.TextWrapped("The built-in tally is disabled while the separate Hunt Tally plugin is installed.");
    }

    private void DrawRemoteMapPreferences()
    {
        var remote = _config.SyncShowRemoteMarksOnMap;
        if (ImGui.Checkbox("Marks other members can see, on my map", ref remote))
        {
            _config.SyncShowRemoteMarksOnMap = remote;
            _config.Save();
        }

        var candidates = _config.ShowSRankCandidatesOnMap;
        if (ImGui.Checkbox("Which spawn points the S can still use", ref candidates))
        {
            _config.ShowSRankCandidatesOnMap = candidates;
            _config.Save();
        }

        const ImGuiColorEditFlags flags = ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf;
        var outlineWidth = _config.SpawnCandidateOutlineWidth;
        if (ImGui.SliderInt("S candidate outline width", ref outlineWidth, 1, 12, "%d / 32"))
        {
            _config.SpawnCandidateOutlineWidth = outlineWidth;
            _config.Save();
        }

        var candidate = _config.SpawnDotColourSCandidate;
        if (ImGui.ColorEdit4("S candidate outline / confirmed fill", ref candidate, flags))
        {
            _config.SpawnDotColourSCandidate = candidate;
            _config.Save();
        }

    }

    private void DrawCommunityAlertPreferences()
    {
        var alerts = _config.SyncSpawnAlerts;
        if (ImGui.Checkbox("Chat alerts for group S sightings and Faloop spawns/releases", ref alerts)) { _config.SyncSpawnAlerts = alerts; _config.Save(); }
        var sound = _config.SyncSpawnSound;
        if (ImGui.Checkbox("Play an alert sound", ref sound)) { _config.SyncSpawnSound = sound; _config.Save(); }
        var currentDc = _config.SyncSpawnCurrentDc;
        if (ImGui.Checkbox("Only my current data centre", ref currentDc)) { _config.SyncSpawnCurrentDc = currentDc; _config.Save(); }
        if (!currentDc)
            foreach (var dc in _worldData.DataCenters)
            {
                var selected = _config.SyncSpawnDataCenters.Contains(dc.Id);
                if (ImGui.Checkbox(dc.Name + "##spawnDc", ref selected))
                {
                    if (selected) _config.SyncSpawnDataCenters.Add(dc.Id); else _config.SyncSpawnDataCenters.Remove(dc.Id);
                    _config.Save();
                }
            }
        ImGui.TextWrapped("Server coverage: " + string.Join(", ", _sync.Faloop.DataCenters));
        if (ImGui.Button("Test S-rank chat alert"))
            ShowSpawnAlert(new Sync.SRankSpawnBroadcast { NameId = Sync.SRankTimerData.All[0].NameId,
                WorldId = _detector.CurrentWorldId(), SpawnedAt = DateTime.UtcNow, X = 21.5f, Y = 21.5f, Source = "Local test" }, test: true);

        ImGui.TextWrapped(_lastCommunityAlert);
        ImGui.TextDisabled($"Last server feed message: {_sync.Faloop.LastLiveMessageAt?.ToLocalTime().ToString("HH:mm:ss") ?? "none"}; last broadcast alert: {_sync.Faloop.LastAlertAt?.ToLocalTime().ToString("HH:mm:ss") ?? "none"}");
        }

}
