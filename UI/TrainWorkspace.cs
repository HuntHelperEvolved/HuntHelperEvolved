using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using HuntHelperEvolved.TrainPresets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private enum TrainWorkspacePage { Route, Reports, Setup }
    private TrainWorkspacePage _trainWorkspacePage;
    private TrainWorkspacePage _compactTrainPanel;
    private bool _trainCompletionReport;
    private readonly ScoutNoteDraft _scoutNote = new();
    private ScoutNoteDraft.UndoCapture? _scoutNoteUndo;
    private DateTime? _scoutNoteUndoAt;
    private bool _showTrainUndoNotice;
    private PreparedDiscordReport? _trainReportPreview;
    private IReadOnlyList<DiscordEmbedPreview> _trainReportDisplay = Array.Empty<DiscordEmbedPreview>();
    private string _trainPreviewError = string.Empty;
    private int? _trainPreviewFingerprint;
    private DateTime _nextTrainPreviewUpdate;
    private DateTime _trainReportsDrawnAt;
    private DateTime _trainPreviewAt;
    private int _trainReportDestinationCount;
    private bool TrainMutationBusy => _reportPostBusy || _completion.IsBusy;

    private void SelectTrainPage(TrainWorkspacePage page)
    {
        _trainWorkspacePage = page;
        _nextTrainPreviewUpdate = DateTime.MinValue;
    }

    private void SelectTrainPanel(TrainWorkspacePage page, bool compact)
    {
        if (compact) _compactTrainPanel = page;
        else SelectTrainPage(page);
        _nextTrainPreviewUpdate = DateTime.MinValue;
    }

    private void DrawTrainWorkspace(bool popout)
    {
        DrawTrainToolbar();
        DrawTrainContext();
        DrawTrainWorkspaceBody(popout);
    }

    private float TrainSecondaryWidth(bool compact)
    {
        var gap = ImGui.GetStyle().ItemSpacing.X;
        return HuntUi.ButtonWidth("Plan", compact ? null : FontAwesomeIcon.Route)
            + HuntUi.ButtonWidth("Reports", compact ? null : FontAwesomeIcon.PaperPlane)
            + (compact ? HuntUi.ButtonWidth("Import", FontAwesomeIcon.Clipboard) : ImGui.GetFrameHeight())
            + ImGui.GetFrameHeight() + gap * 3;
    }

    private static void TrainControlSameLine(float width)
    {
        if (ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X + ImGui.GetStyle().ItemSpacing.X + width
            <= ImGui.GetWindowContentRegionMax().X) ImGui.SameLine();
    }

    private static float TrainModeControlsWidth(bool compact) =>
        (compact ? ImGui.GetFrameHeight() : HuntUi.ButtonWidth("Scouting", FontAwesomeIcon.Pause))
        + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X
        + ImGui.CalcTextSize("Follow").X;

    private static float TrainNavigationControlsWidth(bool compact) =>
        HuntUi.ButtonWidth("Next mark", compact ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.StepForward)
        + ImGui.GetStyle().ItemSpacing.X + (compact ? HuntUi.ButtonWidth("Next Aetheryte") : ImGui.GetFrameHeight());

    private static float TrainOperatingControlsWidth(bool compact) => TrainNavigationControlsWidth(compact)
        + (compact ? 16 : 24) * ImGuiHelpers.GlobalScale + TrainModeControlsWidth(compact);

    private void DrawTrainToolbar()
    {
        var start = ImGui.GetCursorPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padding = 6 * ImGuiHelpers.GlobalScale;
        var primaryWidth = TrainOperatingControlsWidth(compact: false);
        var secondaryWidth = TrainSecondaryWidth(compact: false);
        var inline = primaryWidth + secondaryWidth + padding * 3 <= width;
        var rows = inline ? 1 : primaryWidth + padding * 2 > width ? 3 : 2;
        var height = ImGui.GetFrameHeight() * rows + padding * 2
            + ImGui.GetStyle().ItemSpacing.Y * (rows - 1);
        HuntUi.FillBand(height, HuntTheme.Surface);
        ImGui.SetCursorPos(start + new Vector2(padding));
        DrawTrainOperatingControls(compact: false, width - padding * 2 - (inline ? secondaryWidth + padding : 0));
        if (inline)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(start.X + width - padding - secondaryWidth);
        }
        else ImGui.SetCursorPosX(start.X + padding);
        DrawTrainSecondaryControls(compact: false);
        ImGui.SetCursorPos(new Vector2(start.X, Math.Max(start.Y + height, ImGui.GetCursorPosY())));
    }

    private void DrawTrainOperatingControls(bool compact, float width)
    {
        var start = ImGui.GetCursorPos();
        var inline = TrainOperatingControlsWidth(compact) <= width;
        if (HuntUi.Button("next-mark", "Next mark", compact ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.StepForward,
            primary: true, tooltip: "Move to the next live mark and flag it")) SetCurrentMark(NextLiveMark(), announce: true);
        TrainControlSameLine(compact ? HuntUi.ButtonWidth("Next Aetheryte") : ImGui.GetFrameHeight());
        const string aetheryteTip = "Announce the next aetheryte without teleporting or changing the current map flag";
        var nextAetheryte = compact
            ? HuntUi.Button("next-aetheryte", "Next Aetheryte", tooltip: aetheryteTip)
            : DrawAetheryteButton("next-aetheryte", aetheryteTip);
        if (nextAetheryte)
            OnNextAetheryteCommand(NextAetheryteCommand, string.Empty);
        if (inline)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(start.X + width - TrainModeControlsWidth(compact));
            var divider = ImGui.GetCursorScreenPos() - new Vector2((compact ? 8 : 12) * ImGuiHelpers.GlobalScale, 0);
            ImGui.GetWindowDrawList().AddLine(divider, divider + new Vector2(0, ImGui.GetFrameHeight()),
                ImGui.GetColorU32(HuntTheme.Line));
        }
        else ImGui.SetCursorPosX(start.X + Math.Max(0, width - TrainModeControlsWidth(compact)));
        DrawTrainModeControls(compact);
    }

    private void DrawTrainModeControls(bool compact)
    {
        if (compact)
        {
            DrawTrainFollowControl();
            ImGui.SameLine();
        }
        ImGui.BeginDisabled(TrainMutationBusy);
        var scoutIcon = _config.ScanningPaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var scoutTip = (_config.ScanningPaused ? "Scouting paused - resume" : "Scouting - pause")
            + "\nControls new scouting records and automatic scout credit. Existing deaths still update while paused.";
        var scoutPressed = compact ? HuntUi.IconButton("scouting", scoutIcon, scoutTip, !_config.ScanningPaused)
            : HuntUi.Button("scouting", _config.ScanningPaused ? "Paused" : "Scouting", scoutIcon,
                selected: !_config.ScanningPaused, size: new Vector2(HuntUi.ButtonWidth("Scouting", FontAwesomeIcon.Pause), 0), tooltip: scoutTip);
        if (scoutPressed)
        {
            _config.ScanningPaused = !_config.ScanningPaused;
            _config.Save();
        }
        ImGui.EndDisabled();
        if (!compact)
        {
            ImGui.SameLine();
            DrawTrainFollowControl();
        }
    }

    private void DrawTrainFollowControl()
    {
        var follow = _config.FollowTrain;
        if (ImGui.Checkbox("Follow", ref follow)) SetFollowTrain(follow);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Automatic train flags and announcements on this client. Scouting and sharing are separate.");
    }

    private bool DrawAetheryteButton(string id, string tooltip, Vector2? size = null)
    {
        var dimensions = size ?? new Vector2(ImGui.GetFrameHeight());
        bool clicked;
        if (_textureProvider.TryGetFromGameIcon(new GameIconLookup(AetheryteIconId), out var texture)
            && texture.TryGetWrap(out var icon, out _))
        {
            clicked = HuntUi.Button(id, string.Empty, quiet: true, size: dimensions);
            if (ImGui.IsItemVisible())
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var edge = Math.Max(1, Math.Min(max.X - min.X, max.Y - min.Y) - 2 * ImGuiHelpers.GlobalScale);
                var imageMin = (min + max - new Vector2(edge)) / 2;
                var draw = ImGui.GetWindowDrawList();
                draw.PushClipRect(min, max, true);
                draw.AddImage(icon.Handle, imageMin, imageMin + new Vector2(edge), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(Vector4.One));
                draw.PopClipRect();
            }
        }
        else clicked = HuntUi.Button(id, "AE", quiet: true, size: dimensions);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private void DrawTrainSecondaryControls(bool compact)
    {
        if (compact)
        {
            ImGui.BeginDisabled(TrainMutationBusy);
            if (HuntUi.Button("import", "Import", FontAwesomeIcon.Clipboard, quiet: true,
                tooltip: "Import train from clipboard")) ImportFromClipboard();
            ImGui.EndDisabled();
            TrainControlSameLine(HuntUi.ButtonWidth("Plan"));
        }
        DrawTrainPanelChoice("Plan", TrainWorkspacePage.Setup, compact);
        TrainControlSameLine(HuntUi.ButtonWidth("Reports", compact ? null : FontAwesomeIcon.PaperPlane));
        DrawTrainPanelChoice("Reports", TrainWorkspacePage.Reports, compact);
        if (!compact)
        {
            CompactTrainControlSameLine();
            ImGui.BeginDisabled(TrainMutationBusy);
            if (HuntUi.IconButton("import", FontAwesomeIcon.Clipboard, "Import train from clipboard")) ImportFromClipboard();
            ImGui.EndDisabled();
        }
        CompactTrainControlSameLine();
        if (compact) ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - ImGui.GetFrameHeight()));
        if (HuntUi.IconButton("train-view", FontAwesomeIcon.SlidersH, "Train view and export")) ImGui.OpenPopup("TrainViewOptions");
        DrawCompactTrainOptions(compact);
    }

    private void DrawTrainPanelChoice(string label, TrainWorkspacePage panel, bool compact)
    {
        var selected = (compact ? _compactTrainPanel : _trainWorkspacePage) == panel;
        if (HuntUi.Button("panel-" + panel, label, compact ? null
            : panel == TrainWorkspacePage.Setup ? FontAwesomeIcon.Route : FontAwesomeIcon.PaperPlane,
            selected: selected, quiet: true)) SelectTrainPanel(selected ? TrainWorkspacePage.Route : panel, compact);
    }

    private void DrawTrainWorkspaceBody(bool popout)
    {
        var available = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing;
        var hasPanel = _trainWorkspacePage != TrainWorkspacePage.Route;
        var besideRoute = hasPanel && available.X >= 700 * ImGuiHelpers.GlobalScale;
        if (besideRoute)
        {
            var panelWidth = Math.Min(available.X * .42f, 306 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginChild("Train route", new Vector2(available.X - panelWidth - spacing.X, 0), false))
                DrawTrainRouteRegion(popout);
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, HuntTheme.Panel);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0);
            if (ImGui.BeginChild("Train panel", new Vector2(0, 0), true)) DrawTrainContextPanel(compact: false);
            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
            return;
        }
        if (hasPanel) DrawInlineTrainPanel(compact: false);
        if (ImGui.BeginChild("Train route", Vector2.Zero, false)) DrawTrainRouteRegion(popout);
        ImGui.EndChild();
    }

    private void DrawInlineTrainPanel(bool compact)
    {
        var available = ImGui.GetContentRegionAvail().Y;
        var minimumRoute = TrainRouteFooterHeight(compact) + ImGui.GetFrameHeightWithSpacing() * 2;
        var panelHeight = Math.Min(ImGui.GetFontSize() * 21, Math.Max(ImGui.GetFrameHeight() * 3,
            Math.Min(available * .5f, available - minimumRoute)));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, HuntTheme.Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0);
        if (ImGui.BeginChild("Inline train panel", new Vector2(0, panelHeight), true)) DrawTrainContextPanel(compact);
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawTrainContextPanel(bool compact)
    {
        var panel = compact ? _compactTrainPanel : _trainWorkspacePage;
        var title = panel == TrainWorkspacePage.Setup ? "Train plan" : "Train reports";
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(title);
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetFrameHeight());
        if (HuntUi.IconButton("close-panel", FontAwesomeIcon.Times, "Close " + title.ToLowerInvariant()))
            SelectTrainPanel(TrainWorkspacePage.Route, compact);
        ImGui.Separator();
        if (panel == TrainWorkspacePage.Setup)
        {
            DrawTrainSetup(compact);
            return;
        }
        var available = ImGui.GetContentRegionAvail().Y;
        var footerHeight = TrainReportFooterHeight();
        var scrollFooter = available < footerHeight + ImGui.GetTextLineHeightWithSpacing() * 2;
        if (ImGui.BeginChild("Train report preview", new Vector2(0, Math.Max(1, scrollFooter ? available : available - footerHeight)), false))
        {
            DrawTrainReports(compact);
            if (scrollFooter) DrawTrainReportFooter();
        }
        ImGui.EndChild();
        if (!scrollFooter) DrawTrainReportFooter();
    }

    private void DrawTrainRouteRegion(bool popout)
    {
        var footerHeight = TrainRouteFooterHeight(compact: false);
        var available = ImGui.GetContentRegionAvail().Y;
        if (available < ImGui.GetTextLineHeightWithSpacing() * 2)
        {
            DrawTrainUndoNotice();
            DrawTrainList(showZones: !popout || !_config.HideZonesInPopout,
                swapMarkAndZone: popout && _config.SwapMarkAndZoneInPopout, compact: true);
            DrawTrainWorkspaceFooter();
            return;
        }
        var scrollFooter = available < footerHeight + ImGui.GetTextLineHeightWithSpacing() * 2;
        if (ImGui.BeginChild("Train route rows", new Vector2(0, Math.Max(1, scrollFooter ? available : available - footerHeight)), false))
        {
            DrawTrainUndoNotice();
            DrawTrainList(showZones: !popout || !_config.HideZonesInPopout,
                swapMarkAndZone: popout && _config.SwapMarkAndZoneInPopout, compact: true);
            if (scrollFooter) DrawTrainWorkspaceFooter();
        }
        ImGui.EndChild();
        if (!scrollFooter) DrawTrainWorkspaceFooter();
    }

    private void DrawTrainContext(bool compact = false)
    {
        var sharing = _config.SyncEnabled && _config.SyncShareTrain;
        var context = Sync.ConnectionPresentation.TrainScope(_config.SyncEnabled, _sync.IsConnected, _config.SyncShareTrain);
        var start = ImGui.GetCursorPos();
        var scale = ImGuiHelpers.GlobalScale;
        var padding = compact ? 3 * scale : 6 * scale;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - padding * 2);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var flagWidth = HuntUi.ButtonWidth("Add flag", FontAwesomeIcon.MapMarkerAlt);
        var sharingWidth = ImGui.CalcTextSize(context).X + ImGui.GetFrameHeight() + gap;
        var compactLayout = TrainContextLayout.Compact(width, scale, gap, flagWidth);
        var presetWidth = compact ? compactLayout.PresetWidth : 184 * scale;
        var nameWidth = compact ? compactLayout.NameWidth : 146 * scale;
        var orderWidth = compact ? 0 : ImGui.CalcTextSize("Order").X + gap;
        var inlineSharing = compact || width >= orderWidth + presetWidth + flagWidth + nameWidth + sharingWidth + gap * 4;
        var rows = compact && !_config.ScanningPaused ? compactLayout.Rows : inlineSharing ? 1 : 2;
        var height = ImGui.GetFrameHeight() * rows + padding * 2 + (rows - 1) * ImGui.GetStyle().ItemSpacing.Y;
        HuntUi.FillBand(height, HuntTheme.Surface);
        ImGui.SetCursorPos(start + new Vector2(padding));
        if (!_config.ScanningPaused)
        {
            if (!compact)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Order");
                ImGui.SameLine();
                presetWidth = Math.Min(presetWidth, Math.Max(80 * scale, width - orderWidth - flagWidth - 90 * scale - gap * 2));
                nameWidth = Math.Min(nameWidth, Math.Max(1, width - orderWidth - presetWidth - flagWidth - gap * 2));
            }
            DrawPresetSelector("##Route preset", presetWidth);
            if (!compact || !compactLayout.WrapFlagControls) ImGui.SameLine();
            else ImGui.SetCursorPosX(start.X + padding);
            ImGui.BeginDisabled(TrainMutationBusy);
            if (compact)
            {
                ImGui.SetNextItemWidth(nameWidth);
                ImGui.InputTextWithHint("##customFlagLabel", "Flag name", ref _customFlagLabel, 64);
                if (!compactLayout.StackFlagButton) ImGui.SameLine();
                else ImGui.SetCursorPosX(start.X + padding);
                DrawTrainContextFlagButton();
            }
            else
            {
                DrawTrainContextFlagButton();
                ImGui.SameLine();
                ImGui.SetNextItemWidth(nameWidth);
                ImGui.InputTextWithHint("##customFlagLabel", "Optional stop name", ref _customFlagLabel, 64);
            }
            ImGui.EndDisabled();
        }
        else
        {
            var presets = sharing ? _sync.TrainPresets.Presets : _config.TrainPresets;
            var active = presets.FirstOrDefault(p => p.Id == ActivePresetId);
            var order = active?.Name ?? "Manual order";
            if (active is not null && PresetOrderingPaused) order += " · ordering paused";
            var changeWidth = HuntUi.ButtonWidth("Change");
            var orderAvailable = Math.Max(1, width - changeWidth - gap - (!compact && inlineSharing ? sharingWidth + gap : 0));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(HuntTheme.Muted, TrainRowPresentation.FitText(order, orderAvailable, static text => ImGui.CalcTextSize(text).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(order);
            ImGui.SameLine();
            if (HuntUi.Button("change-order", "Change", quiet: true)) SelectTrainPanel(TrainWorkspacePage.Setup, compact);
        }
        if (!compact)
        {
            if (inlineSharing) ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(start.X + padding, start.X + padding + width - sharingWidth));
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(context);
            ImGui.SameLine();
            if (HuntUi.IconButton("train-popout", FontAwesomeIcon.ExternalLinkAlt, "Hunt Train popout /hht"))
                _trainPopoutVisible = true;
        }
        ImGui.SetCursorPos(new Vector2(start.X, Math.Max(start.Y + height, ImGui.GetCursorPosY())));
        if (sharing && !_sync.HasCurrentTrainSnapshot)
            ImGui.TextWrapped("Waiting for the server's train. Shared updates resume after reconnecting.");
        else if (sharing && !_sync.SupportsTrainPresets)
            ImGui.TextWrapped("Shared presets need server 0.3.26 or later; ordinary train sharing remains available.");
        if (!string.IsNullOrWhiteSpace(_sync.PresetStatus)) ImGui.TextWrapped(_sync.PresetStatus);
    }

    private void DrawTrainContextFlagButton()
    {
        if (!HuntUi.Button("add-flag", "Add flag", FontAwesomeIcon.MapMarkerAlt,
            tooltip: "Add your current map flag to the train as a custom stop") || TrainMutationBusy) return;
        if (_detector.AddCustomFlag(_customFlagLabel) is null)
            ReportProblem("No map flag set - place one with Ctrl+Right-Click first.");
        else _customFlagLabel = string.Empty;
    }

    private void DrawTrainSetup(bool compact)
    {
        ImGui.BeginDisabled(TrainMutationBusy);
        ImGui.TextUnformatted("Route preset");
        var presets = SharingPresetTrain ? _sync.TrainPresets.Presets : _config.TrainPresets;
        var active = presets.FirstOrDefault(preset => preset.Id == ActivePresetId);
        DrawPresetSelector("##Plan preset", Math.Max(1, ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X));
        ImGui.SameLine();
        ImGui.BeginDisabled(active is null);
        if (HuntUi.IconButton("duplicate-preset", FontAwesomeIcon.Copy, "Duplicate this preset" ) && active is not null)
        {
            var duplicate = active.Copy();
            duplicate.Id = Guid.NewGuid().ToString("N");
            duplicate.Name = duplicate.Name.Length <= 73 ? duplicate.Name + " (copy)" : duplicate.Name[..73] + " (copy)";
            EditPreset(duplicate, PresetDraftSource.New, unsaved: true);
        }
        ImGui.EndDisabled();
        if (!compact)
        {
            if (active is not null) DrawTrainPlanZones(active);
            else ImGui.TextDisabled("Manual route order");
        }
        if (HuntUi.Button("edit-preset", active is null ? "Preset library" : "Edit preset", FontAwesomeIcon.PencilAlt))
        {
            if (active is not null) EditPreset(active, SharingPresetTrain ? PresetDraftSource.Server : PresetDraftSource.Local);
            else OpenPresetEditor();
        }
        TrainControlSameLine(HuntUi.ButtonWidth("Calculate rallies", FontAwesomeIcon.MapMarkedAlt));
        var unavailable = RallyCalculationUnavailable;
        ImGui.BeginDisabled(unavailable is not null);
        if (HuntUi.Button("calculate-rallies", "Calculate rallies", FontAwesomeIcon.MapMarkedAlt,
            tooltip: unavailable ?? "Calculate pending rally flags for the current route order") && RallyCalculationUnavailable is null)
        {
            if (SharingPresetTrain) _sync.SendPreset("recalculate-rallies");
            else ApplyLocalTrainPreset(force: true, recalculateRallies: true);
        }
        ImGui.EndDisabled();
        if (ActivePresetId is not null && PresetOrderingPaused)
            ImGui.TextColored(HuntTheme.Warning, "Ordering paused");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Scout credits");
        DrawTrainPlanCredits();
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Train watches")) DrawSRankWatches();
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Route tools & recovery"))
        {
            ImGui.BeginDisabled(TrainMutationBusy);
            if (HuntUi.Button("plan-import", "Import", FontAwesomeIcon.Clipboard)) ImportFromClipboard();
            TrainControlSameLine(HuntUi.ButtonWidth("Export", FontAwesomeIcon.Copy));
            if (HuntUi.Button("plan-export", "Export", FontAwesomeIcon.Copy)) CopyTrainExport();
            ImGui.Spacing();
            DrawAddTrainFlagControls(labelWidth: Math.Min(180, ImGui.GetContentRegionAvail().X));
            ImGui.EndDisabled();
            DrawTrainRecoveryControls();
        }
        if (HuntUi.Button("train-preferences", "Train preferences", FontAwesomeIcon.Cog, quiet: true))
            OpenPreferences(SettingsPage.Train);
    }

    private void DrawTrainPlanZones(TrainPreset preset)
    {
        string? previousExpansion = null;
        foreach (var zone in preset.Zones)
        {
            if (!RouteCatalog.ByTerritory.TryGetValue(zone.TerritoryId, out var info)) continue;
            ImGui.PushID((int)zone.TerritoryId);
            if (previousExpansion != info.Expansion)
            {
                ImGui.Spacing();
                ImGui.TextDisabled(info.Expansion);
            }
            var canMove = previousExpansion == info.Expansion;
            previousExpansion = info.Expansion;
            var start = ImGui.GetCursorPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = ImGui.GetFrameHeight();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(TrainRowPresentation.FitText(info.Name,
                Math.Max(1, width - height - ImGui.GetStyle().ItemSpacing.X), static text => ImGui.CalcTextSize(text).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(info.Name);
            ImGui.SameLine();
            ImGui.SetCursorPosX(start.X + width - height);
            ImGui.BeginDisabled(!canMove || _presetDraftRequest is not null);
            if (HuntUi.IconButton("move-zone-up", FontAwesomeIcon.ArrowUp,
                _presetDraftRequest is not null ? "Wait for the server to confirm the preset change."
                : "Move this zone earlier in the editor draft. Save to apply the change."))
            {
                EditPreset(preset, SharingPresetTrain ? PresetDraftSource.Server : PresetDraftSource.Local,
                    earlierZone: zone.TerritoryId);
            }
            ImGui.EndDisabled();
            ImGui.SetCursorPos(new Vector2(start.X, start.Y + height + 1));
            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void DrawTrainPlanCredits()
    {
        var sharedRemoval = _sync.IsConnected && _config.SyncShareTrain && _sync.SupportsScoutRemoval;
        foreach (var name in CombinedTrainScouts())
        {
            ImGui.PushID(name);
            var start = ImGui.GetCursorPosX();
            var width = ImGui.GetContentRegionAvail().X;
            var local = _config.AdditionalScouts.Any(credit => credit.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(TrainRowPresentation.FitText(name, Math.Max(1, width - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X), static text => ImGui.CalcTextSize(text).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
            ImGui.SameLine();
            ImGui.SetCursorPosX(start + width - ImGui.GetFrameHeight());
            ImGui.BeginDisabled(!local && !sharedRemoval);
            if (HuntUi.IconButton("remove-credit", FontAwesomeIcon.Times,
                local || sharedRemoval ? "Remove scout credit" : "Automatic credit while scouting is active"))
            {
                _config.AdditionalScouts.RemoveAll(credit => credit.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
                _sync.ChangeScoutCredit(name, false);
                _config.Save();
            }
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        var names = _config.AdditionalScouts.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        ImGui.BeginDisabled(names.Count >= MaxAdditionalScouts);
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X));
        var submit = ImGui.InputTextWithHint("##Scout credit", "Scout name", ref _manualScoutDraft, 100, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var draft = _manualScoutDraft.Trim();
        var valid = draft.Length > 0 && !draft.Any(char.IsControl)
            && !names.Any(name => name.Trim().Equals(draft, StringComparison.OrdinalIgnoreCase));
        ImGui.BeginDisabled(!valid);
        var clicked = HuntUi.IconButton("add-credit", FontAwesomeIcon.Plus, "Add scout credit");
        ImGui.EndDisabled();
        if (names.Count < MaxAdditionalScouts && valid && (submit || clicked))
        {
            _config.AdditionalScouts.RemoveAll(string.IsNullOrWhiteSpace);
            _config.AdditionalScouts.Add(draft);
            _sync.ChangeScoutCredit(draft, true);
            _manualScoutDraft = string.Empty;
            _config.Save();
        }
        ImGui.EndDisabled();
        if (names.Count >= MaxAdditionalScouts) ImGui.TextDisabled($"Maximum {MaxAdditionalScouts} additional scouts");
        var removed = _sync.ScoutCredits.Where(credit => credit.Removed).ToList();
        if (removed.Count == 0 || !ImGui.CollapsingHeader($"Removed credits ({removed.Count})")) return;
        foreach (var credit in removed)
        {
            ImGui.PushID("removed-" + credit.Name);
            ImGui.BeginDisabled(!sharedRemoval);
            var width = ImGui.GetContentRegionAvail().X;
            var label = TrainRowPresentation.FitText(credit.Name, Math.Max(1, width - ImGui.GetFrameHeight() * 2), static text => ImGui.CalcTextSize(text).X);
            if (HuntUi.Button("restore-credit", label, FontAwesomeIcon.Undo, quiet: true,
                tooltip: "Restore " + credit.Name)) _sync.ChangeScoutCredit(credit.Name, true);
            ImGui.EndDisabled();
            ImGui.PopID();
        }
    }

    private void DrawTrainRecoveryControls()
    {
        ImGui.BeginDisabled(TrainMutationBusy);
        if (ImGui.Button("Remove Dead")) _detector.RemoveDead();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove dead route rows; their report history is retained.");
        TrainControlSameLine("Reset train");
        ImGui.BeginDisabled(!ImGui.GetIO().KeyShift);
        ImGui.PushStyleColor(ImGuiCol.Text, HuntTheme.Danger);
        if (ImGui.Button("Reset train")) ResetTrainWithUndo();
        ImGui.PopStyleColor();
        ImGui.EndDisabled();
        ImGui.TextDisabled("Hold Shift to reset. Nothing is posted.");
        DrawTrainUndo();
        ImGui.EndDisabled();
    }

    private void DrawTrainUndoNotice()
    {
        if (!_showTrainUndoNotice || _config.ResetUndoAt is null) return;
        ImGui.PushID("Recovery notice");
        ImGui.BeginDisabled(TrainMutationBusy);
        DrawTrainUndo();
        ImGui.EndDisabled();
        if (ImGui.SmallButton("Dismiss recovery notice")) _showTrainUndoNotice = false;
        ImGui.Separator();
        ImGui.PopID();
    }

    private void DrawTrainReports(bool compact)
    {
        _trainReportsDrawnAt = DateTime.UtcNow;
        var report = _trainCompletionReport ? 1 : 0;
        ImGui.BeginDisabled(TrainMutationBusy);
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
        if (ImGui.Combo("##Report type", ref report, new[] { "Scouting report", "Train completion" }, 2))
        {
            _trainCompletionReport = report == 1;
            _trainReportPreview = null;
            _trainPreviewFingerprint = null;
            _nextTrainPreviewUpdate = DateTime.MinValue;
        }
        ImGui.EndDisabled();

        if (!_trainCompletionReport)
        {
            _scoutNote.ObserveTrain(_detector.TrainGeneration);
            var note = _scoutNote.Text;
            ImGui.Spacing();
            ImGui.TextUnformatted("Scout note");
            var count = $"{ScoutingReport.NoteCharacterCount(note)} / {ScoutingReport.MaxNotesLength}";
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(count).X);
            ImGui.TextDisabled(count);
            if (ImGui.InputTextMultiline("##Scout notes", ref note, 4096,
                new Vector2(-1, ImGui.GetTextLineHeightWithSpacing() * 2.5f)))
            {
                _scoutNote.SetText(note, _detector.TrainGeneration);
                _trainPreviewFingerprint = null;
                _nextTrainPreviewUpdate = DateTime.MinValue;
            }
            if (!string.IsNullOrEmpty(_scoutNote.DetachedText))
            {
                ImGui.TextWrapped("A note from a previous train is saved locally; it is not included in this report.");
                if (ImGui.TreeNode("Previous note"))
                {
                    ImGui.TextWrapped(_scoutNote.DetachedText);
                    ImGui.BeginDisabled(!string.IsNullOrEmpty(_scoutNote.Text));
                    if (ImGui.Button("Use for this train")) _scoutNote.ReuseDetached(_detector.TrainGeneration);
                    ImGui.EndDisabled();
                    TrainControlSameLine("Discard previous note");
                    if (ImGui.Button("Discard previous note")) _scoutNote.ClearDetached();
                    ImGui.TreePop();
                }
            }
            if (HuntUi.Button("report-credits", "Scout credits", FontAwesomeIcon.Users, quiet: true))
                SelectTrainPanel(TrainWorkspacePage.Setup, compact);
        }
        else
        {
            if (_config.SyncEnabled && _config.SyncShareTrain && (!_sync.IsConnected || !_sync.SupportsPartialFinish))
                ImGui.TextWrapped("Connect to a server with persistent partial-report support before finishing this shared train.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(HuntTheme.Muted, TrainMutationBusy ? "Captured report" : "Live preview");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(TrainMutationBusy
            ? "Sending this captured report. Note edits are saved for the next report."
            : $"Sending refreshes the latest train state.\nUpdated {_trainPreviewAt.ToLocalTime():T}");
        if (!string.IsNullOrEmpty(_trainPreviewError)) ImGui.TextWrapped(_trainPreviewError);
        if (_trainReportPreview is { } preview)
        {
            var destinations = TrainMutationBusy ? _trainReportDestinationCount : ReportDestinationCount();
            ImGui.TextDisabled($"{destinations} destinations / {preview.MessageCount} messages each");
            if (!_trainCompletionReport && preview.MessageCount == 2)
                ImGui.TextWrapped("The import code will be sent first, followed by the report in a second message.");
            if (preview.MessageCount == 0) ImGui.TextWrapped(preview.EmptyMessage);
            DrawPreparedTrainReport(_trainReportDisplay);
        }
        else if (string.IsNullOrEmpty(_trainPreviewError)) ImGui.TextDisabled("Preparing preview…");

        if (_trainCompletionReport && ImGui.CollapsingHeader("Individual kill history")) DrawMarksSlainTab();
    }

    private static readonly Regex DiscordPreviewTimestamp = new(@"<t:(-?\d+):([tTfFRdD])>", RegexOptions.Compiled);
    private static string TrainPreviewText(string text)
    {
        var local = DiscordPreviewTimestamp.Replace(text, match =>
        {
            if (!long.TryParse(match.Groups[1].Value, out var seconds)) return match.Value;
            try
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                return date.ToString(match.Groups[2].Value is "t" or "T" ? "t" : "g");
            }
            catch (ArgumentOutOfRangeException) { return match.Value; }
        });
        // Display the same prepared text without Discord's formatting delimiters.
        return Regex.Replace(local.Replace("**", ""), @"\\([^\p{L}\p{N}\s])", "$1");
    }

    private void DrawPreparedTrainReport(IReadOnlyList<DiscordEmbedPreview> embeds)
    {
        var start = ImGui.GetCursorScreenPos();
        var inset = 10 * ImGuiHelpers.GlobalScale;
        ImGui.Indent(inset);
        ImGui.BeginGroup();
        for (var i = 0; i < embeds.Count; i++)
        {
            var embed = embeds[i];
            ImGui.PushID(i);
            ImGui.Spacing();
            if (!_trainCompletionReport && _trainReportPreview is { MessageCount: 2 } && i < 2)
                ImGui.TextDisabled(i == 0 ? "Message 1 — import code" : "Message 2 — scouting report");
            if (embed.Description.StartsWith("```", StringComparison.Ordinal))
            {
                if (embed.Title != "Import code") ImGui.TextUnformatted(embed.Title);
                if (ImGui.CollapsingHeader("Import code"))
                {
                    var code = embed.Description.Trim().Trim('`').Trim();
                    if (ImGui.Button("Copy import code"))
                    {
                        try { ImGui.SetClipboardText(code); _lastPostResult = "Import code copied to clipboard."; }
                        catch (Exception ex)
                        {
                            _log.Warning(ex, "Could not copy report import code.");
                            _lastPostResult = "Could not copy the import code to the clipboard.";
                        }
                    }
                    ImGui.TextWrapped(code);
                }
            }
            else
            {
                ImGui.TextWrapped(embed.Title);
                ImGui.TextWrapped(embed.Description);
            }
            foreach (var field in embed.Fields ?? Array.Empty<DiscordEmbedFieldPreview>())
            {
                if (!string.IsNullOrWhiteSpace(field.Name.Replace("\u200b", string.Empty)))
                    ImGui.TextColored(HuntTheme.Muted, field.Name);
                ImGui.TextWrapped(field.Value);
            }
            ImGui.PopID();
        }
        ImGui.EndGroup();
        var end = ImGui.GetItemRectMax();
        ImGui.Unindent(inset);
        if (end.Y > start.Y) ImGui.GetWindowDrawList().AddLine(start, new Vector2(start.X, end.Y),
            ImGui.GetColorU32(HuntTheme.Telemetry), 3 * ImGuiHelpers.GlobalScale);
    }

    private float TrainRouteFooterHeight(bool compact)
    {
        if (compact) return CompactTrainFooterHeight();
        var style = ImGui.GetStyle();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var summary = TrainRouteCountText();
        var textHeight = ImGui.CalcTextSize(summary, false, width).Y;
        var height = TrainFooterEndFits(summary, width) ? Math.Max(textHeight, ImGui.GetFrameHeight())
            : textHeight + style.ItemSpacing.Y + ImGui.GetFrameHeight();
        if (TrainMutationBusy) height += style.ItemSpacing.Y + ImGui.CalcTextSize(TrainReportBusyText, false, width).Y;
        if (!string.IsNullOrEmpty(TrainRouteResultText))
            height += style.ItemSpacing.Y + ImGui.CalcTextSize(TrainRouteResultText, false, width).Y;
        return height + style.ItemSpacing.Y * 2;
    }

    private float TrainReportFooterHeight()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = ImGui.GetFrameHeight() + spacing + ImGui.CalcTextSize(TrainReportSendHint, false, width).Y;
        if (TrainMutationBusy) height += spacing + ImGui.CalcTextSize(TrainReportBusyText, false, width).Y;
        if (!string.IsNullOrEmpty(_lastPostResult)) height += spacing + ImGui.CalcTextSize(_lastPostResult, false, width).Y;
        return height + spacing * 2;
    }

    private void OpenTrainCompletionPreview(bool compact)
    {
        if (!_trainCompletionReport)
        {
            _trainCompletionReport = true;
            _trainReportPreview = null;
            _trainPreviewFingerprint = null;
        }
        SelectTrainPanel(TrainWorkspacePage.Reports, compact);
    }

    private const string TrainEndLabel = "End train";
    private const string TrainReportSendHint = "Hold Shift and click to send.";
    private string TrainRouteResultText => !_trainResultReportsOnly || _trainWorkspacePage != TrainWorkspacePage.Reports
        ? _lastPostResult : string.Empty;
    private string TrainReportBusyText => _completion.IsBusy ? "Finishing report…" : "Sending report…";

    private string TrainRouteCountText()
    {
        var recorded = 0;
        var remaining = 0;
        foreach (var mark in _detector.Marks.Values)
        {
            if (mark.IsCustom) continue;
            recorded++;
            if (!mark.Dead) remaining++;
        }
        return $"{remaining} left / {recorded} recorded";
    }

    private static bool TrainFooterEndFits(string summary, float width) =>
        ImGui.CalcTextSize(summary).X + ImGui.GetStyle().ItemSpacing.X
        + ImGui.CalcTextSize(TrainEndLabel).X + ImGui.GetStyle().FramePadding.X * 2 <= width;

    private static void DrawTrainFooterMutedText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawTrainWorkspaceFooter()
    {
        ImGui.Separator();
        var summary = TrainRouteCountText();
        var endFits = TrainFooterEndFits(summary, ImGui.GetContentRegionAvail().X);
        DrawTrainFooterMutedText(summary);
        if (endFits)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - HuntUi.ButtonWidth(TrainEndLabel));
        }
        DrawTrainEndButton(compact: false);
        if (TrainMutationBusy) DrawTrainFooterMutedText(TrainReportBusyText);
        if (!string.IsNullOrEmpty(TrainRouteResultText)) ImGui.TextWrapped(TrainRouteResultText);
    }

    private void DrawTrainEndButton(bool compact)
    {
        ImGui.BeginDisabled(TrainMutationBusy);
        if (HuntUi.Button("end-train", TrainEndLabel))
        {
            if (ImGui.GetIO().KeyShift) _ = EndTrainNowAsync();
            else OpenTrainCompletionPreview(compact);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Preview the completion report. Shift-click to send immediately.\nReported dead marks clear after success; unfinished marks remain.");
    }

    private void DrawTrainReportFooter()
    {
        ImGui.Separator();
        var canFinish = !_trainCompletionReport || !_config.SyncEnabled || !_config.SyncShareTrain
            || _sync.IsConnected && _sync.SupportsPartialFinish;
        ImGui.BeginDisabled(TrainMutationBusy || !ImGui.GetIO().KeyShift || !canFinish || _trainReportPreview is not { MessageCount: > 0 });
        if (_trainCompletionReport)
        {
            if (HuntUi.Button("send-completion", "Send completion", FontAwesomeIcon.PaperPlane, primary: true)) _ = EndTrainNowAsync();
        }
        else if (HuntUi.Button("send-scouting", "Send scouting report", FontAwesomeIcon.PaperPlane, primary: true)) _ = SendScoutingReportAsync();
        ImGui.EndDisabled();
        DrawTrainFooterMutedText(TrainReportSendHint);
        if (TrainMutationBusy) DrawTrainFooterMutedText(TrainReportBusyText);
        if (!string.IsNullOrEmpty(_lastPostResult)) ImGui.TextWrapped(_lastPostResult);
    }

    private void SetTrainReportPreview(PreparedDiscordReport report)
    {
        _trainReportPreview = report;
        // Called synchronously during guarded preparation, before the first
        // network await, so this matches that send's webhook snapshot.
        _trainReportDestinationCount = ReportDestinationCount();
        _trainReportDisplay = report.Embeds.Select(embed => new DiscordEmbedPreview(embed.Title,
            embed.Description.StartsWith("```", StringComparison.Ordinal) ? embed.Description : TrainPreviewText(embed.Description),
            embed.Fields?.Select(field => new DiscordEmbedFieldPreview(TrainPreviewText(field.Name),
                TrainPreviewText(field.Value))).ToArray())).ToArray();
        _trainPreviewAt = DateTime.UtcNow;
        _trainPreviewError = string.Empty;
    }

    private int ReportDestinationCount() => _config.Webhooks
        .Where(w => w.Enabled && !string.IsNullOrWhiteSpace(w.Url))
        .Select(w => w.Url).Distinct().Count();

    private void UpdateTrainReportPreview()
    {
        _scoutNote.ObserveTrain(_detector.TrainGeneration);
        var now = DateTime.UtcNow;
        if (TrainMutationBusy || now - _trainReportsDrawnAt > TimeSpan.FromSeconds(1) || now < _nextTrainPreviewUpdate) return;
        _nextTrainPreviewUpdate = now.AddSeconds(1);
        try
        {
            var route = _detector.Ordered();
            var scouts = CombinedTrainScouts();
            var history = _trainCompletionReport ? BuildCurrentMarks() : new List<TrackedMark>();
            var fingerprint = TrainPreviewFingerprint(route, scouts, history);
            if (_trainPreviewFingerprint == fingerprint && _trainReportPreview is not null) return;
            _trainPreviewFingerprint = fingerprint;
            PreparedDiscordReport report;
            var unix = new DateTimeOffset(now).ToUnixTimeSeconds();
            if (_trainCompletionReport)
            {
                var marks = CanReportPartially ? TrainReport.ForReport(history) : history;
                var watches = CanReportPartially
                    ? TrainReport.SplitWatches(_config.Flags, TrainReport.ReportedLegs(history), history.Select(m => m.WorldId)).Reported
                    : CloneWatches(_config.Flags);
                report = DiscordRelay.PrepareTrainReport(marks, _objectTable.LocalPlayer?.Name?.TextValue, watches, unix);
            }
            else report = DiscordRelay.PrepareScoutingReport(ScoutRecords(route), scouts,
                TrainExchange.Export(route), unix, _scoutNote.CaptureForReport(_detector.TrainGeneration).Text, ScoutZoneInstanceCounts);
            SetTrainReportPreview(report);
        }
        catch (Exception ex)
        {
            _trainReportPreview = null;
            _trainPreviewError = "Could not prepare the report preview. See the plugin log.";
            _log.Error(ex, "Could not prepare train report preview.");
            _nextTrainPreviewUpdate = now.AddSeconds(10);
        }
    }

    private int TrainPreviewFingerprint(List<DetectedMark> route, List<string> scouts, List<TrackedMark> history)
    {
        // Cheap field hashing only; no gzip/JSON work for unchanged previews or
        // outside the Reports page. Sending always prepares a fresh snapshot.
        var hash = new HashCode();
        hash.Add(_trainCompletionReport); hash.Add(CanReportPartially); hash.Add(_detector.TrainGeneration);
        hash.Add(_scoutNote.Revision); hash.Add(_objectTable.LocalPlayer?.Name?.TextValue);
        if (!_trainCompletionReport)
        {
            var instanceCounts = ScoutZoneInstanceCounts;
            hash.Add(instanceCounts is not null);
            if (instanceCounts is not null)
                foreach (var entry in instanceCounts.OrderBy(p => p.Key))
                {
                    hash.Add(entry.Key);
                    hash.Add(entry.Value);
                }
        }
        foreach (var mark in route)
        {
            hash.Add(mark.Name); hash.Add(mark.NameId); hash.Add(mark.WorldId); hash.Add(mark.WorldName);
            hash.Add(mark.Instance); hash.Add(mark.TerritoryId); hash.Add(mark.MapId); hash.Add(mark.MapPosition);
            hash.Add(mark.Dead); hash.Add(mark.LastSeenUtc); hash.Add(mark.DeathObservedAtUtc); hash.Add(mark.SnipedAtUtc);
            hash.Add(mark.IsCustom); hash.Add(mark.Spiced); hash.Add(mark.ZoneName);
        }
        foreach (var name in scouts) hash.Add(name);
        foreach (var mark in history)
        {
            hash.Add(mark.Key); hash.Add(mark.Name); hash.Add(mark.WorldName); hash.Add(mark.TerritoryId);
            hash.Add(mark.Dead); hash.Add(mark.LastSeenUtc); hash.Add(mark.DeathObservedAtUtc); hash.Add(mark.SnipedAtUtc);
        }
        foreach (var flag in _config.Flags)
        {
            hash.Add(flag.Label); hash.Add(flag.WorldId); hash.Add(flag.Instance); hash.Add(flag.TerritoryId);
            hash.Add(flag.SpawnStatus); hash.Add(flag.HasLocation); hash.Add(flag.X); hash.Add(flag.Y);
        }
        return hash.ToHashCode();
    }
}
