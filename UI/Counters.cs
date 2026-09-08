using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private void DrawCountersTab()
    {
        if (ImGui.BeginChild("S-rank counters", new Vector2(0, 0), false))
            DrawCountersContents();
        ImGui.EndChild();
    }

    private void DrawCountersContents()
    {
        if (ImGui.Button("Open Counter Popout"))
        {
            _counterPopoutVisible = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset All Local Counts"))
        {
            _counter.Reset();
        }
        ImGui.TextDisabled("S-rank spawn progress. Popout: /hhc or /htrc.");

        ImGui.Spacing();
        DrawSpawnWatches();

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
                ImGui.SetNextItemWidth(150);
                ImGui.Combo("World", ref _counterWorldIndex, worldNames, worldNames.Length);

                var chosen = worlds[_counterWorldIndex];
                ImGui.Spacing();
                DrawCounterList(
                    currentZoneOnly: false,
                    worldId: chosen.RowId,
                    instance: 0,
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
