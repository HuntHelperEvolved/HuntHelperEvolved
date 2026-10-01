using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using HuntHelperEvolved.TrainPresets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private bool _presetEditorOpen;
    private bool _presetEditorFocusRequested;
    private readonly PresetDraftSession _presetDrafts = new();
    private string _presetEditorStatus = "";
    private uint _presetZone;
    private uint? _presetReplacementZone;
    private string? _presetDraftRequest;
    private TrainPreset? _presetSubmittedDraft;
    private string? _presetDeletingId;
    private (string Id, string Name, bool Server, long Revision)? _presetDeletion;
    private DateTime _lastPresetApply;
    private bool SharingPresetTrain => _config.SyncEnabled && _config.SyncShareTrain;
    private string? ActivePresetId => SharingPresetTrain ? _sync.TrainPresets.ActivePresetId : _config.ActiveTrainPresetId;
    private bool PresetOrderLocked => ActivePresetId is not null;
    private bool PresetOrderingPaused => SharingPresetTrain ? _sync.TrainPresets.OrderingPaused : _config.TrainPresetOrderingPaused;
    private bool CanDragPresetTrain => !PresetOrderLocked || !SharingPresetTrain
        || _sync.HasCurrentTrainSnapshot && _sync.PendingPresetRequest is null;

    private void DrawPresetSelector(string label, float width)
    {
        var presets = SharingPresetTrain ? _sync.TrainPresets.Presets : _config.TrainPresets;
        var active = presets.FirstOrDefault(p => p.Id == ActivePresetId);
        ImGui.SetNextItemWidth(width);
        ImGui.BeginDisabled(TrainMutationBusy || SharingPresetTrain && (!_sync.SupportsTrainPresets
            || !_sync.HasCurrentTrainSnapshot || _sync.PendingPresetRequest is not null));
        if (ImGui.BeginCombo(label, active is null ? "Manual order" : active.Name + (PresetOrderingPaused ? " (paused)" : "")))
        {
            if (ImGui.Selectable("Manual order", ActivePresetId is null)) SelectTrainPreset(null);
            foreach (var preset in presets)
                if (ImGui.Selectable(preset.Name + "##" + preset.Id, ActivePresetId == preset.Id)) SelectTrainPreset(preset.Id);
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(SharingPresetTrain
                ? "Order existing and incoming marks for everyone. Reselecting resumes automatic ordering and restarts preset rally stops. The preset stays active until you choose Manual order or another preset."
                : "Order existing and incoming marks. Reselecting resumes automatic ordering and restarts preset rally stops. The preset stays active until you choose Manual order or another preset.");
    }

    private void DrawPresetControls(bool showStatus = true)
    {
        DrawPresetSelector("Route preset", Math.Min(280, Math.Max(120, ImGui.GetContentRegionAvail().X - 100)));
        TrainControlSameLine("Manage presets");
        if (ImGui.Button("Manage presets")) OpenPresetEditor();
        DrawCalculateRallyFlags();
        if (!showStatus) return;
        if (ActivePresetId is not null && PresetOrderingPaused)
            ImGui.TextWrapped("Automatic ordering is paused. Reselect a preset to resume, including for future trains.");
        if (SharingPresetTrain && !_sync.HasCurrentTrainSnapshot)
            ImGui.TextWrapped("Waiting for the server's train. Shared updates will resume after reconnecting.");
        else if (SharingPresetTrain && !_sync.SupportsTrainPresets)
            ImGui.TextWrapped("Shared presets require server 0.3.26 or later. Other train sharing remains available.");
        if (!string.IsNullOrEmpty(_sync.PresetStatus)) ImGui.TextWrapped(_sync.PresetStatus);
    }

    private string? RallyCalculationUnavailable
    {
        get
        {
            if (TrainMutationBusy) return "Wait for the current train operation to finish.";
            var presets = SharingPresetTrain ? _sync.TrainPresets.Presets : _config.TrainPresets;
            var preset = presets.FirstOrDefault(p => p.Id == ActivePresetId);
            if (preset is null) return "Select a route preset and adjust the train order to pause it first.";
            if (!PresetOrderingPaused) return "Adjust the train order to pause this preset first. Active presets calculate rally flags automatically.";
            if (SharingPresetTrain)
            {
                if (!_sync.HasCurrentTrainSnapshot) return "Wait for the server's current train before calculating rally flags.";
                if (!_sync.SupportsTrainPresets || !_sync.SupportsRallyRecalculation)
                    return "Shared rally calculation needs server 0.3.31 or later.";
                if (_sync.PendingPresetRequest is not null) return "Wait for the pending preset change to finish.";
            }
            return RouteCatalog.Validate(preset);
        }
    }

    private void DrawCalculateRallyFlags()
    {
        var unavailable = RallyCalculationUnavailable;
        ImGui.BeginDisabled(unavailable is not null);
        if (ImGui.Button("Calculate Rally Flags") && RallyCalculationUnavailable is null)
        {
            if (SharingPresetTrain) _sync.SendPreset("recalculate-rallies");
            else ApplyLocalTrainPreset(force: true, recalculateRallies: true);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(unavailable ?? "Update pending rally flags for the current train order using this preset's rally settings.\nThe mark order stays unchanged and the preset stays paused. Completed rallies stay completed."
                + (SharingPresetTrain ? "\nUpdates the shared train for everyone." : ""));
    }

    private void SelectTrainPreset(string? id)
    {
        if (TrainMutationBusy) return;
        if (SharingPresetTrain) _sync.SendPreset("select", presetId: id);
        else
        {
            _config.ActiveTrainPresetId = id;
            _config.TrainPresetOrderingPaused = false;
            _config.Save();
            ApplyLocalTrainPreset(force: true, restartRallies: true);
        }
        ClearTrainDrag();
    }

    private void ClearTrainDrag()
    {
        _dragFromIndex = _dragExpansionFrom = _dragToIndex = _dragExpansionTo = -1;
        _dragMarkKey = null;
        _dragTargetKey = null;
        _dragExpansionBlock = null;
        _dragExpansionTargetBlock = null;
    }

    private bool ApplyManualTrainOrder(List<DetectedMark> marks)
    {
        if (PresetOrderLocked && SharingPresetTrain)
            return _sync.SendPreset("reorder", order: marks.Select(m => Sync.SyncKey.From(m.Key)).ToList());
        if (PresetOrderLocked) _config.TrainPresetOrderingPaused = true;
        _detector.ApplyOrder(marks);
        PersistTrain();
        _config.Save();
        return true;
    }

    private void ApplyLocalTrainPreset(bool force = false, bool restartRallies = false, bool recalculateRallies = false)
    {
        if (SharingPresetTrain
            || _dragFromIndex != -1 || _dragExpansionFrom != -1
            || !force && DateTime.UtcNow - _lastPresetApply < TimeSpan.FromSeconds(1)) return;
        _lastPresetApply = DateTime.UtcNow;
        var preset = _config.TrainPresets.FirstOrDefault(p => p.Id == _config.ActiveTrainPresetId);
        if (preset is not null && RouteCatalog.Validate(preset) is not null) return;
        if (recalculateRallies && (preset is null || !_config.TrainPresetOrderingPaused)) return;
        var marks = _detector.Ordered();
        var flagChanged = false;
        var route = PresetRallies.Reconcile(marks, preset, _config.LocalPresetRallies,
            m => new RoutePoint(m.NameId, m.WorldId, m.TerritoryId, m.Instance, m.MapPosition.X, m.MapPosition.Y, m.Dead, m.IsCustom),
            (stop, id, existing) =>
            {
                var lead = marks.First(m => !m.IsCustom && m.WorldId == stop.Key.WorldId
                    && m.TerritoryId == stop.Key.TerritoryId && m.Instance == stop.Key.Instance);
                var flag = existing ?? new DetectedMark { NameId = id, WorldId = stop.Key.WorldId,
                    TerritoryId = stop.Key.TerritoryId, Instance = stop.Key.Instance, IsCustom = true,
                    FirstSeenUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow };
                var name = "Rally: " + stop.Aetheryte.Name;
                var position = new Vector2(stop.Aetheryte.X, stop.Aetheryte.Y);
                flagChanged |= existing is null || flag.Name != name || flag.MapPosition != position;
                flag.Name = name; flag.MapPosition = position;
                flag.MapId = lead.MapId;
                flag.WorldName = lead.WorldName;
                flag.ZoneName = RouteCatalog.ByTerritory[stop.Key.TerritoryId].Name;
                return flag;
            }, orderingPaused: _config.TrainPresetOrderingPaused, restartRallies: restartRallies,
            recalculateRallies: recalculateRallies);
        var order = route.Rows;
        foreach (var removed in marks.Except(order)) _detector.Remove(removed.Key);
        _detector.Merge(order.Where(m => !_detector.Marks.ContainsKey(m.Key)));
        if (order.Count == 0 && _config.LocalPresetRallies.Completed.Count > 0)
        {
            _config.LocalPresetRallies = new();
            flagChanged = true;
        }
        if (!order.SequenceEqual(marks) || flagChanged || route.ProgressChanged)
        {
            _detector.ApplyOrder(order);
            PersistTrain();
            _config.Save();
        }
    }

    private string? PresetServerMutationUnavailable => PresetEditorPolicy.ServerMutationUnavailable(
        _config.SyncEnabled, _sync.IsConnected, _sync.SupportsTrainPresets,
        _sync.PendingPresetRequest is not null || _presetDraftRequest is not null, TrainMutationBusy);

    private void OpenPresetEditor()
    {
        _presetEditorOpen = true;
        _presetEditorFocusRequested = true;
    }

    private void EditPreset(TrainPreset preset, PresetDraftSource source, bool unsaved = false, uint? earlierZone = null,
        bool replaceCurrent = false)
    {
        OpenPresetEditor();
        if (_presetDraftRequest is not null)
        {
            _presetEditorStatus = "Wait for the server to confirm this draft before switching presets.";
            return;
        }
        if (_presetDrafts.Open(preset, source, _sync.TrainPresets.Revision, unsaved, earlierZone, replaceCurrent))
            SelectPresetDraftZone(earlierZone);
        else _presetReplacementZone = earlierZone;
    }

    private void SelectPresetDraftZone(uint? preferred = null)
    {
        var draft = _presetDrafts.Draft;
        _presetZone = preferred ?? (draft?.Zones.Any(zone => zone.TerritoryId == _presetZone) == true
            ? _presetZone : draft?.Zones.FirstOrDefault()?.TerritoryId ?? 0);
        _presetEditorStatus = "";
    }

    private void DrawPresetReplacementConfirmation()
    {
        const string popup = "Discard unsaved preset changes?";
        if (!_presetDrafts.HasPendingReplacement) return;
        if (!ImGui.IsPopupOpen(popup)) ImGui.OpenPopup(popup);
        ImGui.SetNextWindowSize(new Vector2(Math.Min(430 * ImGuiHelpers.GlobalScale, ImGui.GetIO().DisplaySize.X - 40), 0));
        var open = true;
        if (ImGui.BeginPopupModal(popup, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextWrapped($"'{_presetDrafts.Draft?.Name}' has unsaved changes. Discard them and open the requested preset?");
            if (ImGui.Button("Discard changes"))
            {
                _presetDrafts.ConfirmReplacement();
                SelectPresetDraftZone(_presetReplacementZone);
                _presetReplacementZone = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Keep editing"))
            {
                _presetDrafts.CancelReplacement();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (!open) _presetDrafts.CancelReplacement();
    }

    private void DrawPresetDeletionConfirmation()
    {
        const string popup = "Delete saved preset?";
        if (_presetDeletion is not { } deletion) return;
        if (!ImGui.IsPopupOpen(popup)) ImGui.OpenPopup(popup);
        ImGui.SetNextWindowSize(new Vector2(Math.Min(430 * ImGuiHelpers.GlobalScale, ImGui.GetIO().DisplaySize.X - 40), 0));
        var open = true;
        if (ImGui.BeginPopupModal(popup, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextWrapped(deletion.Server
                ? $"Delete '{deletion.Name}' from the shared library for everyone?"
                : $"Delete '{deletion.Name}' from your local library?");
            if (deletion.Server ? _sync.TrainPresets.ActivePresetId == deletion.Id : _config.ActiveTrainPresetId == deletion.Id)
                ImGui.TextWrapped("This active preset will return to Manual order. Existing train marks remain.");
            ImGui.TextWrapped("Your open draft will be kept.");
            var unavailable = deletion.Server ? PresetServerMutationUnavailable
                : TrainMutationBusy ? "Wait for the current train operation to finish."
                : _presetDraftRequest is not null ? "Wait for the server to confirm this draft." : null;
            ImGui.BeginDisabled(unavailable is not null);
            if (ImGui.Button(deletion.Server ? "Delete from server" : "Delete locally"))
            {
                if (deletion.Server)
                {
                    if (_sync.SendPreset("delete", presetId: deletion.Id, baseRevision: deletion.Revision))
                    {
                        _presetDraftRequest = _sync.PendingPresetRequest;
                        _presetDeletingId = deletion.Id;
                        _presetSubmittedDraft = null;
                    }
                }
                else
                {
                    _config.TrainPresets.RemoveAll(preset => preset.Id == deletion.Id);
                    if (_config.ActiveTrainPresetId == deletion.Id)
                    {
                        _config.ActiveTrainPresetId = null;
                        _config.TrainPresetOrderingPaused = false;
                    }
                    _config.Save();
                    if (_presetDrafts.Draft?.Id == deletion.Id) _presetDrafts.MarkUnsaved();
                    _presetEditorStatus = "Deleted locally. The draft is still open.";
                }
                _presetDeletion = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && unavailable is not null) ImGui.SetTooltip(unavailable);
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _presetDeletion = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (!open) _presetDeletion = null;
    }

    private void UpdatePresetDraftRequest()
    {
        if (_presetDraftRequest is not null && _sync.PendingPresetRequest is null)
        {
            if (_presetDraftRequest == _sync.CompletedPresetRequest && _sync.AcceptedPresetRevision is { } revision)
            {
                if (_presetSubmittedDraft is { } saved) _presetDrafts.MarkSaved(saved, revision, PresetDraftSource.Server);
                else if (_presetDrafts.Draft?.Id == _presetDeletingId) _presetDrafts.MarkUnsaved(revision);
            }
            _presetDraftRequest = null;
            _presetSubmittedDraft = null;
            _presetDeletingId = null;
        }
    }

    private void DrawPresetEditorContents()
    {
        ImGui.TextWrapped("Save a conductor's preferences for future trains, then select the preset before scouting. Strict zones use your mark order; other zones use estimated travel distance from an aetheryte.");
        ImGui.BeginDisabled(_presetDraftRequest is not null);
        ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - 120));
        if (ImGui.BeginCombo("Edit preset", _presetDrafts.Draft?.Name is { Length: > 0 } name ? name : "Choose a saved preset"))
        {
            foreach (var preset in _config.TrainPresets)
                if (ImGui.Selectable("Local: " + preset.Name + "##local" + preset.Id)) EditPreset(preset, PresetDraftSource.Local);
            foreach (var preset in _sync.TrainPresets.Presets)
                if (ImGui.Selectable("Server: " + preset.Name + "##server" + preset.Id)) EditPreset(preset, PresetDraftSource.Server);
            ImGui.EndCombo();
        }
        if (ImGui.Button("New preset"))
        {
            var preset = new TrainPreset { ExcludedAetheryteIds = TeleportHelper.Blacklist.ToList() };
            foreach (var expansion in new[] { "Dawntrail", "Shadowbringers", "Endwalker" })
                foreach (var zone in RouteCatalog.Zones.Where(z => z.Expansion == expansion)) PresetEditing.AddZone(preset, zone.TerritoryId);
            EditPreset(preset, PresetDraftSource.New, unsaved: true);
        }
        ImGui.EndDisabled();
        if (_presetDrafts.Draft is not { } draft)
        {
            ImGui.TextWrapped("Create a preset or choose one from your local or server library.");
            return;
        }
        TrainControlSameLine("Duplicate");
        ImGui.BeginDisabled(_presetDraftRequest is not null);
        if (ImGui.Button("Duplicate"))
        {
            var duplicate = draft.Copy();
            duplicate.Id = Guid.NewGuid().ToString("N");
            duplicate.Name = duplicate.Name.Length <= 73 ? duplicate.Name + " (copy)" : duplicate.Name[..73] + " (copy)";
            EditPreset(duplicate, PresetDraftSource.New, unsaved: true);
            draft = _presetDrafts.Draft!;
        }
        ImGui.EndDisabled();
        var reloadPreset = _presetDrafts.Source switch
        {
            PresetDraftSource.Local => _config.TrainPresets.FirstOrDefault(preset => preset.Id == draft.Id),
            PresetDraftSource.Server => _sync.TrainPresets.Presets.FirstOrDefault(preset => preset.Id == draft.Id),
            _ => null,
        };
        if (reloadPreset is not null)
        {
            TrainControlSameLine("Reload saved...");
            ImGui.BeginDisabled(_presetDraftRequest is not null);
            if (ImGui.Button("Reload saved..."))
            {
                EditPreset(reloadPreset, _presetDrafts.Source, replaceCurrent: true);
                draft = _presetDrafts.Draft!;
            }
            ImGui.EndDisabled();
        }
        if (_presetDrafts.IsDirty) ImGui.TextColored(HuntTheme.Warning, "Unsaved changes");
        var draftName = draft.Name;
        ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - 65));
        if (ImGui.InputText("Name", ref draftName, 81)) draft.Name = draftName;
        var rallyInstances = draft.RallyInInstancedZones;
        if (ImGui.Checkbox("Rally on zone entry and instance changes", ref rallyInstances))
            draft.RallyInInstancedZones = rallyInstances;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("In instanced zones, add an aetheryte flag before the marks in i1, i2, and so on. No extra rally between marks in the same instance.");
        if (ImGui.BeginCombo("Add expansion", "Choose an expansion"))
        {
            foreach (var expansion in RouteCatalog.Zones.GroupBy(z => z.Expansion))
                if (ImGui.Selectable(expansion.Key))
                    foreach (var zone in expansion) PresetEditing.AddZone(draft, zone.TerritoryId);
            ImGui.EndCombo();
        }
        if (ImGui.BeginCombo("Add zone", "Choose a zone"))
        {
            foreach (var zone in RouteCatalog.Zones.Where(z => !draft.Zones.Any(p => p.TerritoryId == z.TerritoryId)))
                if (ImGui.Selectable(zone.Name))
                {
                    PresetEditing.AddZone(draft, zone.TerritoryId);
                    _presetZone = zone.TerritoryId;
                }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped("Zones omitted from the preset stay after its listed zones in their expansion. Each zone runs through instances in numerical order.");
        if (ImGui.BeginChild("Preset zones", new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y - 260)), true))
        {
            foreach (var expansion in draft.Zones.GroupBy(z => RouteCatalog.ByTerritory[z.TerritoryId].Expansion).ToList())
            {
                ImGui.PushID(expansion.Key);
                ImGui.Separator();
                ImGui.TextUnformatted(expansion.Key);
                var groups = draft.Zones.Select(z => RouteCatalog.ByTerritory[z.TerritoryId].Expansion).Distinct().ToList();
                if (groups.IndexOf(expansion.Key) > 0)
                {
                    var rallyExpansion = draft.RallyBeforeExpansions.Contains(expansion.Key);
                    if (ImGui.Checkbox("Rally when entering " + expansion.Key, ref rallyExpansion))
                    {
                        if (rallyExpansion) draft.RallyBeforeExpansions.Add(expansion.Key);
                        else draft.RallyBeforeExpansions.Remove(expansion.Key);
                    }
                }
                if (groups.IndexOf(expansion.Key) > 0 && ImGui.SmallButton("Earlier expansion"))
                    PresetEditing.MoveExpansion(draft, expansion.Key, -1);
                if (groups.IndexOf(expansion.Key) < groups.Count - 1)
                {
                    TrainControlSameLine("Later expansion");
                    if (ImGui.SmallButton("Later expansion")) PresetEditing.MoveExpansion(draft, expansion.Key, 1);
                }
                foreach (var zone in expansion.ToList())
                {
                    var info = RouteCatalog.ByTerritory[zone.TerritoryId];
                    if (ImGui.Selectable(info.Name + (zone.Strict ? " (strict)" : ""), _presetZone == zone.TerritoryId))
                        _presetZone = zone.TerritoryId;
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        if (draft.Zones.FirstOrDefault(z => z.TerritoryId == _presetZone) is { } selected)
        {
            ImGui.TextUnformatted(RouteCatalog.ByTerritory[_presetZone].Name);
            var sameExpansion = draft.Zones.Where(z => RouteCatalog.ByTerritory[z.TerritoryId].Expansion == RouteCatalog.ByTerritory[_presetZone].Expansion).ToList();
            if (sameExpansion.IndexOf(selected) > 0 && ImGui.Button("Earlier zone")) PresetEditing.MoveZone(draft, _presetZone, -1);
            if (sameExpansion.IndexOf(selected) < sameExpansion.Count - 1)
            {
                TrainControlSameLine("Later zone");
                if (ImGui.Button("Later zone")) PresetEditing.MoveZone(draft, _presetZone, 1);
            }
            TrainControlSameLine("Remove zone");
            if (ImGui.Button("Remove zone")) draft.Zones.Remove(selected);
            var entries = RouteCatalog.ByTerritory[_presetZone].Aetherytes.Where(a => !draft.ExcludedAetheryteIds.Contains(a.Id)).ToList();
            if (ImGui.BeginCombo("Entry / rally aetheryte", selected.EntryAetheryteId == 0 ? "Automatic"
                : entries.FirstOrDefault(a => a.Id == selected.EntryAetheryteId)?.Name ?? "Choose an available aetheryte"))
            {
                if (ImGui.Selectable("Automatic", selected.EntryAetheryteId == 0)) selected.EntryAetheryteId = 0;
                foreach (var entry in entries)
                    if (ImGui.Selectable(entry.Name, selected.EntryAetheryteId == entry.Id)) selected.EntryAetheryteId = entry.Id;
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Automatic uses the allowed aetheryte with the shortest estimated flight to the first mark, including Tertium's northern exit. Choosing one also sets the starting point for automatic mark order in this zone.");
            var strict = selected.Strict;
            if (ImGui.Checkbox("Strict mark order", ref strict))
            {
                selected.Strict = strict;
                if (strict && selected.MarkOrder.Count == 0)
                    selected.MarkOrder = RouteCatalog.ByTerritory[_presetZone].Marks.Select(m => m.NameId).ToList();
            }
            if (selected.Strict)
            {
                var info = RouteCatalog.ByTerritory[_presetZone];
                ImGui.TextWrapped(string.Join(" > ", selected.MarkOrder.Select(id => info.Marks.First(m => m.NameId == id).Name)));
                if (selected.MarkOrder.Count > 1 && ImGui.Button("Reverse mark order")) selected.MarkOrder.Reverse();
            }
        }
        var problem = RouteCatalog.Validate(draft);
        if (problem is not null) ImGui.TextWrapped(problem);
        if (_sync.TrainPresets.ActivePresetId == draft.Id)
            ImGui.TextWrapped(_sync.TrainPresets.OrderingPaused
                ? "Ordering is paused. Saving this preset keeps the current route until you reselect a preset."
                : "Updating this active server preset also changes everyone's current route.");
        var localUnavailable = TrainMutationBusy ? "Wait for the current train operation to finish."
            : _presetDraftRequest is not null ? "Wait for the server to confirm this draft." : problem;
        ImGui.BeginDisabled(localUnavailable is not null);
        if (ImGui.Button("Save locally"))
        {
            draft.Name = draft.Name.Trim();
            var index = _config.TrainPresets.FindIndex(p => p.Id == draft.Id);
            if (index < 0) _config.TrainPresets.Add(draft.Copy()); else _config.TrainPresets[index] = draft.Copy();
            _config.Save();
            ApplyLocalTrainPreset(force: true);
            _presetDrafts.MarkSaved(draft, source: PresetDraftSource.Local);
            _presetEditorStatus = "Saved locally.";
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && localUnavailable is not null) ImGui.SetTooltip(localUnavailable);
        TrainControlSameLine("Share / update server");
        var serverUnavailable = PresetServerMutationUnavailable ?? problem;
        ImGui.BeginDisabled(serverUnavailable is not null);
        if (ImGui.Button("Share / update server"))
        {
            draft.Name = draft.Name.Trim();
            if (_sync.SendPreset("save", draft, baseRevision: _presetDrafts.ServerRevision))
            {
                _presetDraftRequest = _sync.PendingPresetRequest;
                _presetSubmittedDraft = draft.Copy();
                _presetDeletingId = null;
                _presetEditorStatus = "Select the shared preset in Route preset after it has been saved.";
            }
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && serverUnavailable is not null) ImGui.SetTooltip(serverUnavailable);
        if (_config.TrainPresets.FirstOrDefault(preset => preset.Id == draft.Id) is { } localPreset)
        {
            ImGui.BeginDisabled(TrainMutationBusy || _presetDraftRequest is not null);
            if (ImGui.Button("Delete local preset..."))
                _presetDeletion = (localPreset.Id, localPreset.Name, false, 0);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && (TrainMutationBusy || _presetDraftRequest is not null))
                ImGui.SetTooltip(TrainMutationBusy ? "Wait for the current train operation to finish." : "Wait for the server to confirm this draft.");
        }
        if (_sync.TrainPresets.Presets.FirstOrDefault(preset => preset.Id == draft.Id) is { } serverPreset)
        {
            var deleteUnavailable = PresetServerMutationUnavailable;
            ImGui.BeginDisabled(deleteUnavailable is not null);
            if (ImGui.Button("Delete server preset..."))
                _presetDeletion = (serverPreset.Id, serverPreset.Name, true, _sync.TrainPresets.Revision);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && deleteUnavailable is not null) ImGui.SetTooltip(deleteUnavailable);
        }
        if (ImGui.Button("Use current aetheryte exclusions"))
        {
            draft.ExcludedAetheryteIds = TeleportHelper.Blacklist.ToList();
            _presetEditorStatus = "Copied your aetheryte exclusions into this draft.";
        }
        ImGui.TextWrapped("Rallies use custom flags and their usual teleport behaviour. Leave rally options off for no rally stops. Travel estimates use map distance and Tertium's northern exit; other terrain and teleport loading times are not modelled.");
        if (!string.IsNullOrEmpty(_presetEditorStatus)) ImGui.TextWrapped(_presetEditorStatus);
        if (!string.IsNullOrEmpty(_sync.PresetStatus)) ImGui.TextWrapped(_sync.PresetStatus);
        DrawPresetReplacementConfirmation();
        DrawPresetDeletionConfirmation();
    }
}
