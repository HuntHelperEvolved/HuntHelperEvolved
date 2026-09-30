using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntTally;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private SettingsResetCategory? _pendingSettingsReset;
    private string _settingsResetStatus = string.Empty;

    private bool CanResetTallySettings => !_standaloneTallyPresent
        && TallyConfigStore.SuspendedReason is null;

    private static string SettingsResetLabel(SettingsResetCategory category) => category switch
    {
        SettingsResetCategory.AllPreferences => "All preferences",
        SettingsResetCategory.Train => "Train behavior and layout",
        SettingsResetCategory.Counters => "S-rank counters",
        SettingsResetCategory.Travel => "Travel and aetherytes",
        SettingsResetCategory.MapDisplay => "Map display",
        SettingsResetCategory.MapColours => "Map colours",
        SettingsResetCategory.PlayerGuides => "Player guides",
        SettingsResetCategory.DetectionNotifications => "Mark detection alerts",
        SettingsResetCategory.CommunityNotifications => "S-rank reminders and community alerts",
        SettingsResetCategory.Sharing => "Sharing preferences",
        SettingsResetCategory.HuntWindows => "Active Marks and A/S-rank filters",
        SettingsResetCategory.Tally => "Hunt tally preferences",
        SettingsResetCategory.General => "Update notifications",
        SettingsResetCategory.SyncConnection => "Saved sync connection",
        SettingsResetCategory.DiscordWebhooks => "Discord destinations",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private static string SettingsResetDescription(SettingsResetCategory category) => category switch
    {
        SettingsResetCategory.AllPreferences => "Restore the preference groups below to this version's defaults. "
            + "Sync is turned off. Saved connection details and Discord destinations are kept.",
        SettingsResetCategory.Train => "Restore polling, death marking, train rows, popout layout, chat echoes, "
            + "watches and expansion grouping. Saved expansion-order preferences and folded groups are reset; marks and presets are kept.",
        SettingsResetCategory.Counters => "Restore kill-counting and per-mark auto-reset preferences. Current counts are kept.",
        SettingsResetCategory.Travel => "Restore map flagging and the default aetheryte blacklist: "
            + "The Macarenses Angle, Base Omicron and Many Fires.",
        SettingsResetCategory.MapDisplay => "Restore spawn points, live marks, rank filters, dark dot outlines, dot and label sizes, "
            + "map controls, shared sightings and S-rank mapping. Colours and player guides have their own resets.",
        SettingsResetCategory.MapColours => "Restore spawn-point, mark, label, SS-event and S-rank mapping colours.",
        SettingsResetCategory.PlayerGuides => "Restore the detection ring, heading line, position dot and projected path, "
            + "including their sizes and colours. Individual guides return to off.",
        SettingsResetCategory.DetectionNotifications => "Restore detection chat, fly text and speech, including rank selections, "
            + "message templates, voice and volume. All three notification channels return to off.",
        SettingsResetCategory.CommunityNotifications => "Restore zone-entry reminders, community spawn alerts, "
            + "their bongo sounds and data-centre selections. Sounds return to on; spawn alerts follow your current data centre.",
        SettingsResetCategory.Sharing => "Turn sync off and restore sharing preferences. Your server address, password "
            + "and display name are kept. The group's train is not changed.",
        SettingsResetCategory.HuntWindows => "Restore Active Marks filters and A/S-rank world, expansion, availability "
            + "and condition filters. Clear searches. Open windows and recorded sightings are kept.",
        SettingsResetCategory.Tally => "Restore kill-credit rules, tracked ranks, chat, integration settings, "
            + "automatic achievement seeding and the history limit. Character totals and achievement baselines are kept.",
        SettingsResetCategory.General => "Restore automatic display of release notes after an update.",
        SettingsResetCategory.SyncConnection => "Turn sync off and remove the saved server address, password and display name. "
            + "You will need to enter them again to reconnect. Your train and local recovery copies are kept.",
        SettingsResetCategory.DiscordWebhooks => "Remove every saved Discord webhook URL and label, including disabled destinations. "
            + "You will need to add a destination again before sending reports. Saved reports are kept.",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private void DrawSettingsResets()
    {
        ImGui.TextWrapped("Restore defaults by category. Your train, presets, counters and tally totals are kept.");
        ImGui.Spacing();
        if (!string.IsNullOrEmpty(_settingsResetStatus))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, HuntTheme.Success);
            ImGui.TextWrapped(_settingsResetStatus);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        DrawSettingsResetRow(SettingsResetCategory.AllPreferences);
        DrawSettingsResetGroup("Hunting", true,
            SettingsResetCategory.Train, SettingsResetCategory.Counters, SettingsResetCategory.Travel);
        DrawSettingsResetGroup("Map appearance", true,
            SettingsResetCategory.MapDisplay, SettingsResetCategory.MapColours, SettingsResetCategory.PlayerGuides);
        DrawSettingsResetGroup("Notifications", true,
            SettingsResetCategory.DetectionNotifications, SettingsResetCategory.CommunityNotifications, SettingsResetCategory.General);
        DrawSettingsResetGroup("Windows and tally", true,
            SettingsResetCategory.HuntWindows, SettingsResetCategory.Tally);
        DrawSettingsResetGroup("Sharing", true, SettingsResetCategory.Sharing);
        DrawSettingsResetGroup("Clear saved connections", false,
            SettingsResetCategory.SyncConnection, SettingsResetCategory.DiscordWebhooks);

        DrawSettingsResetConfirmation();
    }

    private void DrawSettingsResetGroup(string heading, bool initiallyOpen, params SettingsResetCategory[] categories)
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader(heading, initiallyOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) return;
        foreach (var category in categories) DrawSettingsResetRow(category);
    }

    private string? SettingsResetBlockedReason(SettingsResetCategory category)
    {
        if (category == SettingsResetCategory.Tally && !CanResetTallySettings)
            return "Tally settings cannot be reset while the built-in tally is unavailable. See Settings > Tally.";
        if (TrainMutationBusy && (category is SettingsResetCategory.AllPreferences or SettingsResetCategory.Train
            or SettingsResetCategory.Sharing or SettingsResetCategory.SyncConnection or SettingsResetCategory.DiscordWebhooks))
            return "Wait for the current train operation to finish.";
        return null;
    }

    private void DrawSettingsResetRow(SettingsResetCategory category)
    {
        ImGui.PushID(category.ToString());
        ImGui.Spacing();
        var scale = ImGui.GetFontSize() / 17f;
        var useColumns = ImGui.GetContentRegionAvail().X >= 440 * scale
            && ImGui.BeginTable("resetRow", 2, ImGuiTableFlags.SizingStretchProp);
        if (useColumns)
        {
            ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 105 * scale);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
        }

        ImGui.TextWrapped(SettingsResetLabel(category));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(SettingsResetDescription(category));
        ImGui.PopStyleColor();
        if (category == SettingsResetCategory.AllPreferences && !CanResetTallySettings)
            ImGui.TextWrapped("Built-in tally preferences are unavailable and will be skipped.");
        var blocked = SettingsResetBlockedReason(category);
        if (blocked is not null) ImGui.TextWrapped(blocked);

        if (useColumns) ImGui.TableSetColumnIndex(1);
        else ImGui.Spacing();
        ImGui.BeginDisabled(blocked is not null);
        if (ImGui.Button(category is SettingsResetCategory.SyncConnection or SettingsResetCategory.DiscordWebhooks
                ? "Clear..." : "Reset...", new Vector2(100 * scale, 0)))
            _pendingSettingsReset = category;
        ImGui.EndDisabled();
        if (useColumns) ImGui.EndTable();
        ImGui.Spacing();
        ImGui.PopID();
    }

    private void DrawSettingsResetConfirmation()
    {
        const string popup = "Reset settings?";
        // Open outside the row's ID scope so every category uses the same modal.
        if (_pendingSettingsReset is not { } category) return;
        if (!ImGui.IsPopupOpen(popup)) ImGui.OpenPopup(popup);
        var scale = ImGui.GetFontSize() / 17f;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(520 * scale, ImGui.GetIO().DisplaySize.X - 40 * scale), 0));
        var open = true;
        if (ImGui.BeginPopupModal(popup, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextWrapped(SettingsResetLabel(category));
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped(SettingsResetDescription(category));
            if (CanResetTallySettings && (category is SettingsResetCategory.Tally or SettingsResetCategory.AllPreferences))
            {
                ImGui.Spacing();
                ImGui.TextWrapped($"The tally history limit returns to {new HuntTally.Configuration().HistoryLimit:N0} entries per character. "
                    + "Older detail entries may be trimmed as new kills are recorded.");
            }
            if (category == SettingsResetCategory.AllPreferences && !CanResetTallySettings)
                ImGui.TextWrapped("Built-in tally preferences will be skipped.");
            var blocked = SettingsResetBlockedReason(category);
            if (blocked is not null) ImGui.TextWrapped(blocked);
            ImGui.Spacing();
            ImGui.BeginDisabled(blocked is not null);
            if (ImGui.Button(category is SettingsResetCategory.SyncConnection or SettingsResetCategory.DiscordWebhooks
                    ? "Clear saved details" : "Restore defaults"))
            {
                ApplySettingsReset(category);
                _pendingSettingsReset = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _pendingSettingsReset = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (!open) _pendingSettingsReset = null;
    }

    private void ApplySettingsReset(SettingsResetCategory category)
    {
        var includeTally = CanResetTallySettings;
        SettingsReset.Apply(_config, _tallyConfig, category, includeTally);
        if (category is SettingsResetCategory.Train or SettingsResetCategory.AllPreferences)
        {
            ClearTrainDrag();
            ResetTrainExpansionProgress();
        }
        if (category is SettingsResetCategory.HuntWindows or SettingsResetCategory.AllPreferences)
        {
            _activeMarksWindow.OnSettingsReset();
            _arankWindow.OnSettingsReset();
            _srankWindow.OnSettingsReset();
        }
        _config.Save();
        if (category is SettingsResetCategory.Sharing or SettingsResetCategory.SyncConnection or SettingsResetCategory.AllPreferences)
            _sync.ApplySettings();
        if (includeTally && (category is SettingsResetCategory.Tally or SettingsResetCategory.AllPreferences))
        {
            _tallyConfig.MarkChanged();
            _tallyConfig.Flush(force: true);
        }
        _settingsResetStatus = category is SettingsResetCategory.SyncConnection or SettingsResetCategory.DiscordWebhooks
            ? $"{SettingsResetLabel(category)} cleared."
            : $"{SettingsResetLabel(category)} restored to defaults.";
        if (category == SettingsResetCategory.AllPreferences && !includeTally)
            _settingsResetStatus += " Built-in tally preferences were skipped.";
    }
}
