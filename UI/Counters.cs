using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using HuntHelperEvolved.Sync;
using System;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private int _counterBrowserInstance;

    private void DrawSelectedSRankCounters(SRankTimer timer, uint worldId, uint instance)
    {
        var definition = HuntCounter.Definitions.FirstOrDefault(d => d.TerritoryId == timer.TerritoryId);
        if (definition is not null)
            DrawCounterEvidence(definition, worldId, instance);
        if (!SpawnWatchCounters.AppliesTo(timer.TerritoryId)) return;
        var current = SRankWorkspaceScope.IsLiveScope(worldId, timer.TerritoryId, instance,
            _detector.CurrentWorldId(), _detector.CurrentTerritoryId, MarkDetector.GetCurrentInstance());
        var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 34 ? 2 : 1;
        if (ImGui.BeginTable("Live observations", columns, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Current-zone observations");
            ImGui.Spacing();
            if (current)
            {
                ImGui.PushTextWrapPos(0);
                DrawSpawnWatches();
                ImGui.PopTextWrapPos();
            }
            else ImGui.TextWrapped($"Unavailable outside {timer.Zone} on {_worldData.NameOf(worldId)}"
                + (instance == 0 ? " (uninstanced)." : $", instance {instance}."));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(timer.Name);
            ImGui.Spacing();
            ImGui.TextDisabled("Observation scope");
            ImGui.TextWrapped($"{_worldData.NameOf(worldId)} / {timer.Zone}" + (instance == 0 ? " / uninstanced" : $" / I{instance}"));
            ImGui.TextColored(current ? HuntTheme.Success : HuntTheme.Muted, current ? "Observing here" : "Not at this location");
            ImGui.EndTable();
        }
    }

    private void DrawCounterEvidence(CounterDefinition definition, uint world, uint instance)
    {
        ImGui.PushID($"selectedCounter{definition.MarkName}:{world}:{instance}");
        var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 34 ? 2 : 1;
        if (ImGui.BeginTable("Counter evidence", columns, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(definition.TriggerPatterns.Length == 0 ? "Kill contributions" : "Trigger counts");
            ImGui.Spacing();
            if (ImGui.BeginTable("Trigger counts", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH))
            {
                var personal = _config.CountOnlyMyKills || definition.TriggerPatterns.Length > 0;
                ImGui.TableSetupColumn("Mob / event", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(personal ? "Mine" : "Nearby", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.5f);
                ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.5f);
                ImGui.TableHeadersRow();
                foreach (var mob in definition.MobNames)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(mob);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(_counter.GetTally(world, instance, mob).ToString());
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(personal
                        ? "Locally recorded personal events. Local resets do not change the group total."
                        : "Locally recorded nearby kills. Nearby kills are not uploaded to the group total.");
                    ImGui.TableNextColumn();
                    var shared = definition.TriggerPatterns.Length == 0
                        ? _sync.SharedCounterTotal(world, definition.TerritoryId, instance, mob) : null;
                    ImGui.TextUnformatted(shared?.ToString() ?? "-");
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(definition.TriggerPatterns.Length > 0
                        ? "This event does not have a shared group counter."
                        : shared is null ? "Connect to a server with counter syncing enabled."
                        : "Group total of personal kills since the shared reset. Older local counts and nearby kills are not uploaded.");
                }
                ImGui.EndTable();
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted("Attempt");
            ImGui.Spacing();
            ImGui.TextDisabled("Scope");
            ImGui.SameLine();
            ImGui.TextUnformatted(_worldData.NameOf(world) + (instance == 0 ? " / uninstanced" : $" / I{instance}"));
            var last = _counter.GetLastKill(world, instance, definition.MarkName);
            ImGui.TextDisabled("Local event");
            ImGui.SameLine();
            ImGui.TextUnformatted(last is { } lastEvent ? $"{FormatRemaining(DateTime.UtcNow - lastEvent)} ago" : "No events recorded");
            ImGui.Spacing();
            if (HuntUi.Button("resetLocal", "Reset local counts", FontAwesomeIcon.Undo))
                _counter.ResetFor(definition, world, instance);
            if (definition.TriggerPatterns.Length == 0)
            {
                ImGui.BeginDisabled(!_sync.CountersAvailable);
                if (HuntUi.Button("resetGroup", "Reset group attempt", FontAwesomeIcon.Undo,
                    tooltip: _sync.CountersAvailable ? "Reset this mark's shared counts for the selected world and instance." : "Connect to a server with counter syncing enabled."))
                    ImGui.OpenPopup("Reset selected group attempt");
                ImGui.EndDisabled();
                if (ImGui.BeginPopup("Reset selected group attempt"))
                {
                    ImGui.TextWrapped($"Clear group counts for {definition.MarkName} on {_worldData.NameOf(world)}"
                        + (instance == 0 ? " (uninstanced)?" : $", instance {instance}?"));
                    if (HuntUi.Button("confirmResetGroup", "Reset attempt", FontAwesomeIcon.Undo))
                    {
                        _sync.ResetSharedCounters(definition, world, instance);
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (HuntUi.Button("cancelResetGroup", "Cancel")) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
            }
            ImGui.Spacing();
            var settings = _counter.SettingsFor(definition.MarkName);
            var autoReset = settings.AutoResetEnabled;
            if (ImGui.Checkbox("Auto-reset local counts", ref autoReset))
            {
                settings.AutoResetEnabled = autoReset;
                _config.Save();
            }
            if (settings.AutoResetEnabled)
            {
                var hours = settings.AutoResetHours;
                ImGui.SetNextItemWidth(95);
                if (ImGui.InputInt("hours", ref hours))
                {
                    settings.AutoResetHours = Math.Clamp(hours, 1, 9);
                    _config.Save();
                }
                if (last is { } lastKill)
                {
                    var remaining = lastKill.AddHours(Math.Clamp(settings.AutoResetHours, 1, 9)) - DateTime.UtcNow;
                    ImGui.TextDisabled(remaining > TimeSpan.Zero ? $"Resets in {FormatRemaining(remaining)}" : "Resetting");
                }
            }
            ImGui.EndTable();
        }
        ImGui.PopID();
    }

    private void DrawSelectedSRankTrainWatch(SRankTimer timer, uint worldId, uint instance)
    {
        var matches = _config.Flags.Where(f => SRankWorkspaceScope.MatchesWatch(f, timer.Name, timer.TerritoryId, worldId, instance)).ToList();
        var legacy = _config.Flags.Count(f => f.WorldId == 0 && SRankWorkspaceScope.MatchesMark(f, timer.Name, timer.TerritoryId));
        var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 34 ? 2 : 1;
        if (!ImGui.BeginTable("Train watch evidence", columns, ImGuiTableFlags.SizingStretchSame)) return;
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Train check");
        ImGui.Spacing();
        ImGui.TextDisabled("Scope");
        ImGui.TextWrapped($"{_worldData.NameOf(worldId)} / {timer.Zone}" + (instance == 0 ? " / uninstanced" : $" / I{instance}"));
        ImGui.TextDisabled("Source");
        ImGui.TextUnformatted(matches.Any(f => f.Automatic) ? "Automatic train watch" : "Manual train watch");
        if (legacy > 0) ImGui.TextWrapped($"Unscoped train watches: {legacy}. Available in Train > Plan.");
        ImGui.TableNextColumn();
        var supported = !_config.SyncShareTrain || _sync.SupportsScopedTrainWatches;
        if (matches.Count == 0)
        {
            var narrowRift = timer.TerritoryId == SpawnWatchCounters.UltimaThuleTerritory;
            if (narrowRift)
            {
                var labels = NarrowRiftSpawns.Select((s, i) => $"Spawn {i + 1} ({s.X:F1}, {s.Y:F1})").ToArray();
                ImGui.SetNextItemWidth(Math.Min(240, ImGui.GetContentRegionAvail().X));
                ImGui.Combo("##selectedNarrowRiftSpawn", ref _selectedNarrowRiftSpawn, labels, labels.Length);
            }
            ImGui.BeginDisabled(TrainMutationBusy || !supported || worldId == 0);
            if (HuntUi.Button("addSelectedWatch", "Add train watch", FontAwesomeIcon.Plus))
            {
                var watch = new FlagEntry { Label = timer.Name, TerritoryId = timer.TerritoryId, WorldId = worldId, Instance = instance };
                if (narrowRift)
                {
                    var spot = NarrowRiftSpawns[_selectedNarrowRiftSpawn];
                    watch.Label = $"Narrow-rift - Spawn {_selectedNarrowRiftSpawn + 1} ({spot.X:F1}, {spot.Y:F1})";
                    watch.HasLocation = true;
                    watch.X = spot.X;
                    watch.Y = spot.Y;
                }
                _config.Flags.Add(watch);
                _config.Save();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !supported)
                ImGui.SetTooltip("Scoped watches require an updated sync server when train sharing is enabled.");
        }
        foreach (var flag in matches)
        {
            ImGui.PushID(_config.Flags.IndexOf(flag));
            ImGui.TextWrapped(flag.Label + (flag.Automatic ? " (automatic)" : ""));
            ImGui.BeginDisabled(TrainMutationBusy || !supported);
            DrawSpawnStatusBoxes(flag);
            ImGui.BeginDisabled(flag.Automatic && _config.AutoTrainWatches);
            if (HuntUi.Button("removeSelectedWatch", "Remove watch", FontAwesomeIcon.Trash, quiet: true)) { _config.Flags.Remove(flag); _config.Save(); }
            ImGui.EndDisabled();
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        ImGui.PushTextWrapPos(0);
        ImGui.TextDisabled("Train checklist only / kill timer unchanged");
        ImGui.PopTextWrapPos();
        ImGui.EndTable();
    }

    private void DrawCountersTab()
    {
        if (ImGui.BeginChild("S-rank counters", new Vector2(0, 0), false))
            DrawCountersContents();
        ImGui.EndChild();
    }

    private void DrawCountersContents()
    {
        if (HuntUi.Button("resetAllLocalCounters", "Reset all local counts", FontAwesomeIcon.Undo))
            ImGui.OpenPopup("Reset all local counters");
        ImGui.SetNextWindowSizeConstraints(new Vector2(250, 0), new Vector2(420, float.MaxValue));
        if (ImGui.BeginPopup("Reset all local counters"))
        {
            ImGui.TextWrapped("Clear every stored trigger count for all marks, worlds and instances on this client?");
            ImGui.TextWrapped("Group counts, train records and lifetime tally totals will not change. This cannot be undone.");
            ImGui.Spacing();
            if (HuntUi.Button("confirmResetAllLocalCounters", "Reset all local counts", FontAwesomeIcon.Undo))
            {
                _counter.Reset();
                ImGui.CloseCurrentPopup();
            }
            HuntUi.SameLineIfFits(HuntUi.ButtonWidth("Cancel"));
            if (HuntUi.Button("cancelResetAllLocalCounters", "Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.TextDisabled("Stored trigger counts");

        // World picker — the popout always shows where you're standing, but you may want to check counts for another world entirely.
        var dcs = _worldData.DataCenters;
        if (dcs.Count > 0)
        {
            // Follow the player's world when it changes, then leave the picker
            // alone so a manual selection isn't fought.
            var liveWorld = _counter.CurrentWorldId();
            if (liveWorld != 0 && liveWorld != _lastSeenWorldId)
            {
                _lastSeenWorldId = liveWorld;
                if (_worldData.LocateWorld(liveWorld) is { } located)
                {
                    _counterDcIndex = located.DcIndex;
                    _counterWorldIndex = located.WorldIndex;
                    _counterBrowserInstance = (int)MarkDetector.GetCurrentInstance();
                }
            }

            ImGui.Spacing();
            var dcNames = dcs.Select(d => d.Name).ToArray();
            _counterDcIndex = Math.Clamp(_counterDcIndex, 0, dcs.Count - 1);
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Data centre", ref _counterDcIndex, dcNames, dcNames.Length))
                _counterWorldIndex = 0;

            var worlds = _worldData.WorldsIn(dcs[_counterDcIndex].Id);
            if (worlds.Count > 0)
            {
                var worldNames = worlds.Select(w => w.Name).ToArray();
                _counterWorldIndex = Math.Clamp(_counterWorldIndex, 0, worlds.Count - 1);
                ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < 150 + ImGui.CalcTextSize("World").X + ImGui.GetStyle().ItemInnerSpacing.X)
                    ImGui.NewLine();
                ImGui.SetNextItemWidth(150);
                ImGui.Combo("World", ref _counterWorldIndex, worldNames, worldNames.Length);
                ImGui.SetNextItemWidth(100);
                ImGui.InputInt("Instance", ref _counterBrowserInstance);
                _counterBrowserInstance = Math.Clamp(_counterBrowserInstance, 0, 9);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("0 is uninstanced. Counts and resets use this world and instance.");

                var chosen = worlds[_counterWorldIndex];
                ImGui.Spacing();
                DrawCounterList(
                    currentZoneOnly: false,
                    worldId: chosen.RowId,
                    instance: (uint)_counterBrowserInstance,
                    worldName: chosen.Name);
                return;
            }
        }

        // No world list available — fall back to wherever the player is.
        ImGui.Spacing();
        DrawCounterList(
            currentZoneOnly: false,
            worldId: _counter.CurrentWorldId(),
            instance: MarkDetector.GetCurrentInstance(),
            worldName: _counter.CurrentWorldName());

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

}
