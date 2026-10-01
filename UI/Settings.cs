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
    private enum SettingsPage { Appearance, Train, Counters, Map, Notifications, Travel, Sharing, ActiveMarks, Discord, Tally, Reset }
    private SettingsPage _settingsPage;
    private string _settingsSearch = string.Empty;
    private static string SettingsPageLabel(SettingsPage page) => page switch
    {
        SettingsPage.Counters => "Counters",
        SettingsPage.Map => "Map & overlays",
        SettingsPage.Sharing => "Sharing",
        SettingsPage.ActiveMarks => "Active Marks",
        _ => page.ToString()
    };

    private bool _drawingSettingsSearch;
    private SettingsPage _settingsResultPage;
    private int _settingsMatchCount;
    private string _settingsSearchQuery = string.Empty;
    private readonly Dictionary<(SettingsPage Page, string Label, string Aliases), bool> _settingsMatchCache = new();

    private bool SettingMatches(string label, string aliases = "")
    {
        if (!_drawingSettingsSearch) return true;
        var key = (_settingsResultPage, label, aliases);
        if (!_settingsMatchCache.TryGetValue(key, out var match))
            _settingsMatchCache[key] = match = SettingsSearchIndex.MatchesSetting(_settingsSearch,
                SettingsPageLabel(_settingsResultPage), label, aliases);
        if (!match) return false;
        if (_settingsMatchCount++ > 0) ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(HuntTheme.Muted, SettingsPageLabel(_settingsResultPage) + " / " + label);
        return true;
    }

    private void DrawSettingsSearch()
    {
        if (ImGui.BeginChild("Matching settings", Vector2.Zero, false))
        {
            if (_settingsSearchQuery != _settingsSearch)
            {
                _settingsSearchQuery = _settingsSearch;
                _settingsMatchCache.Clear();
                ImGui.SetScrollY(0);
            }
            _settingsMatchCount = 0;
            _drawingSettingsSearch = true;
            ImGui.PushTextWrapPos(0);
            try
            {
                foreach (var page in Enum.GetValues<SettingsPage>())
                {
                    _settingsResultPage = page;
                    ImGui.PushID(page.ToString());
                    try { DrawSettingsPage(page); }
                    finally { ImGui.PopID(); }
                }
            }
            finally
            {
                _drawingSettingsSearch = false;
                ImGui.PopTextWrapPos();
            }
            ImGui.Spacing();
            ImGui.TextDisabled(_settingsMatchCount == 0 ? "No matching settings. Try a setting name or keyword."
                : $"{_settingsMatchCount} matching setting{(_settingsMatchCount == 1 ? "" : "s")}");
        }
        ImGui.EndChild();
    }

    private void DrawSettingsHeading(string title)
    {
        if (_drawingSettingsSearch) return;
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(title);
        ImGui.Spacing();
    }

    private void DrawSettingsTab()
    {
        var clearSize = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - clearSize - ImGui.GetStyle().ItemSpacing.X));
        ImGui.InputTextWithHint("##Preference search", "Search settings by name or keyword", ref _settingsSearch, 160);
        ImGui.SameLine();
        ImGui.BeginDisabled(_settingsSearch.Length == 0);
        if (HuntUi.IconButton("Clear search", FontAwesomeIcon.Times, "Clear preference search")) _settingsSearch = string.Empty;
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Clear preference search");
        if (!string.IsNullOrWhiteSpace(_settingsSearch))
        {
            DrawSettingsSearch();
            return;
        }
        var navigationWidth = Math.Min(ImGui.GetFontSize() * 12, ImGui.GetContentRegionAvail().X * .30f);
        var navigationStart = ImGui.GetCursorScreenPos();
        var navigationHeight = ImGui.GetContentRegionAvail().Y;
        if (ImGui.BeginChild("Settings categories", new Vector2(navigationWidth, 0), false))
        {
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                var label = SettingsPageLabel(page);
                var height = ImGui.CalcTextSize(label, false, ImGui.GetContentRegionAvail().X).Y
                    + ImGui.GetStyle().FramePadding.Y * 2 + 4;
                var start = ImGui.GetCursorPos();
                if (ImGui.Selectable("##Settings " + page, _settingsPage == page,
                    ImGuiSelectableFlags.None, new Vector2(0, height))) _settingsPage = page;
                var end = ImGui.GetCursorPos();
                ImGui.SetCursorPos(start + new Vector2(4, ImGui.GetStyle().FramePadding.Y + 2));
                ImGui.TextWrapped(label);
                ImGui.SetCursorPos(end);
            }
        }
        ImGui.EndChild();
        var dividerX = navigationStart.X + navigationWidth;
        ImGui.GetWindowDrawList().AddLine(new Vector2(dividerX, navigationStart.Y),
            new Vector2(dividerX, navigationStart.Y + navigationHeight), ImGui.GetColorU32(HuntTheme.Line));
        ImGui.SameLine();
        if (ImGui.BeginChild("Settings content##" + _settingsPage, new Vector2(0, 0), false))
        {
            ImGui.PushID(_settingsPage.ToString());
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(SettingsPageLabel(_settingsPage));
            if (!_drawingSettingsSearch) ImGui.Separator();
            if (!_drawingSettingsSearch) ImGui.Spacing();
            DrawSettingsPage(_settingsPage);
            ImGui.PopTextWrapPos();
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    private void DrawSettingsPage(SettingsPage page)
    {
        switch (page)
        {
            case SettingsPage.Appearance:
                if (SettingMatches("Theme", "Graphite Daylight Dalamud light dark appearance")) HuntTheme.DrawPreferences(_config);
                if (SettingMatches("Window opacity", "WindowOpacity global transparency transparent alpha appearance")) HuntTheme.DrawOpacityPreferences(_config);
                if (SettingMatches("Show release notes after updates", "ShowReleaseNotesOnUpdate"))
                {
                    var releaseNotes = _config.ShowReleaseNotesOnUpdate;
                    if (HuntUi.WrappedCheckbox("Show release notes after updates", ref releaseNotes))
                    {
                        _config.ShowReleaseNotesOnUpdate = releaseNotes;
                        _config.Save();
                    }
                }

                break;
            case SettingsPage.Train:
                DrawTrainPreferences();
                break;
            case SettingsPage.Counters:
                DrawCounterPreferences();
                break;
            case SettingsPage.Map:
                DrawMapPreferences();
                DrawSettingsHeading("Shared sightings and S-rank mapping");
                DrawRemoteMapPreferences();
                break;
            case SettingsPage.Notifications:
                DrawSettingsHeading("Bongo sounds");
                DrawBongoSoundPreferences();
                DrawDetectionNotificationSettings();
                DrawSettingsHeading("Community S-rank alerts");
                DrawCommunityAlertPreferences();
                break;
            case SettingsPage.Travel: DrawTravelPreferences(); break;
            case SettingsPage.Sharing:
                DrawConnectionPreferences();
                DrawSettingsHeading("Share with the group");
                DrawSharingPreferences();
                break;
            case SettingsPage.ActiveMarks:
                _activeMarksWindow.DrawSettings(_drawingSettingsSearch ? SettingMatches : null);
                break;
            case SettingsPage.Discord: DrawDiscordPreferences(); break;
            case SettingsPage.Tally: DrawTallyTab(); break;
            case SettingsPage.Reset: DrawSettingsResets(); break;
        }
    }


    private void DrawTrainPreferences()
    {
        if (SettingMatches("Auto-mark dead using Hunt Tally", "AutoMarkDeadEnabled observed death"))
        {
            var autoMark = _config.AutoMarkDeadEnabled;
            if (HuntUi.WrappedCheckbox("Auto-mark dead using Hunt Tally", ref autoMark))
            {
                _config.AutoMarkDeadEnabled = autoMark;
                _config.Save();
            }
            ImGui.TextDisabled(TallyFeedStatus());
            ImGui.TextDisabled("Observed deaths update this train automatically; unknown kill times remain unknown.");
        }

        if (SettingMatches("Echo a mark to chat when its row is clicked", "EchoOnMarkClick"))
        {
            var echoClick = _config.EchoOnMarkClick;
            if (HuntUi.WrappedCheckbox("Echo a mark to chat when its row is clicked", ref echoClick))
            {
                _config.EchoOnMarkClick = echoClick;
                _config.Save();
            }
            ImGui.TextDisabled("Off still flags the mark on your map — it just doesn't post the chat line.");
        }

        if (SettingMatches("Tick a mark dead when the battle log says it died", "MarkDeadOnObservedDefeat"))
        {
            var observedDeaths = _config.MarkDeadOnObservedDefeat;
            if (HuntUi.WrappedCheckbox("Tick a mark dead when the battle log says it died", ref observedDeaths))
            {
                _config.MarkDeadOnObservedDefeat = observedDeaths;
                _config.Save();
            }
        }

        if (SettingMatches("Teleport also drops the map flag", "TeleportAlsoFlags travel"))
        {
            var teleFlags = _config.TeleportAlsoFlags;
            if (HuntUi.WrappedCheckbox("Teleport also drops the map flag", ref teleFlags))
            {
                _config.TeleportAlsoFlags = teleFlags;
                _config.Save();
            }
        }

        if (SettingMatches("Show how long ago each mark was last seen", "ShowMarkAge"))
        {
            var showAge = _config.ShowMarkAge;
            if (HuntUi.WrappedCheckbox("Show how long ago each mark was last seen", ref showAge))
            {
                _config.ShowMarkAge = showAge;
                _config.Save();
            }
        }

        DrawTrainNamePreferences();

        if (SettingMatches("Show spicing markers", "ShowSpicing"))
        {
            var spicing = _config.ShowSpicing;
            if (HuntUi.WrappedCheckbox("Show spicing markers", ref spicing))
            {
                _config.ShowSpicing = spicing;
                _config.Save();
            }
            ImGui.TextDisabled("A scout flagging a mark they'll prep before the train arrives.");
        }

        if (SettingMatches("Auto-advance to the next mark when the current one dies", "AutoAdvance following"))
        {
            var autoAdv = _config.AutoAdvance;
            if (HuntUi.WrappedCheckbox("Auto-advance to the next mark when the current one dies", ref autoAdv))
            {
                _config.AutoAdvance = autoAdv;
                _config.Save();
            }
        }

        if (_config.AutoAdvance || _drawingSettingsSearch)
        {
            if (SettingMatches("Echo and flag the mark it advances to", "EchoOnAdvance"))
            {
                var echoAdv = _config.EchoOnAdvance;
                if (HuntUi.WrappedCheckbox("Echo and flag the mark it advances to", ref echoAdv))
                {
                    _config.EchoOnAdvance = echoAdv;
                    _config.Save();
                }
            }

        }

        if (SettingMatches("Row padding (logical pixels)", "TrainRowHeight height layout"))
        {
            var rowH = Math.Clamp(_config.TrainRowHeight, 14, 48) - 14;
            ImGui.TextUnformatted("Row padding (logical pixels)");
            ImGui.SetNextItemWidth(Math.Min(120, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputInt("##Row padding", ref rowH))
            {
                _config.TrainRowHeight = Math.Clamp(rowH, 0, 34) + 14;
                _config.Save();
            }
        }

        if (SettingMatches("Detection interval (seconds)", "PollIntervalSeconds scan polling"))
        {
            var pollInterval = _config.PollIntervalSeconds;
            ImGui.TextUnformatted("Detection interval (seconds)");
            ImGui.SetNextItemWidth(Math.Min(120, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputInt("##Detection interval", ref pollInterval))
            {
                _config.PollIntervalSeconds = Math.Clamp(pollInterval, 1, 30);
                _config.Save();
            }
            ImGui.TextDisabled("How often marks are scanned for. Lower catches more while flying fast.");
            if (!_drawingSettingsSearch) ImGui.Spacing();
        }

        DrawSettingsHeading("S-rank train watches");
        if (SettingMatches("Remind me on entering an S-rank zone", "SRankZoneReminderEnabled watches"))
        {
            var reminderOn = _config.SRankZoneReminderEnabled;
            if (HuntUi.WrappedCheckbox("Remind me on entering an S-rank zone", ref reminderOn))
            {
                _config.SRankZoneReminderEnabled = reminderOn;
                _config.Save();
            }
        }

        if (!_drawingSettingsSearch && _config.SRankZoneReminderEnabled)
        {
            if (!_drawingSettingsSearch) HuntUi.SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("with bongo sound").X);
            var reminderSound = _config.SRankZoneReminderSound;
            if (HuntUi.WrappedCheckbox("with bongo sound", ref reminderSound))
            {
                _config.SRankZoneReminderSound = reminderSound;
                _config.Save();
            }
        }
        if (!_drawingSettingsSearch) ImGui.TextDisabled("Lakeland (Tyger), Ultima Thule (Narrow-rift), Elpis (Ophioneus), Yak T'el (Neyoozoteel). Only you see it.");

        if (!_drawingSettingsSearch) ImGui.Spacing();
        if (SettingMatches("Show these watches on the train list", "ShowSRankWatchesInTrainList"))
        {
            var watchesInList = _config.ShowSRankWatchesInTrainList;
            if (HuntUi.WrappedCheckbox("Show these watches on the train list", ref watchesInList))
            {
                _config.ShowSRankWatchesInTrainList = watchesInList;
                _config.Save();
            }

            if (!_drawingSettingsSearch) ImGui.Spacing();
        }

    }

    private void DrawTrainNamePreferences()
    {
        if (SettingMatches("Hide zone names in train rows", "HideZonesInPopout popout"))
        {
            var hideZones = _config.HideZonesInPopout;
            if (HuntUi.WrappedCheckbox("Hide zone names in train rows", ref hideZones))
            {
                _config.HideZonesInPopout = hideZones;
                _config.Save();
            }
        }

        if (SettingMatches("Show zone before mark in train rows", "SwapMarkAndZoneInPopout swap names popout"))
        {
            ImGui.BeginDisabled(_config.HideZonesInPopout);
            var swapNames = _config.SwapMarkAndZoneInPopout;
            if (HuntUi.WrappedCheckbox("Show zone before mark in train rows", ref swapNames))
            {
                _config.SwapMarkAndZoneInPopout = swapNames;
                _config.Save();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(_config.HideZonesInPopout ? "Show zone names to swap their position with mark names."
                    : "Show the zone first, followed by the mark name. Applies to /hh and /hht.");
        }

    }

    private void DrawCounterPreferences()
    {
        if (SettingMatches("Count only kills I land", "CountOnlyMyKills personal kill credit"))
        {
            var myKills = _config.CountOnlyMyKills;
            if (HuntUi.WrappedCheckbox("Count only kills I land", ref myKills))
            {
                _config.CountOnlyMyKills = myKills;
                _config.Save();
            }

            if (!_drawingSettingsSearch) ImGui.Spacing();
        }

    }

    private void DrawOccupiedSpawnPointSetting()
    {
        if (SettingMatches("Hide occupied spawn points", "HideOccupiedSpawnPoints"))
        {
            var hideOccupied = _config.HideOccupiedSpawnPoints;
            if (HuntUi.WrappedCheckbox("Hide occupied spawn points", ref hideOccupied))
            {
                _config.HideOccupiedSpawnPoints = hideOccupied;
                _config.DeferWindowStateSave();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hide the nearest matching spawn point while a live mark is shown within 2 map coordinates. Ambiguous matches stay visible. The point returns when the mark moves away, dies or is no longer visible.");
        }

    }

    private void DrawMissingMarkSpawnPointSetting()
    {
        if (SettingMatches("Dim points for found A-ranks", "DimFoundARankSpawnPoints"))
        {
            var dim = _config.DimFoundARankSpawnPoints;
            if (HuntUi.WrappedCheckbox("Dim points for found A-ranks", ref dim))
            {
                _config.DimFoundARankSpawnPoints = dim;
                _config.DeferWindowStateSave();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("While scanning, dim points that only living, non-sniped A-ranks already in this world's train can use, including their S-rank candidate rings. Dead or sniped rows do not count as found. Shared points stay normal if a possible mark is missing. Pausing or finding every A-rank alive in this zone/instance restores all points. Unknown locations and enabled B-rank points stay normal.");
        }

    }

    private void DrawMapPreferences()
    {
        if (SettingMatches("Show spawn points on the in-game map", "ShowSpawnPointsOnMap"))
        {
            var mapPoints = _config.ShowSpawnPointsOnMap;
            if (HuntUi.WrappedCheckbox("Show spawn points on the in-game map", ref mapPoints))
            {
                _config.ShowSpawnPointsOnMap = mapPoints;
                _config.Save();
            }
            ImGui.TextDisabled("Where a mark could be. A zone's B-rank points alone can run to sixty dots.");
        }

        DrawOccupiedSpawnPointSetting();
        DrawMissingMarkSpawnPointSetting();

        if (SettingMatches("Show live marks on the in-game map", "ShowMarksOnMap"))
        {
            var mapMarks = _config.ShowMarksOnMap;
            if (HuntUi.WrappedCheckbox("Show live marks on the in-game map", ref mapMarks))
            {
                _config.ShowMarksOnMap = mapMarks;
                _config.Save();
            }

            ImGui.TextDisabled(_mapOverlay.Status);
        }

        if (SettingMatches("Show a control bar above the map", "ShowMapControlBar"))
        {
            var bar = _config.ShowMapControlBar;
            if (HuntUi.WrappedCheckbox("Show a control bar above the map", ref bar))
            {
                _config.ShowMapControlBar = bar;
                _config.Save();
            }
            ImGui.TextDisabled("These same toggles, pinned to the top of the game's map and shown with it. Also /htrm.");
        }

        if (_drawingSettingsSearch || _config.ShowSpawnPointsOnMap || _config.ShowMarksOnMap)
        {
            if (_drawingSettingsSearch || _config.ShowSpawnPointsOnMap)
            {
                if (SettingMatches("A-rank spawn points", "ShowARankPoints"))
                {
                    var showA = _config.ShowARankPoints;
                    if (HuntUi.WrappedCheckbox("A-rank points", ref showA))
                    {
                        _config.ShowARankPoints = showA;
                        _config.Save();
                    }
                }

                if (!_drawingSettingsSearch) HuntUi.SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("B-rank").X);
                if (SettingMatches("B-rank spawn points", "ShowBRankPoints"))
                {
                    var showB = _config.ShowBRankPoints;
                    if (HuntUi.WrappedCheckbox("B-rank##points", ref showB))
                    {
                        _config.ShowBRankPoints = showB;
                        _config.Save();
                    }
                }

                if (!_drawingSettingsSearch) HuntUi.SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("B-rank").X);
                if (SettingMatches("S-rank spawn points", "ShowSRankPoints"))
                {
                    var showS = _config.ShowSRankPoints;
                    if (HuntUi.WrappedCheckbox("S-rank##points", ref showS))
                    {
                        _config.ShowSRankPoints = showS;
                        _config.Save();
                    }
                }

            }

            if (_drawingSettingsSearch || _config.ShowMarksOnMap)
            {
                if (SettingMatches("A-rank live marks", "ShowARankMarks"))
                {
                    var markA = _config.ShowARankMarks;
                    if (HuntUi.WrappedCheckbox("A-rank marks", ref markA))
                    {
                        _config.ShowARankMarks = markA;
                        _config.Save();
                    }
                }

                if (!_drawingSettingsSearch) HuntUi.SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("B-rank").X);
                if (SettingMatches("B-rank live marks", "ShowBRankMarks"))
                {
                    var markB = _config.ShowBRankMarks;
                    if (HuntUi.WrappedCheckbox("B-rank##marks", ref markB))
                    {
                        _config.ShowBRankMarks = markB;
                        _config.Save();
                    }
                }

                if (!_drawingSettingsSearch) HuntUi.SameLineIfFits(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("B-rank").X);
                if (SettingMatches("S-rank live marks", "ShowSRankMarks"))
                {
                    var markS = _config.ShowSRankMarks;
                    if (HuntUi.WrappedCheckbox("S-rank##marks", ref markS))
                    {
                        _config.ShowSRankMarks = markS;
                        _config.Save();
                    }
                }

            }

            if (SettingMatches("Alt-click a spawn point on the map to flag it", "ClickSpawnPointToFlag"))
            {
                var clickFlag = _config.ClickSpawnPointToFlag;
                if (HuntUi.WrappedCheckbox("Alt-click a spawn point on the map to flag it", ref clickFlag))
                {
                    _config.ClickSpawnPointToFlag = clickFlag;
                    _config.Save();
                }
            }

            if (SettingMatches("Mark SS event minion locations", "ShowSsEventOnMap"))
            {
                var ssEvent = _config.ShowSsEventOnMap;
                if (HuntUi.WrappedCheckbox("Mark SS event minion locations", ref ssEvent))
                {
                    _config.ShowSsEventOnMap = ssEvent;
                    _config.Save();
                }

                ImGui.TextDisabled(_ssEvent.Status);
            }

            if (SettingMatches("Write mark names and health on the map", "ShowMarkLabelsOnMap HP"))
            {
                var labels = _config.ShowMarkLabelsOnMap;
                if (HuntUi.WrappedCheckbox("Write mark names and health on the map", ref labels))
                {
                    _config.ShowMarkLabelsOnMap = labels;
                    _config.Save();
                }
            }

            if (_drawingSettingsSearch || _config.ShowMarkLabelsOnMap)
            {
                if (SettingMatches("Name text size", "MarkLabelFontSize font"))
                {
                    var fontSize = _config.MarkLabelFontSize;
                    ImGui.SetNextItemWidth(90);
                    if (ImGui.InputFloat(HuntUi.FieldLabel("Name text size", 120), ref fontSize, 1f))
                    {
                        _config.MarkLabelFontSize = Math.Clamp(fontSize, 6f, 48f);
                        _config.Save();
                    }
                }

            }

            if (!_drawingSettingsSearch) ImGui.Spacing();
            if (_drawingSettingsSearch || ImGui.CollapsingHeader("Dot colours")) DrawDotColours();
            if (!_drawingSettingsSearch) ImGui.Spacing();

            if (SettingMatches("Dark outlines around map dots", "OutlineMapDots"))
            {
                var darkOutlines = _config.OutlineMapDots;
                if (HuntUi.WrappedCheckbox("Dark outlines around map dots", ref darkOutlines))
                {
                    _config.OutlineMapDots = darkOutlines;
                    _config.Save();
                }
                ImGui.TextWrapped("Adds contrast on pale maps. Off by default. When enabled, S-rank candidate rings sit outside the dot and stay clearer while scouting.");
            }

            if (SettingMatches("Dot size", "SpawnDotSize"))
            {
                var dotSize = _config.SpawnDotSize;
                ImGui.SetNextItemWidth(90);
                if (ImGui.InputFloat(HuntUi.FieldLabel("Dot size", 120), ref dotSize, 2f))
                {
                    _config.SpawnDotSize = Math.Clamp(dotSize, 6f, 48f);
                    _config.Save();
                }
            }

        }

        if (!_drawingSettingsSearch) ImGui.Spacing();
        if (!_drawingSettingsSearch) ImGui.Separator();
        if (!_drawingSettingsSearch) ImGui.Spacing();
        if (_drawingSettingsSearch || ImGui.CollapsingHeader("Player guides")) DrawPlayerGuideSettings();
        if (!_drawingSettingsSearch) ImGui.Spacing();
    }

    private void DrawTravelPreferences()
    {
        if (!SettingMatches("Aetheryte blacklist", "BlacklistedAetherytes travel teleport exclude zone expansion")) return;
        ImGui.TextWrapped("Aetheryte blacklist — never route to these.");
        ImGui.TextDisabled("Affects the teleport button and Next Aetheryte alike.");
        if (!_drawingSettingsSearch) ImGui.Spacing();
        DrawBlacklistPicker();
        if (!_drawingSettingsSearch) ImGui.Spacing();
    }

    private void DrawDiscordPreferences()
    {
        if (SettingMatches("Discord destinations", "webhook URL label enabled add remove")) DrawWebhookList();
        if (!_drawingSettingsSearch) ImGui.Spacing();
        if (SettingMatches("Send Discord test message", "webhook") && ImGui.Button("Send test message")) _ = SendTestAsync();
        if (!_drawingSettingsSearch) ImGui.TextDisabled("Posts to enabled webhooks.");
    }

    private void DrawConnectionPreferences()
    {
        if (!_drawingSettingsSearch) ImGui.Spacing();
        if (SettingMatches("Enable server sharing", "SyncEnabled sync connection"))
        {
            var enabled = _config.SyncEnabled;
            if (HuntUi.WrappedCheckbox("Enabled", ref enabled))
            {
                _config.SyncEnabled = enabled;
                _config.Save();
                _sync.ApplySettings();
            }
        }

        if (SettingMatches("Allow unencrypted development connections", "SyncAllowPlaintext plaintext ws"))
        {
            var plaintext = _config.SyncAllowPlaintext;
            if (HuntUi.WrappedCheckbox("Allow unencrypted development connections",ref plaintext))
            { _config.SyncAllowPlaintext=plaintext;_config.Save();_sync.ApplySettings(); }
            if (_config.SyncAllowPlaintext) ImGui.TextWrapped("ws:// exposes the group password and shared data. Use only on a trusted development network.");
        }

        if (SettingMatches("Server URL", "SyncServerUrl wss address"))
        {
            ImGui.TextUnformatted("Server URL");
            ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
            var url = _config.SyncServerUrl;
            if (ImGui.InputTextWithHint("##Server URL", "wss://hunts.example.com/ws", ref url, 512))
                _config.SyncServerUrl = url;
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _config.Save();
                _sync.ApplySettings();
            }
        }

        if (SettingMatches("Password", "SyncPassword credentials"))
        {
            ImGui.TextUnformatted("Password");
            ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
            var password = _config.SyncPassword;
            var passwordFlags = _showSyncPassword ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
            if (ImGui.InputText("##Password", ref password, 256, passwordFlags))
                _config.SyncPassword = password;
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _config.Save();
                _sync.ApplySettings();
            }
            WorkspaceSameLine("show", true);
            HuntUi.WrappedCheckbox("show", ref _showSyncPassword);
        }

        if (SettingMatches("Display name", "SyncDisplayName alias"))
        {
            ImGui.TextUnformatted("Display name");
            ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
            var name = _config.SyncDisplayName;
            if (ImGui.InputTextWithHint("##Display name", "Anonymous", ref name, 40))
                _config.SyncDisplayName = name;
            if (ImGui.IsItemDeactivatedAfterEdit())
                _config.Save();
            if (ImGui.IsItemDeactivatedAfterEdit()) _sync.ApplySettings();
            ImGui.TextDisabled("Your chosen alias is shared. Blank uses Anonymous.");
        }

        if (!_drawingSettingsSearch) ImGui.Spacing();
        if (SettingMatches("Connection status and reconnect", "Faloop Bear feed coverage online server")) ConnectionUi.DrawDetails(_config, _sync);

        if (_drawingSettingsSearch || _sync.IsConnected)
        {
            if ((_drawingSettingsSearch || _sync.LocalBackupCount > 0 && _config.SyncShareTrain)
                && SettingMatches("Upload saved local marks", "train backup recovery"))
            {
                ImGui.BeginDisabled(!_sync.IsConnected || !_config.SyncShareTrain || _sync.LocalBackupCount == 0);
                if (ImGui.Button($"Upload saved local marks ({_sync.LocalBackupCount})")) _sync.UploadLocalBackup();
                ImGui.EndDisabled();
            }
            if ((_drawingSettingsSearch || _sync.LocalWatchBackupCount > 0 && _config.SyncShareTrain)
                && SettingMatches("Upload saved local watches", "train backup recovery"))
            {
                ImGui.BeginDisabled(!_sync.IsConnected || !_config.SyncShareTrain || _sync.LocalWatchBackupCount == 0);
                if (ImGui.Button($"Upload saved local watches ({_sync.LocalWatchBackupCount})")) _sync.UploadWatchBackup();
                ImGui.EndDisabled();
            }
            if (!_drawingSettingsSearch) ImGui.TextDisabled("Joining uses the server train. Upload saved local marks explicitly if needed.");
            if (SettingMatches("Online now", "connected scouts group members"))
            {
                ImGui.TextWrapped("Online now:");
                if (!_sync.IsConnected) ImGui.TextDisabled("Connect to see online scouts.");
                foreach (var client in _sync.Clients)
                {
                    var where = client.TerritoryId != 0
                        ? $" — {_detector.GetZoneName(client.TerritoryId)}{ExpansionData.InstanceGlyph(client.Instance)}" : string.Empty;
                    var world = client.WorldId != 0 ? $" [{_worldData.NameOf(client.WorldId)}]" : string.Empty;
                    ImGui.BulletText($"{client.Name}{world}{where}");
                }
            }
        }

        if (!_drawingSettingsSearch) ImGui.Spacing();
    }

    private void DrawSharingPreferences()
    {
        if (SettingMatches("Share the train", "SyncShareTrain group"))
        {
            var train = _config.SyncShareTrain;
            if (HuntUi.WrappedCheckbox("The train", ref train))
            {
                _config.SyncShareTrain = train;
                _config.Save();
                _sync.ApplySettings();
            }
        }

        if (SettingMatches("Receive Bear Toolkit S-rank reports", "SyncReceiveBearFeed HP opt out"))
        {
            var bear = _config.SyncReceiveBearFeed;
            if (HuntUi.WrappedCheckbox("Receive Bear Toolkit S-rank reports", ref bear))
            {
                _config.SyncReceiveBearFeed = bear;
                _config.Save();
                _sync.ApplySettings();
            }
            ImGui.TextDisabled("Enabled by default. Turn off to hide Bear S-rank reports and HP.");
            ImGui.TextDisabled("Requires Bear enabled on the server. Reports stay separate from your train.");
        }

        if (SettingMatches("Share what I can see", "SyncShareSightings observations"))
        {
            var sightings = _config.SyncShareSightings;
            if (HuntUi.WrappedCheckbox("What I can see", ref sightings))
            {
                _config.SyncShareSightings = sightings;
                _config.Save();
            }
        }

        if (SettingMatches("Share S-rank kills I witness", "SyncReportSRankKills"))
        {
            var kills = _config.SyncReportSRankKills;
            if (HuntUi.WrappedCheckbox("S-rank kills I witness", ref kills))
            {
                _config.SyncReportSRankKills = kills;
                _config.Save();
            }
            ImGui.TextDisabled("The exact moment an S dies in front of you starts the group's clock for it.");
            if (_standaloneTallyPresent)
                ImGui.TextWrapped("The built-in tally is disabled while the separate Hunt Tally plugin is installed.");
        }

    }

    private void DrawRemoteMapPreferences()
    {
        if (SettingMatches("Marks other members can see on my map", "SyncShowRemoteMarksOnMap"))
        {
            var remote = _config.SyncShowRemoteMarksOnMap;
            if (HuntUi.WrappedCheckbox("Marks other members can see, on my map", ref remote))
            {
                _config.SyncShowRemoteMarksOnMap = remote;
                _config.Save();
            }
        }

        if (SettingMatches("Which spawn points the S can still use", "ShowSRankCandidatesOnMap mapping"))
        {
            var candidates = _config.ShowSRankCandidatesOnMap;
            if (HuntUi.WrappedCheckbox("Which spawn points the S can still use", ref candidates))
            {
                _config.ShowSRankCandidatesOnMap = candidates;
                _config.Save();
            }
        }

        const ImGuiColorEditFlags flags = ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf;
        if (SettingMatches("S candidate outline width", "SpawnCandidateOutlineWidth"))
        {
            var outlineWidth = _config.SpawnCandidateOutlineWidth;
            if (ImGui.SliderInt(HuntUi.FieldLabel("S candidate outline width"), ref outlineWidth, 1, 12, "%d"))
            {
                _config.SpawnCandidateOutlineWidth = outlineWidth;
                _config.Save();
            }
        }

        if (SettingMatches("S candidate outline / confirmed fill", "SpawnDotColourSCandidate colour color"))
        {
            var candidate = _config.SpawnDotColourSCandidate;
            if (ImGui.ColorEdit4(HuntUi.FieldLabel("S candidate outline / confirmed fill"), ref candidate, flags))
            {
                _config.SpawnDotColourSCandidate = candidate;
                _config.Save();
            }
        }

    }

    private void DrawBongoSoundPreferences()
    {
        if (SettingMatches("S-rank zone-entry reminder sound", "SRankZoneReminderSound bongo zone-entry reminders"))
        {
            var reminderSound = _config.SRankZoneReminderSound;
            if (HuntUi.WrappedCheckbox("S-rank zone-entry reminders", ref reminderSound))
            {
                _config.SRankZoneReminderSound = reminderSound;
                _config.Save();
            }
        }

        if (SettingMatches("Community S-rank spawn/release alert sound", "SyncSpawnSound bongo"))
        {
            var spawnSound = _config.SyncSpawnSound;
            if (HuntUi.WrappedCheckbox("Community S-rank spawn/release alerts", ref spawnSound))
            {
                _config.SyncSpawnSound = spawnSound;
                _config.Save();
            }
            ImGui.TextWrapped("Turn off either sound to keep its notification without the bongo.");
        }

    }

    private Sync.MarkScopeRuleEditor? _relayRuleEditor;

    private void DrawCommunityAlertPreferences()
    {
        if (SettingMatches("Chat alerts for group S sightings and Faloop spawns/releases", "SyncSpawnAlerts relay"))
        {
            var alerts = _config.SyncSpawnAlerts;
            if (HuntUi.WrappedCheckbox("Chat alerts for group S sightings and Faloop spawns/releases", ref alerts))
            { _config.SyncSpawnAlerts = alerts; _config.Save(); }
        }

        if (SettingMatches("S-rank relay rules and presets", "SyncRelayRules saved presets load save rename delete copy hhv world DC data centre expansion ShB Shadowbringers Endwalker Dawntrail"))
        {
            if (_config.SyncRelayRules is null)
            {
                _config.SyncRelayRules = Sync.RelayScopeFilter.MigrateLegacy(_config.SyncSpawnCurrentDc, _config.SyncSpawnDataCenters);
                _config.Save();
            }
            _relayRuleEditor ??= new(_worldData, _detector.CurrentWorldId, _config.Save,
                () => _config.RelayPresetId, id => _config.RelayPresetId = id, relayOnly: true);
            ImGui.TextWrapped("S-rank relays matching any enabled rule appear in chat. These rules also control the relay sound.");
            if (ImGui.Button("Copy /hhv S-rank rules"))
            {
                _config.SyncRelayRules = Sync.RelayScopeFilter.FromActiveMarks(_config.VisibleMarkFilters);
                _config.RelayPresetId = null;
                _relayRuleEditor.Reset(); _config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Replace chat rules with a separate copy of the S-rank world/DC and expansion rules in /hhv. Later edits remain independent.");
            if (!_drawingSettingsSearch) HuntUi.SameLineIfFits(HuntUi.ButtonWidth("Reset relay rules"));
            if (ImGui.Button("Reset relay rules"))
            {
                _config.SyncRelayRules = Sync.RelayScopeFilter.MigrateLegacy(true, null);
                _config.RelayPresetId = null;
                _relayRuleEditor.Reset(); _config.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Restore all S-rank relays in your current DC, across every expansion.");
            if (!_drawingSettingsSearch) ImGui.Spacing();
            _relayRuleEditor.Draw(_config.SyncRelayRules, _config.RelayPresets ??= new());
        }

        if (!_drawingSettingsSearch) ImGui.Separator();
        if (SettingMatches("Test relay chat format", "local TEST notifications"))
        {
            ImGui.TextWrapped("Server coverage: " + string.Join(", ", _sync.Faloop.DataCenters));
            if (ImGui.Button("Test chat format"))
                ShowSpawnAlert(new Sync.SRankSpawnBroadcast { NameId = Sync.SRankTimerData.All[0].NameId,
                    WorldId = _detector.CurrentWorldId(), SpawnedAt = DateTime.UtcNow, X = 21.5f, Y = 21.5f, Source = "Local test" }, test: true);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show a local TEST example, ignoring relay rules and the chat-alert switch. Sends no report and does not consume a real spawn's notification.");
            ImGui.TextWrapped(_lastCommunityAlert);
            ImGui.TextDisabled($"Last server feed message: {_sync.Faloop.LastLiveMessageAt?.ToLocalTime().ToString("HH:mm:ss") ?? "none"}; last broadcast alert: {_sync.Faloop.LastAlertAt?.ToLocalTime().ToString("HH:mm:ss") ?? "none"}");
        }

    }
}
