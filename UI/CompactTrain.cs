using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using HuntHelperEvolved.Sync;
using System;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private void DrawCompactTrainWindowContents()
    {
        using var compact = HuntTheme.PushCompact();
        ImGui.PushID("compactTrain");
        var toolbarStart = ImGui.GetCursorPos();
        var padding = 4 * ImGuiHelpers.GlobalScale;
        var toolbarWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - padding * 2);
        var navigationRows = TrainNavigationControlsWidth() <= toolbarWidth ? 1 : 2;
        var modeRows = TrainOperatingControlsWidth(compact: true) <= toolbarWidth ? 0 : 1;
        var toolbarRows = navigationRows + modeRows + 1;
        var toolbarHeight = ImGui.GetFrameHeight() * toolbarRows
            + ImGui.GetStyle().ItemSpacing.Y * (toolbarRows - 1) + padding * 2;
        HuntUi.FillBand(toolbarHeight, HuntTheme.Panel);
        ImGui.SetCursorPos(toolbarStart + new Vector2(padding));
        DrawTrainOperatingControls(compact: true, toolbarWidth);
        ImGui.SetCursorPosX(toolbarStart.X + padding);
        DrawTrainSecondaryControls(compact: true);
        ImGui.SetCursorPos(new Vector2(toolbarStart.X, Math.Max(toolbarStart.Y + toolbarHeight, ImGui.GetCursorPosY())));
        if (_compactTrainPanel != TrainWorkspacePage.Route) DrawInlineTrainPanel(compact: true);
        DrawTrainContext(compact: true);

        var footerHeight = CompactTrainFooterHeight();
        var available = ImGui.GetContentRegionAvail().Y;
        if (available < ImGui.GetTextLineHeightWithSpacing()*2)
        {
            DrawCompactTrainBody(includeFooter:true);
            ImGui.PopID();
            return;
        }
        var scrollFooter = available < footerHeight+ImGui.GetTextLineHeightWithSpacing()*2;
        if (ImGui.BeginChild("compactRoute",new Vector2(0,Math.Max(1,scrollFooter ? available : available-footerHeight))))
            DrawCompactTrainBody(scrollFooter);
        ImGui.EndChild();
        if (!scrollFooter) DrawCompactTrainFooter();
        ImGui.PopID();
    }

    private void DrawCompactTrainBody(bool includeFooter)
    {
        DrawTrainUndoNotice();
        DrawTrainList(showZones:!_config.HideZonesInPopout,swapMarkAndZone:_config.SwapMarkAndZoneInPopout,compact:true,satellite:true);
        if (includeFooter) DrawCompactTrainFooter();
    }

    private static void CompactTrainControlSameLine()
    {
        var right = ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X-ImGui.GetWindowPos().X+ImGui.GetStyle().ItemSpacing.X+ImGui.GetFrameHeight() <= right)
            ImGui.SameLine();
    }

    private void DrawCompactTrainOptions(bool compact)
    {
        if (!ImGui.BeginPopup("TrainViewOptions")) return;
        ImGui.TextUnformatted("Train view");
        ImGui.Separator();
        CompactTrainOption("Hide dead",_config.HideDeadMarks,value=>_config.HideDeadMarks=value);
        CompactTrainOption("Show zones",!_config.HideZonesInPopout,value=>_config.HideZonesInPopout=!value);
        ImGui.BeginDisabled(_config.HideZonesInPopout);
        CompactTrainOption("Show zone first",_config.SwapMarkAndZoneInPopout,value=>_config.SwapMarkAndZoneInPopout=value);
        ImGui.EndDisabled();
        CompactTrainOption("Show time since seen",_config.ShowMarkAge,value=>_config.ShowMarkAge=value);
        CompactTrainOption("Show spicing markers",_config.ShowSpicing,value=>_config.ShowSpicing=value);
        ImGui.BeginDisabled(TrainMutationBusy);
        var grouped = _config.GroupTrainByExpansion;
        if (ImGui.Checkbox("Group expansions within worlds", ref grouped))
        {
            _config.GroupTrainByExpansion = grouped;
            if (grouped) _detector.ApplyOrder(GroupByExpansion(_detector.Ordered()));
            _config.Save();
        }
        ImGui.EndDisabled();
        if (grouped)
            CompactTrainOption("Open next expansion automatically",_config.AutoExpandNextExpansion,value=>_config.AutoExpandNextExpansion=value);
        ImGui.Separator();
        CompactTrainOption("Advance after current mark dies",_config.AutoAdvance,value=>_config.AutoAdvance=value);
        CompactTrainOption("Announce on advance",_config.EchoOnAdvance,value=>_config.EchoOnAdvance=value);
        CompactTrainOption("Announce clicked marks",_config.EchoOnMarkClick,value=>_config.EchoOnMarkClick=value);
        ImGui.Separator();
        ImGui.BeginDisabled(TrainMutationBusy);
        if (ImGui.MenuItem("Copy export code")) CopyTrainExport();
        ImGui.EndDisabled();
        if (compact && ImGui.MenuItem("Open train workspace")) OpenTrainWorkspace(_compactTrainPanel);
        ImGui.EndPopup();
    }

    private void CopyTrainExport()
    {
        if (TrainMutationBusy) return;
        if (_detector.Marks.Count == 0) _lastPostResult = "Nothing to export - no marks recorded.";
        else
        {
            try
            {
                ImGui.SetClipboardText(TrainExchange.Export(_detector.Ordered()));
                _lastPostResult = $"Exported {_detector.Marks.Count} marks to clipboard.";
            }
            catch (Exception ex)
            {
                _log.Error(ex,"Could not copy train export.");
                _lastPostResult = "Could not copy the train export. See the plugin log.";
            }
        }
    }

    private void CompactTrainOption(string label,bool value,Action<bool> update)
    {
        if (!ImGui.Checkbox(label,ref value)) return;
        update(value);
        _config.DeferWindowStateSave();
    }

    private void DrawCompactTrainFooter()
    {
        ImGui.Separator();
        var summary = CompactTrainSummary();
        var width = ImGui.GetContentRegionAvail().X;
        var connectionWidth = CompactTrainConnectionWidth(summary,width);
        var inline = TrainFooterEndFits(summary,width-connectionWidth-ImGui.GetStyle().ItemSpacing.X);
        ConnectionUi.Draw("train-connection", _config, _sync, () => OpenPreferences(SettingsPage.Sharing), connectionWidth);
        ImGui.SameLine();
        DrawTrainFooterMutedText(summary);
        if (inline)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - HuntUi.ButtonWidth(TrainEndLabel));
        }
        DrawTrainEndButton(compact: true);
        var result = TrainMutationBusy ? TrainReportBusyText : _lastPostResult;
        if (!string.IsNullOrEmpty(result))
        {
            ImGui.TextColored(TrainMutationBusy ? HuntTheme.Telemetry : HuntTheme.Muted,
                TrainRowPresentation.FitText(result,width,static text=>ImGui.CalcTextSize(text).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(result);
        }
    }

    private static float CompactTrainConnectionWidth(string summary, float width)
    {
        var labelWidth = ConnectionUi.Width();
        return TrainFooterEndFits(summary, width - labelWidth - ImGui.GetStyle().ItemSpacing.X)
            ? labelWidth : ConnectionUi.Width(compact: true);
    }

    private string CompactTrainSummary()
    {
        var counts = TrainRowPresentation.Count(_detector.Marks.Values);
        return $"{counts.Remaining} left / {counts.Recorded} recorded";
    }

    private float CompactTrainFooterHeight()
    {
        var width = Math.Max(1,ImGui.GetContentRegionAvail().X);
        var summary = CompactTrainSummary();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var summaryWidth = Math.Max(1,width-CompactTrainConnectionWidth(summary,width)-ImGui.GetStyle().ItemSpacing.X);
        var textHeight = ImGui.CalcTextSize(summary,false,summaryWidth).Y;
        var height = Math.Max(textHeight,ImGui.GetFrameHeight());
        if (!TrainFooterEndFits(summary,summaryWidth)) height += spacing+ImGui.GetFrameHeight();
        if (TrainMutationBusy || !string.IsNullOrEmpty(_lastPostResult))
            height += spacing+ImGui.GetTextLineHeight();
        return height+spacing*2;
    }
}
