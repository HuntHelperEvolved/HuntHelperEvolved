using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private enum TrainWorkspacePage { Route, Reports, Setup }
    private TrainWorkspacePage _trainWorkspacePage;
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
    private DateTime _trainWorkspaceDrawnAt;
    private DateTime _trainPreviewAt;
    private int _trainReportDestinationCount;
    private bool TrainMutationBusy => _reportPostBusy || _completion.IsBusy;

    private void SelectTrainPage(TrainWorkspacePage page)
    {
        _trainWorkspacePage = page;
        _nextTrainPreviewUpdate = DateTime.MinValue;
    }

    private void DrawTrainWorkspace(bool popout)
    {
        _trainWorkspaceDrawnAt = DateTime.UtcNow;
        var operatingStart = ImGui.GetCursorPos();
        var operatingWidth = ImGui.GetContentRegionAvail().X;
        var scanButtonSize = ImGui.GetFrameHeight();
        var firstTabWidth = ImGui.CalcTextSize(nameof(TrainWorkspacePage.Route)).X + ImGui.GetStyle().FramePadding.X * 2;
        var reservedRight = scanButtonSize + ImGui.GetStyle().ItemSpacing.X;
        if (firstTabWidth + reservedRight > operatingWidth)
            ImGui.SetCursorPosY(operatingStart.Y + scanButtonSize + ImGui.GetStyle().ItemSpacing.Y);
        foreach (var page in Enum.GetValues<TrainWorkspacePage>())
        {
            if (page != TrainWorkspacePage.Route) TrainControlSameLine(page.ToString(), reservedRight);
            var selected = page == _trainWorkspacePage;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            if (ImGui.Button(page.ToString())) SelectTrainPage(page);
            if (selected) ImGui.PopStyleColor();
        }
        var tabsEnd = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(operatingStart.X + Math.Max(0, operatingWidth - scanButtonSize), operatingStart.Y));
        ImGui.BeginDisabled(TrainMutationBusy);
        if (TrainIconButton(_config.ScanningPaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause, new Vector2(scanButtonSize)))
        {
            _config.ScanningPaused = !_config.ScanningPaused;
            _config.Save();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip((_config.ScanningPaused ? "Scanning paused — resume" : "Scanning — pause")
            + "\nControls new scouting records and automatic scout credit. Existing deaths still update while paused.");
        ImGui.SetCursorPos(new Vector2(operatingStart.X, Math.Max(tabsEnd.Y, ImGui.GetCursorPosY())));
        ImGui.Separator();
        if (_trainWorkspacePage == TrainWorkspacePage.Route)
        {
            DrawTrainNavigation();
            DrawTrainContext();
        }

        var footerHeight = TrainWorkspaceFooterHeight();
        // All setup/report details scroll. Only operating controls and the relevant
        // action/result stay fixed, including when UI scaling makes the window small.
        var available = ImGui.GetContentRegionAvail().Y;
        var scrollFooter = available < footerHeight + ImGui.GetTextLineHeightWithSpacing() * 2;
        var bodyHeight = Math.Max(1, scrollFooter ? available : available - footerHeight);
        if (ImGui.BeginChild("Train workspace content", new Vector2(0, bodyHeight), false))
        {
            if (_showTrainUndoNotice && _config.ResetUndoAt is not null)
            {
                ImGui.PushID("Recovery notice");
                ImGui.BeginDisabled(TrainMutationBusy);
                DrawTrainUndo();
                ImGui.EndDisabled();
                if (ImGui.SmallButton("Dismiss recovery notice")) _showTrainUndoNotice = false;
                ImGui.Separator();
                ImGui.PopID();
            }
            switch (_trainWorkspacePage)
            {
                case TrainWorkspacePage.Route:
                    DrawTrainList(showZones: !popout || !_config.HideZonesInPopout);
                    break;
                case TrainWorkspacePage.Reports:
                    DrawTrainReports();
                    break;
                case TrainWorkspacePage.Setup:
                    DrawTrainSetup();
                    break;
            }
            // At short heights or large font scales, keep the action reachable
            // in the same vertical scroll region instead of drawing it below the window.
            if (scrollFooter) DrawTrainWorkspaceFooter();
        }
        ImGui.EndChild();
        if (!scrollFooter) DrawTrainWorkspaceFooter();
    }

    private void DrawTrainContext()
    {
        var sharing = _config.SyncEnabled && _config.SyncShareTrain;
        var context = sharing ? (_sync.IsConnected ? "Shared train" : "Shared train · disconnected") : "Local train";
        var presets = sharing ? _sync.TrainPresets.Presets : _config.TrainPresets;
        var active = presets.FirstOrDefault(p => p.Id == ActivePresetId);
        var order = active?.Name ?? "Manual order";
        if (active is not null && PresetOrderingPaused) order += " · ordering paused";
        ImGui.TextWrapped(context + " · " + order);
        TrainControlSameLine("Change");
        if (ImGui.SmallButton("Change")) SelectTrainPage(TrainWorkspacePage.Setup);
        if (sharing && !_sync.HasCurrentTrainSnapshot)
            ImGui.TextWrapped("Waiting for the server's train. Shared updates resume after reconnecting.");
        else if (sharing && !_sync.SupportsTrainPresets)
            ImGui.TextWrapped("Shared presets need server 0.3.26 or later; ordinary train sharing remains available.");
        if (!string.IsNullOrWhiteSpace(_sync.PresetStatus)) ImGui.TextWrapped(_sync.PresetStatus);
    }

    private void DrawTrainSetup()
    {
        ImGui.BeginDisabled(TrainMutationBusy);
        DrawSettingsHeading("Route preset");
        DrawPresetControls();
        ImGui.Spacing();
        DrawSettingsHeading("Route tools and view");
        DrawTrainControls();
        var spicing = _config.ShowSpicing;
        if (ImGui.Checkbox("Show spicing markers", ref spicing))
        {
            _config.ShowSpicing = spicing;
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show spicing markers and enable right-click > Being spiced on mark rows.");
        ImGui.Spacing();
        DrawSettingsHeading("Scout credits");
        DrawTrainScouts();
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("S-rank watch setup")) DrawSRankWatches();
        ImGui.Spacing();
        DrawSettingsHeading("Recovery");
        if (ImGui.Button("Remove Dead")) _detector.RemoveDead();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove dead route rows; their report history is retained.");
        TrainControlSameLine("Reset train");
        ImGui.BeginDisabled(!ImGui.GetIO().KeyShift);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
        if (ImGui.Button("Reset train")) ResetTrainWithUndo();
        ImGui.PopStyleColor();
        ImGui.EndDisabled();
        ImGui.TextDisabled("Hold Shift to reset. Nothing is posted.");
        DrawTrainUndo();
        ImGui.EndDisabled();
    }

    private void DrawTrainReports()
    {
        var report = _trainCompletionReport ? 1 : 0;
        ImGui.BeginDisabled(TrainMutationBusy);
        ImGui.SetNextItemWidth(Math.Min(240, ImGui.GetContentRegionAvail().X));
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
            ImGui.TextUnformatted($"Scout notes (optional) — {ScoutingReport.NoteCharacterCount(note)} / {ScoutingReport.MaxNotesLength}");
            if (ImGui.InputTextMultiline("##Scout notes", ref note, 4096,
                new Vector2(-1, ImGui.GetTextLineHeightWithSpacing() * 3)))
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
            ImGui.Spacing();
            var scouts = CombinedTrainScouts();
            var credits = scouts.Count == 0 ? "No scouts credited" : "Scouts: " + string.Join(", ", scouts.Take(3));
            if (scouts.Count > 3) credits += $" (+{scouts.Count - 3})";
            ImGui.TextWrapped(credits);
            if (ImGui.SmallButton("Edit credits")) SelectTrainPage(TrainWorkspacePage.Setup);
        }
        else
        {
            ImGui.TextWrapped("Reports expansions with observed kills. Reported dead marks are cleared after success; unfinished marks remain.");
            if (_config.SyncEnabled && _config.SyncShareTrain && (!_sync.IsConnected || !_sync.SupportsPartialFinish))
                ImGui.TextWrapped("Connect to a server with persistent partial-report support before finishing this shared train.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (TrainMutationBusy)
            ImGui.TextWrapped("Sending the captured report. Note edits are saved for your next report.");
        else
            ImGui.TextWrapped("Live preview — sending refreshes the latest train state.");
        if (!string.IsNullOrEmpty(_trainPreviewError)) ImGui.TextWrapped(_trainPreviewError);
        if (_trainReportPreview is { } preview)
        {
            var destinations = TrainMutationBusy ? _trainReportDestinationCount : ReportDestinationCount();
            ImGui.TextWrapped($"{preview.MessageCount} Discord message(s) per destination · {destinations} enabled destination(s)");
            if (!_trainCompletionReport && preview.MessageCount == 2)
                ImGui.TextWrapped("The import code will be sent first, followed by the report in a second message.");
            if (preview.MessageCount == 0) ImGui.TextWrapped(preview.EmptyMessage);
            ImGui.TextDisabled($"Preview updated {_trainPreviewAt.ToLocalTime():T}");
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
        for (var i = 0; i < embeds.Count; i++)
        {
            var embed = embeds[i];
            ImGui.PushID(i);
            ImGui.Spacing();
            if (!_trainCompletionReport && _trainReportPreview is { MessageCount: 2 } && i < 2)
                ImGui.TextDisabled(i == 0 ? "Message 1 — import code" : "Message 2 — scouting report");
            if (embed.Description.StartsWith("```", StringComparison.Ordinal))
            {
                if (embed.Title != "Import code") DrawSettingsHeading(embed.Title);
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
                DrawSettingsHeading(embed.Title);
                ImGui.TextWrapped(embed.Description);
            }
            foreach (var field in embed.Fields ?? Array.Empty<DiscordEmbedFieldPreview>())
            {
                if (!string.IsNullOrWhiteSpace(field.Name.Replace("\u200b", string.Empty)))
                    DrawSettingsHeading(field.Name);
                ImGui.TextWrapped(field.Value);
            }
            ImGui.PopID();
        }
    }

    private float TrainWorkspaceFooterHeight()
    {
        if (!HasTrainWorkspaceFooter) return 0;
        var style = ImGui.GetStyle();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = 0f;
        void AddRow(float rowHeight)
        {
            if (height > 0) height += style.ItemSpacing.Y;
            height += rowHeight;
        }
        float TextHeight(string text) => ImGui.CalcTextSize(text, false, width).Y;

        if (_trainWorkspacePage == TrainWorkspacePage.Route)
        {
            var summary = TrainRouteCountText();
            if (TrainFooterEndFits(summary, width))
                AddRow(Math.Max(TextHeight(summary), ImGui.GetFrameHeight()));
            else
            {
                AddRow(TextHeight(summary));
                AddRow(ImGui.GetFrameHeight());
            }
        }
        else if (_trainWorkspacePage == TrainWorkspacePage.Reports)
        {
            AddRow(ImGui.GetFrameHeight());
            AddRow(TextHeight(TrainReportSendHint));
        }
        if (ShowTrainReportProgress) AddRow(TextHeight(TrainReportBusyText));
        if (!string.IsNullOrEmpty(TrainWorkspaceResultText)) AddRow(TextHeight(TrainWorkspaceResultText));
        // EndChild and the separator each advance by ItemSpacing.Y. The last
        // footer row needs its visible height, without another trailing gap.
        return height + style.ItemSpacing.Y * 2;
    }

    private const string TrainEndLabel = "End train";
    private const string TrainReportSendHint = "Hold Shift and click to send.";
    private bool HasTrainWorkspaceFooter => _trainWorkspacePage != TrainWorkspacePage.Setup
        || !string.IsNullOrEmpty(TrainWorkspaceResultText);
    private bool ShowTrainReportProgress => _trainWorkspacePage == TrainWorkspacePage.Reports && TrainMutationBusy;
    private string TrainWorkspaceResultText => !_trainResultReportsOnly || _trainWorkspacePage == TrainWorkspacePage.Reports
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
        return $"{recorded} records · {remaining} recorded up";
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
        if (!HasTrainWorkspaceFooter) return;
        ImGui.Separator();
        if (_trainWorkspacePage == TrainWorkspacePage.Route)
        {
            var summary = TrainRouteCountText();
            var endFits = TrainFooterEndFits(summary, ImGui.GetContentRegionAvail().X);
            DrawTrainFooterMutedText(summary);
            if (endFits) ImGui.SameLine();
            var armed = ImGui.GetIO().KeyShift;
            ImGui.BeginDisabled(TrainMutationBusy || !armed);
            if (ImGui.Button(TrainEndLabel) && armed) _ = EndTrainNowAsync();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold Shift and click to send the completion report.\nReported dead marks are cleared after success; unfinished marks remain.\nPreview available in Reports > Train completion.");
        }
        else if (_trainWorkspacePage == TrainWorkspacePage.Reports)
        {
            var canFinish = !_trainCompletionReport || !_config.SyncEnabled || !_config.SyncShareTrain
                || _sync.IsConnected && _sync.SupportsPartialFinish;
            ImGui.BeginDisabled(TrainMutationBusy || !ImGui.GetIO().KeyShift || !canFinish || _trainReportPreview is not { MessageCount: > 0 });
            if (_trainCompletionReport)
            {
                if (ImGui.Button("Send & finish completed legs")) _ = EndTrainNowAsync();
            }
            else if (ImGui.Button("Send scouting report")) _ = SendScoutingReportAsync();
            ImGui.EndDisabled();
            DrawTrainFooterMutedText(TrainReportSendHint);
        }
        if (ShowTrainReportProgress) DrawTrainFooterMutedText(TrainReportBusyText);
        if (!string.IsNullOrEmpty(TrainWorkspaceResultText)) ImGui.TextWrapped(TrainWorkspaceResultText);
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
        if (TrainMutationBusy || _trainWorkspacePage != TrainWorkspacePage.Reports
            || now - _trainWorkspaceDrawnAt > TimeSpan.FromSeconds(1) || now < _nextTrainPreviewUpdate) return;
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
                TrainExchange.Export(route), unix, _scoutNote.CaptureForReport(_detector.TrainGeneration).Text);
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
