using Dalamud.Bindings.ImGui;
using HuntHelperEvolved.TrainPresets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private bool _presetEditorOpen;
    private TrainPreset? _presetDraft;
    private string _presetEditorStatus = "";
    private uint _presetZone;
    private long _presetDraftRevision;
    private string? _presetDraftRequest;
    private DateTime _lastPresetApply;
    private bool SharingPresetTrain => _config.SyncEnabled && _config.SyncShareTrain;
    private string? ActivePresetId => SharingPresetTrain ? _sync.TrainPresets.ActivePresetId : _config.ActiveTrainPresetId;
    private bool PresetOrderLocked => ActivePresetId is not null;
    private bool PresetOrderingPaused => SharingPresetTrain ? _sync.TrainPresets.OrderingPaused : _config.TrainPresetOrderingPaused;
    private bool CanDragPresetTrain => !PresetOrderLocked || !SharingPresetTrain
        || _sync.HasCurrentTrainSnapshot && _sync.PendingPresetRequest is null;

    private void DrawPresetControls(bool showStatus = true)
    {
        var presets = SharingPresetTrain ? _sync.TrainPresets.Presets : _config.TrainPresets;
        var active = presets.FirstOrDefault(p => p.Id == ActivePresetId);
        ImGui.SetNextItemWidth(Math.Min(280, Math.Max(120, ImGui.GetContentRegionAvail().X - 100)));
        ImGui.BeginDisabled(SharingPresetTrain && (!_sync.SupportsTrainPresets
            || !_sync.HasCurrentTrainSnapshot || _sync.PendingPresetRequest is not null));
        if (ImGui.BeginCombo("Route preset", active is null ? "Manual order" : active.Name + (PresetOrderingPaused ? " (paused)" : "")))
        {
            if (ImGui.Selectable("Manual order", ActivePresetId is null)) SelectTrainPreset(null);
            foreach (var preset in presets)
                if (ImGui.Selectable(preset.Name + "##" + preset.Id, ActivePresetId == preset.Id)) SelectTrainPreset(preset.Id);
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(SharingPresetTrain
                ? "Select before scouting to organise incoming marks for everyone. The preset stays active for future trains until you choose Manual order or another preset."
                : "Select before scouting to organise incoming marks. The preset stays active for future trains until you choose Manual order or another preset.");
        TrainControlSameLine("Manage presets");
        if (ImGui.Button("Manage presets")) _presetEditorOpen = true;
        if (!showStatus) return;
        if (active is not null && PresetOrderingPaused)
            ImGui.TextWrapped("Automatic ordering is paused. Reselect a preset to resume, including for future trains.");
        if (SharingPresetTrain && !_sync.HasCurrentTrainSnapshot)
            ImGui.TextWrapped("Waiting for the server's train. Shared updates will resume after reconnecting.");
        else if (SharingPresetTrain && !_sync.SupportsTrainPresets)
            ImGui.TextWrapped("Shared presets require server 0.3.26 or later. Other train sharing remains available.");
        if (!string.IsNullOrEmpty(_sync.PresetStatus)) ImGui.TextWrapped(_sync.PresetStatus);
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

    private void ApplyLocalTrainPreset(bool force = false, bool restartRallies = false)
    {
        if (SharingPresetTrain
            || _dragFromIndex != -1 || _dragExpansionFrom != -1
            || !force && DateTime.UtcNow - _lastPresetApply < TimeSpan.FromSeconds(1)) return;
        _lastPresetApply = DateTime.UtcNow;
        var preset = _config.TrainPresets.FirstOrDefault(p => p.Id == _config.ActiveTrainPresetId);
        if (preset is not null && RouteCatalog.Validate(preset) is not null) return;
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
            }, orderingPaused: _config.TrainPresetOrderingPaused, restartRallies: restartRallies);
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

    private void EditPreset(TrainPreset preset)
    {
        _presetDraftRevision = _sync.TrainPresets.Revision;
        _presetDraftRequest = null;
        _presetDraft = preset.Copy();
        _presetZone = preset.Zones.FirstOrDefault()?.TerritoryId ?? 0;
        _presetEditorStatus = "";
    }

    private void DrawPresetEditor()
    {
        if (!_presetEditorOpen) return;
        if (_presetDraftRequest is not null && _sync.PendingPresetRequest is null)
        {
            if (_presetDraftRequest == _sync.CompletedPresetRequest && _sync.AcceptedPresetRevision is { } revision)
                _presetDraftRevision = revision;
            _presetDraftRequest = null;
        }
        ImGui.SetNextWindowSize(new Vector2(640, 700), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Train presets", ref _presetEditorOpen))
        {
            ImGui.End();
            return;
        }
        ImGui.TextWrapped("Save a conductor's preferences for future trains, then select the preset before scouting. Strict zones use your mark order; other zones use estimated travel distance from an aetheryte.");
        ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - 120));
        if (ImGui.BeginCombo("Edit preset", _presetDraft?.Name is { Length: > 0 } name ? name : "Choose a saved preset"))
        {
            foreach (var preset in _config.TrainPresets)
                if (ImGui.Selectable("Local: " + preset.Name + "##local" + preset.Id)) EditPreset(preset);
            foreach (var preset in _sync.TrainPresets.Presets)
                if (ImGui.Selectable("Server: " + preset.Name + "##server" + preset.Id)) EditPreset(preset);
            ImGui.EndCombo();
        }
        if (ImGui.Button("New preset"))
        {
            var preset = new TrainPreset { ExcludedAetheryteIds = TeleportHelper.Blacklist.ToList() };
            foreach (var expansion in new[] { "Dawntrail", "Shadowbringers", "Endwalker" })
                foreach (var zone in RouteCatalog.Zones.Where(z => z.Expansion == expansion)) PresetEditing.AddZone(preset, zone.TerritoryId);
            EditPreset(preset);
        }
        if (_presetDraft is not { } draft)
        {
            ImGui.TextWrapped("Create a preset or choose one from your local or server library.");
            ImGui.End();
            return;
        }
        TrainControlSameLine("Duplicate");
        if (ImGui.Button("Duplicate"))
        {
            draft = draft.Copy();
            draft.Id = Guid.NewGuid().ToString("N");
            draft.Name = draft.Name.Length <= 73 ? draft.Name + " (copy)" : draft.Name[..73] + " (copy)";
            EditPreset(draft);
        }
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
        if (ImGui.Button("Save locally"))
        {
            if (problem is not null) _presetEditorStatus = problem;
            else
            {
                draft.Name = draft.Name.Trim();
                var index = _config.TrainPresets.FindIndex(p => p.Id == draft.Id);
                if (index < 0) _config.TrainPresets.Add(draft.Copy()); else _config.TrainPresets[index] = draft.Copy();
                _config.Save();
                ApplyLocalTrainPreset(force: true);
                _presetEditorStatus = "Saved locally.";
            }
        }
        TrainControlSameLine("Share / update server");
        if (ImGui.Button("Share / update server"))
        {
            if (problem is not null) _presetEditorStatus = problem;
            else if (_sync.SendPreset("save", draft, baseRevision: _presetDraftRevision))
            {
                _presetDraftRequest = _sync.PendingPresetRequest;
                _presetEditorStatus = "Select the shared preset in Route preset after it has been saved.";
            }
        }
        if (_config.TrainPresets.Any(p => p.Id == draft.Id) && ImGui.Button("Remove local preset"))
        {
            _config.TrainPresets.RemoveAll(p => p.Id == draft.Id);
            if (_config.ActiveTrainPresetId == draft.Id)
            {
                _config.ActiveTrainPresetId = null;
                _config.TrainPresetOrderingPaused = false;
            }
            _config.Save();
            _presetEditorStatus = "Removed locally. The draft is still open.";
        }
        if (_sync.TrainPresets.Presets.Any(p => p.Id == draft.Id) && ImGui.Button("Remove server preset"))
            _sync.SendPreset("delete", presetId: draft.Id);
        if (ImGui.Button("Use current aetheryte exclusions"))
        {
            draft.ExcludedAetheryteIds = TeleportHelper.Blacklist.ToList();
            _presetEditorStatus = "Copied your aetheryte exclusions into this draft.";
        }
        ImGui.TextWrapped("Rallies use custom flags and their usual teleport behaviour. Leave rally options off for no rally stops. Travel estimates use map distance and Tertium's northern exit; other terrain and teleport loading times are not modelled.");
        if (!string.IsNullOrEmpty(_presetEditorStatus)) ImGui.TextWrapped(_presetEditorStatus);
        if (!string.IsNullOrEmpty(_sync.PresetStatus)) ImGui.TextWrapped(_sync.PresetStatus);
        ImGui.End();
    }
}
