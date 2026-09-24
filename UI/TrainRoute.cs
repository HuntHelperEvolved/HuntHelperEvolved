using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    /// <summary>
    /// The train list, with drag-to-reorder.
    ///
    /// The important property here: NOTHING is reordered while the drag is in
    /// progress. Hovering a row only records where the drop would land, and the
    /// list is mutated exactly once, after the loop, when the mouse is
    /// released. Earlier versions swapped rows on every frame the cursor was
    /// off the source row, which made the dragged row race down the list and
    /// snap back — worse the slower you moved.
    ///
    /// The source index is tracked in a field rather than through an ImGui drag
    /// payload; behaviour is the same, and it keeps to API already proven to
    /// compile in this project.
    /// </summary>
    private void DrawTrainList(bool showZones = true)
    {
        var allMarks = _detector.Ordered();
        if (TrainMutationBusy) ClearTrainDrag();

        if (allMarks.Count == 0)
        {
            ClearTrainDrag();
            ResetTrainExpansionProgress();
            ImGui.TextWrapped(_config.ScanningPaused
                ? "The route is empty. Resume scanning to record marks nearby, or import a train."
                : "No marks recorded yet. Fly near a mark while scanning, or import a train.");
            ImGui.BeginDisabled(TrainMutationBusy);
            if (_config.ScanningPaused && ImGui.Button("Resume scanning"))
            {
                _config.ScanningPaused = false;
                _config.Save();
            }
            if (ImGui.Button("Import from Clipboard")) ImportFromClipboard();
            ImGui.EndDisabled();
            DrawSRankWatchRows();
            return;
        }

        // Grouping genuinely reorders the train rather than only redrawing it,
        // so it happens here, before anything is measured or drawn: everything
        // below — and Next Mark, the export code and the end-of-train report
        // with it — then sees one ordinary list in one order. A grouping the
        // reports did not follow would be a different train from the one on
        // screen.
        //
        // Never while a drag is in progress, or the re-sort would fight the
        // conductor for the row they are holding.
        var grouping = _config.GroupTrainByExpansion;
        // Shared grouping follows the route's existing block order, so every scout
        // reaches the same order without applying conflicting local preferences.
        if (!TrainMutationBusy && !PresetOrderLocked && _dragFromIndex == -1 && _dragExpansionFrom == -1
            && _trainGroupingState.Changed(allMarks, grouping, _config.SyncEnabled && _config.SyncShareTrain,
                _config.ExpansionOrder, _config.WorldExpansionOrder))
        {
            var grouped = GroupByExpansion(allMarks);
            if (!grouped.SequenceEqual(allMarks))
            {
                _detector.ApplyOrder(grouped);
                allMarks = grouped;
            }
        }

        // After the re-sort, because "the next block" is a question about the
        // order the blocks are in, and before the dead marks are filtered out,
        // because a leg ending is precisely a block whose marks are all dead.
        if (grouping && _config.AutoExpandNextExpansion)
            AutoExpandNextExpansion(allMarks);
        else
            ResetTrainExpansionProgress();

        // What's shown may be a subset, but ordering maths always works against
        // the full list so hidden dead marks keep their place in the train.
        var marks = allMarks;
        if (_config.HideDeadMarks)
        {
            _trainVisibleMarks.Clear();
            foreach (var mark in allMarks) if (!mark.Dead) _trainVisibleMarks.Add(mark);
            marks = _trainVisibleMarks;
        }

        if (marks.Count == 0)
        {
            ClearTrainDrag();
            ImGui.TextWrapped($"All {allMarks.Count} route entries are hidden by Hide dead.");
            if (ImGui.Button("Show dead"))
            {
                _config.HideDeadMarks = false;
                _config.DeferWindowStateSave();
            }
            DrawSRankWatchRows();
            return;
        }

        // Hidden rows retain their place in the route and the recorded total.
        // Only headings with visible rows are drawn, but their counts describe
        // the complete block, excluding rally stops.
        var expansionCounts = _trainExpansionCounts;
        expansionCounts.Clear();
        var expansionUpCounts = _trainExpansionUpCounts;
        expansionUpCounts.Clear();
        var presentExpansions = _trainPresentExpansions;
        presentExpansions.Clear();
        if (grouping)
        {
            foreach (var mark in marks)
            {
                var block = TrainBlock(mark);
                if (!presentExpansions.Contains(block)) presentExpansions.Add(block);
            }
            foreach (var block in allMarks.GroupBy(TrainBlock))
            {
                var count = TrainRowPresentation.Count(block);
                expansionCounts[block.Key] = count.Recorded;
                expansionUpCounts[block.Key] = count.Remaining;
            }
        }

        // Scouting can change positions between frames while a row is held.
        if (_dragMarkKey is { } dragKey && (_dragFromIndex = marks.FindIndex(m => m.Key == dragKey)) < 0) ClearTrainDrag();
        if (_dragTargetKey is { } targetKey) _dragToIndex = marks.FindIndex(m => m.Key == targetKey);
        if (_dragExpansionBlock is { } dragBlock && (_dragExpansionFrom = presentExpansions.IndexOf(dragBlock)) < 0) ClearTrainDrag();
        if (_dragExpansionTargetBlock is { } targetBlock) _dragExpansionTo = presentExpansions.IndexOf(targetBlock);
        (uint NameId, uint Instance, uint WorldId)? toRemove = null;

        var buttonHeight = ImGui.GetFrameHeight();
        var buttonGap = ImGui.GetStyle().ItemInnerSpacing.X;
        var rowGap = ImGui.GetStyle().ItemSpacing.Y;
        var dragging = _dragFromIndex != -1;
        var now = DateTime.UtcNow;
        string? lastExpansion = null;
        uint? lastWorld = null;
        var blockIsFolded = false;

        for (var i = 0; i < marks.Count; i++)
        {
            var mark = marks[i];
            if (lastWorld != mark.WorldId)
            {
                lastWorld = mark.WorldId;
                lastExpansion = null;
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextWrapped(TrainWorldName(mark.WorldId, marks));
            }
            if (grouping)
            {
                var expansion = ExpansionData.ExpansionOf(mark.NameId, mark.ZoneName);
                if (expansion != lastExpansion)
                {
                    lastExpansion = expansion;
                    blockIsFolded = DrawExpansionHeader(TrainBlock(mark),
                        presentExpansions.IndexOf(TrainBlock(mark)),
                        expansionCounts.GetValueOrDefault(TrainBlock(mark)),
                        expansionUpCounts.GetValueOrDefault(TrainBlock(mark)),
                        buttonHeight, presentExpansions);
                }
                if (blockIsFolded) continue;
            }

            ImGui.PushID($"{mark.NameId}_{mark.Instance}_{mark.WorldId}");
            var rowStart = ImGui.GetCursorPos();
            var rowMin = ImGui.GetCursorScreenPos();
            var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var layout = TrainRowLayout.Create(width, buttonHeight, buttonGap);
            var buttonSize = new Vector2(layout.ButtonWidth, buttonHeight);
            var isCurrent = _currentMark == mark.Key;
            var name = $"{(isCurrent ? "> " : "")}{mark.Name}";
            var instance = ExpansionData.InstanceGlyph(mark.Instance);
            var zone = ExpansionData.Lookup(mark.NameId)?.Location ?? (mark.IsCustom ? mark.ZoneName : "?");
            var detail = TrainRowPresentation.Describe(mark, now, showZones ? zone : null,
                _config.ShowMarkAge, _config.ShowSpicing);
            var displayedName = TrainRowPresentation.FitText(name, layout.TextWidth,
                static text => ImGui.CalcTextSize(text).X, instance);
            var displayedNameWidth = ImGui.CalcTextSize(displayedName).X;
            var inlineDetail = string.IsNullOrEmpty(detail) ? string.Empty : " · " + detail;
            var displayedDetail = TrainRowPresentation.FitText(inlineDetail,
                layout.TextWidth - displayedNameWidth, static text => ImGui.CalcTextSize(text).X);
            var height = Math.Max(buttonHeight, Math.Clamp(_config.TrainRowHeight, 14, 48));
            var textY = rowStart.Y + (height - ImGui.GetTextLineHeight()) / 2;
            var actionsY = rowStart.Y + (height - buttonHeight) / 2;
            var rowMax = rowMin + new Vector2(width, height);
            var rowColour = mark.Dead ? new Vector4(0.6f, 0.6f, 0.6f, 1f)
                : mark.Spiced && _config.ShowSpicing ? new Vector4(1f, 0.35f, 0.35f, 1f)
                : mark.IsCustom ? new Vector4(0.45f, 0.95f, 0.5f, 1f) : Vector4.One;

            ImGui.SetCursorPos(new Vector2(rowStart.X + layout.X(0), actionsY));
            var teleportPressed = false;
            if (_textureProvider.TryGetFromGameIcon(new GameIconLookup(AetheryteIconId), out var iconTex)
                && iconTex.TryGetWrap(out var iconWrap, out _))
            {
                teleportPressed = ImGui.Button("##teleport", buttonSize);
                var iconMin = ImGui.GetItemRectMin() + new Vector2(2);
                ImGui.GetWindowDrawList().AddImage(iconWrap.Handle, iconMin, iconMin + buttonSize - new Vector2(4));
            }
            else teleportPressed = ImGui.Button("TP", buttonSize);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(mark.IsCustom
                ? "Teleport to this rally stop, then complete it"
                : "Teleport to the nearest aetheryte");
            if (teleportPressed)
            {
                if (!_teleport.TeleportToNearest(mark.TerritoryId, mark.MapPosition)) ReportProblem(_teleport.LastError);
                else
                {
                    if (_config.TeleportAlsoFlags) MapFlagHelper.FlagMark(_gameGui, mark);
                    if (mark.IsCustom && !_pendingCustomRemovals.ContainsKey(mark.Key))
                        _pendingCustomRemovals[mark.Key] = DateTime.UtcNow.AddSeconds(CustomFlagRemovalDelaySeconds);
                }
            }

            // One text hit region excludes the action buttons. Neither long
            // names nor metadata can increase the row height or move controls.
            ImGui.SetCursorPos(new Vector2(rowStart.X + layout.TextLeft, rowStart.Y));
            ImGui.Selectable("##row", _dragFromIndex == i, ImGuiSelectableFlags.None,
                new Vector2(layout.TextWidth, height));
            var rowHovered = ImGui.IsItemHovered();
            var dropHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var rowActive = ImGui.IsItemActive();
            var rowFocused = ImGui.IsItemFocused();
            ImGui.SetItemAllowOverlap();
            if (_config.ShowSpicing && !mark.IsCustom && ImGui.BeginPopupContextItem("Mark spicing"))
            {
                ImGui.BeginDisabled(TrainMutationBusy);
                if (ImGui.MenuItem("Being spiced", "", mark.Spiced)) mark.Spiced = !mark.Spiced;
                ImGui.EndDisabled();
                ImGui.EndPopup();
            }
            ImGui.SetCursorPos(new Vector2(rowStart.X + layout.TextLeft, textY));
            ImGui.PushStyleColor(ImGuiCol.Text, isCurrent ? new Vector4(1f, 0.85f, 0.4f, 1f) : rowColour);
            ImGui.TextUnformatted(displayedName);
            ImGui.PopStyleColor();
            if (!string.IsNullOrEmpty(displayedDetail))
            {
                ImGui.SetCursorPos(new Vector2(rowStart.X + layout.TextLeft + displayedNameWidth, textY));
                ImGui.PushStyleColor(ImGuiCol.Text, rowColour);
                ImGui.TextUnformatted(displayedDetail);
                ImGui.PopStyleColor();
            }
            if (rowHovered && _dragFromIndex == -1 && _dragExpansionFrom == -1)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(name + instance);
                ImGui.TextUnformatted(TrainWorldName(mark.WorldId, allMarks));
                if (!string.IsNullOrEmpty(detail)) ImGui.TextWrapped(detail);
                if (_config.ShowSpicing && !mark.IsCustom) ImGui.TextUnformatted("Right-click to change Being spiced.");
                ImGui.EndTooltip();
            }

            ImGui.BeginDisabled(TrainMutationBusy);
            ImGui.SetCursorPos(new Vector2(rowStart.X + layout.X(1), actionsY));
            var witnessed = mark.Dead && mark.SnipedAtUtc is null && mark.DeathObservedAtUtc is not null;
            if (witnessed) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.42f, 0.3f, 1f));
            var killPressed = TrainIconButton(FontAwesomeIcon.Check, buttonSize);
            if (witnessed) ImGui.PopStyleColor();
            if (killPressed)
            {
                mark.Dead = !mark.Dead;
                mark.DeathObservedAtUtc = mark.Dead ? DateTime.UtcNow : null;
                if (!mark.Dead) mark.SnipedAtUtc = null;
                else _detector.RemoveSighting(mark.NameId, mark.Instance, mark.WorldId);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(TrainMutationBusy
                ? "Wait for the report operation to finish before editing the train"
                : mark.IsCustom ? mark.Dead ? "Restore this stop" : "Complete this stop"
                : mark.Dead ? "Restore this mark to not marked dead; clear death and sniped evidence"
                : "Killed now — record a witnessed death");

            ImGui.SetCursorPos(new Vector2(rowStart.X + layout.X(2), actionsY));
            ImGui.BeginDisabled(mark.IsCustom);
            var wasSniped = mark.SnipedAtUtc is not null;
            if (wasSniped) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.48f, 0.35f, 0.15f, 1f));
            var snipePressed = TrainIconButton(FontAwesomeIcon.Crosshairs, buttonSize);
            if (wasSniped) ImGui.PopStyleColor();
            if (snipePressed)
            {
                if (wasSniped) mark.SnipedAtUtc = null;
                else
                {
                    mark.SnipedAtUtc = DateTime.UtcNow;
                    mark.DeathObservedAtUtc = null;
                    mark.Dead = true;
                    _detector.RemoveSighting(mark.NameId, mark.Instance, mark.WorldId);
                }
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(mark.IsCustom
                ? "Rally stops have no sniped state"
                : TrainMutationBusy ? "Wait for the report operation to finish before editing the train"
                : wasSniped ? "Clear found-gone evidence; this mark stays dead with its kill time unknown"
                : "Found gone — record a missing mark without inventing a witnessed kill time");

            ImGui.SetCursorPos(new Vector2(rowStart.X + layout.X(3), actionsY));
            ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl);
            if (ImGui.Button("X", buttonSize) && ImGui.GetIO().KeyCtrl) toRemove = mark.Key;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(TrainMutationBusy
                ? "Wait for the report operation to finish before editing the train"
                : "Hold Ctrl and click to remove from route");
            ImGui.EndDisabled();

            if (dragging && _dragToIndex == i && _dragFromIndex != i)
            {
                var edgeY = _dragToIndex < _dragFromIndex ? rowMin.Y : rowMax.Y;
                ImGui.GetWindowDrawList().AddLine(new Vector2(rowMin.X, edgeY), new Vector2(rowMax.X, edgeY),
                    ImGui.GetColorU32(ImGuiCol.DragDropTarget), 2.5f);
            }
            if (!TrainMutationBusy && CanDragPresetTrain && _dragFromIndex == -1 && _dragExpansionFrom == -1
                && rowActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                _dragFromIndex = i;
                _dragMarkKey = mark.Key;
            }
            var droppableHere = _dragFromIndex >= 0 && _dragFromIndex < marks.Count
                && marks[_dragFromIndex].WorldId == mark.WorldId
                && (!grouping || TrainBlock(marks[_dragFromIndex]) == TrainBlock(mark));
            if (dragging && dropHovered && droppableHere)
            {
                _dragToIndex = i;
                _dragTargetKey = mark.Key;
            }
            if (_dragExpansionFrom != -1 && dropHovered)
            {
                var over = presentExpansions.IndexOf(TrainBlock(mark));
                if (over >= 0 && _dragExpansionFrom < presentExpansions.Count
                    && presentExpansions[_dragExpansionFrom].WorldId == mark.WorldId)
                {
                    _dragExpansionTo = over;
                    _dragExpansionTargetBlock = presentExpansions[over];
                }
            }
            if (_dragFromIndex == -1 && _dragExpansionFrom == -1 && rowFocused && rowHovered
                && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                _currentMark = mark.Key;
                if (_config.EchoOnMarkClick)
                {
                    var fullIndex = allMarks.FindIndex(m => m.Key == mark.Key);
                    TrainChatEcho.Send(_chatGui, _gameGui, mark, fullIndex < 0 ? i : fullIndex, allMarks.Count);
                }
                else MapFlagHelper.FlagMark(_gameGui, mark);
            }
            ImGui.SetCursorPos(new Vector2(rowStart.X, rowStart.Y + height + rowGap));
            ImGui.Separator();
            ImGui.PopID();
        }

        // --- floating preview under the cursor while dragging ---
        // Gemini suggested doing this inside BeginDragDropSource, but this
        // implementation tracks the drag manually rather than through an ImGui
        // payload, so a plain tooltip gives the same cursor-following preview.
        if (dragging && _dragFromIndex < marks.Count)
        {
            var source = marks[_dragFromIndex];
            var sourceZone = ExpansionData.Lookup(source.NameId)?.Location ?? "?";
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"「{sourceZone}」 {source.Name}{ExpansionData.InstanceGlyph(source.Instance)}");
            ImGui.EndTooltip();
        }

        if (_dragExpansionFrom >= 0 && _dragExpansionFrom < presentExpansions.Count)
        {
            var block = presentExpansions[_dragExpansionFrom];
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"{TrainWorldName(block.WorldId, marks)} / {block.Expansion} ({expansionCounts.GetValueOrDefault(block)})");
            ImGui.EndTooltip();
        }

        // --- commit the move exactly once, on release, after the loop ---
        if (!TrainMutationBusy && _dragFromIndex != -1 && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (_dragToIndex != -1
                && _dragToIndex != _dragFromIndex
                && _dragFromIndex < marks.Count
                && _dragToIndex < marks.Count)
            {
                // Translate the visible positions back to positions in the full
                // list, so dragging still lands correctly when dead marks are
                // hidden between the rows being moved.
                var moving = marks[_dragFromIndex];
                var target = marks[_dragToIndex];

                var fromFull = allMarks.FindIndex(m => m.Key == moving.Key);
                var toFull = allMarks.FindIndex(m => m.Key == target.Key);

                if (fromFull >= 0 && toFull >= 0)
                {
                    allMarks.RemoveAt(fromFull);
                    allMarks.Insert(toFull, moving);
                    ApplyManualTrainOrder(allMarks);
                }
            }

            _dragFromIndex = -1;
            _dragToIndex = -1;
            _dragMarkKey = null;
            _dragTargetKey = null;
        }

        // --- and the same for a whole expansion block ---
        if (!TrainMutationBusy && _dragExpansionFrom != -1 && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (_dragExpansionTo != -1
                && _dragExpansionTo != _dragExpansionFrom
                && _dragExpansionFrom < presentExpansions.Count
                && _dragExpansionTo < presentExpansions.Count)
            {
                MoveExpansion(
                    presentExpansions[_dragExpansionFrom],
                    presentExpansions[_dragExpansionTo]);
            }

            _dragExpansionFrom = -1;
            _dragExpansionTo = -1;
            _dragExpansionBlock = null;
            _dragExpansionTargetBlock = null;
        }

        if (!TrainMutationBusy && toRemove.HasValue) _detector.Remove(toRemove.Value);

        ImGui.Spacing();
        ImGui.TextWrapped(_config.EchoOnMarkClick ? "Click a mark to flag it and echo it to chat." : "Click a mark to flag it.");
        ImGui.TextWrapped(grouping
            ? "Drag rows within their expansion; drag a heading to reorder expansions within that world."
            : "Drag rows to reorder marks within their world.");
        if (PresetOrderLocked && !PresetOrderingPaused)
            ImGui.TextWrapped("Dragging pauses preset ordering until you reselect a preset.");

        DrawSRankWatchRows();
    }

    /// <summary>
    /// One expansion block heading, and the drag that reorders whole blocks.
    ///
    /// Like the mark drag it sits above, this only ever RECORDS where a drop
    /// would land. Nothing moves until the mouse is released, after the list
    /// has finished drawing — see DrawTrainList for why that matters.
    /// </summary>
    private bool DrawExpansionHeader(
        (uint WorldId, string Expansion) block, int index, int count, int upCount, float rowHeight,
        List<(uint WorldId, string Expansion)> blocks)
    {
        var expansion = block.Expansion;
        var collapsed = _config.CollapsedExpansions.Contains(TrainBlockKey(block))
                        || _config.CollapsedExpansions.Contains(expansion);

        ImGui.PushID($"expansion_{TrainBlockKey(block)}");
        ImGui.Spacing();
        var arrow = collapsed ? "▶" : "▼";
        var tally = count == 0 ? string.Empty : $" · {upCount}/{count}";
        var label = $"{arrow} {expansion}{tally}";
        var start = ImGui.GetCursorPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var height = Math.Max(rowHeight, ImGui.CalcTextSize(label, false, width).Y);
        ImGui.Selectable("##expansion", _dragExpansionFrom == index,
            ImGuiSelectableFlags.None, new Vector2(width, height));
        var headerMin = ImGui.GetItemRectMin();
        var headerMax = ImGui.GetItemRectMax();
        var headerHovered = ImGui.IsItemHovered();
        var dropHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var headerActive = ImGui.IsItemActive();
        ImGui.SetItemAllowOverlap();
        ImGui.SetCursorPos(start);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.78f, 1f, 1f));
        ImGui.PushTextWrapPos(start.X + width);
        ImGui.TextUnformatted(label);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.SetCursorPos(new Vector2(start.X, start.Y + height + ImGui.GetStyle().ItemSpacing.Y));

        // Click to fold the block away, drag to move it: the same split the
        // mark rows use, where a click flags and a drag reorders.
        if (_dragExpansionFrom == -1 && _dragFromIndex == -1
            && headerHovered
            && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
            && Math.Abs(ImGui.GetMouseDragDelta().Y) < 0.1f)
        {
            SetTrainBlockCollapsed(block, !collapsed);
            _config.DeferWindowStateSave();
            collapsed = !collapsed;
        }

        if (_dragExpansionFrom != -1 && _dragExpansionTo == index && _dragExpansionFrom != index)
        {
            var edgeY = _dragExpansionTo < _dragExpansionFrom ? headerMin.Y : headerMax.Y;
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(headerMin.X, edgeY),
                new Vector2(headerMax.X, edgeY),
                ImGui.GetColorU32(ImGuiCol.DragDropTarget),
                2.5f);
        }

        if (!TrainMutationBusy && CanDragPresetTrain && _dragExpansionFrom == -1
            && _dragFromIndex == -1
            && headerActive
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _dragExpansionFrom = index;
            _dragExpansionBlock = block;
        }

        if (_dragExpansionFrom >= 0 && _dragExpansionFrom < blocks.Count && dropHovered
            && blocks[_dragExpansionFrom].WorldId == block.WorldId)
        {
            _dragExpansionTo = index;
            _dragExpansionTargetBlock = block;
        }

        if (_dragExpansionFrom == -1 && _dragFromIndex == -1 && headerHovered)
            ImGui.SetTooltip((count > 0 ? $"{upCount} remaining / {count} recorded.\n" : "")
                + (PresetOrderLocked && !PresetOrderingPaused ? "Click to fold or open this expansion. Drag to move it and pause preset ordering." : collapsed
                ? "Click to open this expansion. Drag to reorder expansions within this world."
                : "Click to fold this expansion away. Drag to reorder expansions within this world."));

        ImGui.PopID();
        return collapsed;
    }

}
