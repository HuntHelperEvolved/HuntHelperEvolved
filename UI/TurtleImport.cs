using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private List<DetectedMark>? _turtleMarks;
    private bool _turtleExport;
    private bool _openTurtleDialog;
    private uint _turtleWorld;

    private bool PrepareTurtleImport(string text)
    {
        if (!text.Contains('@')) return false;
        var marks = TurtleExchange.Import(text, DateTime.UtcNow, out var error,
            _detector.GetMarkName, _detector.GetZoneName);
        if (marks is null) { ReportProblem(error); return true; }
        foreach (var mark in marks) mark.MapId = _detector.GetMapId(mark.TerritoryId);
        _turtleMarks = marks;
        _turtleWorld = 0;
        _turtleExport = false;
        _openTurtleDialog = true;
        return true;
    }

    private void ExportToTurtle()
    {
        var marks = _detector.Ordered().Where(m => !m.IsCustom && !m.Dead).ToList();
        if (marks.Count == 0) { ReportProblem("No living train marks to export."); return; }
        if (marks.Any(m => m.WorldId == 0)) { ReportProblem("Assign a world to the train marks before exporting to Turtle."); return; }
        var worlds = marks.Select(m => m.WorldId).Distinct().ToArray();
        if (worlds.Length == 1) { CopyTurtleExport(marks, worlds[0]); return; }
        _turtleMarks = marks;
        _turtleWorld = 0;
        _turtleExport = true;
        _openTurtleDialog = true;
    }

    private void CopyTurtleExport(IEnumerable<DetectedMark> marks, uint world)
    {
        try
        {
            var text = TurtleExchange.Export(marks, world);
            if (text.Length == 0) { ReportProblem("No living marks to export for that world."); return; }
            ImGui.SetClipboardText(text);
            _lastPostResult = $"Copied {_worldData.NameOf(world)} marks for Turtle Scout.";
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not export Turtle Scout marks.");
            ReportProblem(ex is InvalidOperationException ? ex.Message : "Could not copy Turtle Scout marks.");
        }
    }

    private void DrawTurtleDialog()
    {
        const string title = "Turtle Scout";
        if (_openTurtleDialog) { ImGui.OpenPopup(title); _openTurtleDialog = false; }
        var open = true;
        ImGui.SetNextWindowSize(new(390 * ImGuiHelpers.GlobalScale, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(title, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextUnformatted(_turtleExport ? "Which world do you want to export?" : "Which world is this import for?");
            ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("World", _turtleWorld == 0 ? "Choose a world" : _worldData.NameOf(_turtleWorld)))
            {
                foreach (var dc in _worldData.DataCenters)
                {
                    ImGui.TextDisabled(dc.Name);
                    foreach (var world in _worldData.WorldsIn(dc.Id))
                    {
                        if (_turtleExport && _turtleMarks?.Any(m => m.WorldId == world.RowId) != true) continue;
                        if (ImGui.Selectable(world.Name, _turtleWorld == world.RowId)) _turtleWorld = world.RowId;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.BeginDisabled(_turtleWorld == 0 || TrainMutationBusy || _turtleMarks is null);
            if (ImGui.Button(_turtleExport ? "Copy for Turtle" : "Import marks") && _turtleMarks is { } marks)
            {
                if (_turtleExport) CopyTurtleExport(marks, _turtleWorld);
                else
                {
                    foreach (var mark in marks) { mark.WorldId = _turtleWorld; mark.WorldName = _worldData.NameOf(_turtleWorld); }
                    var added = _detector.Merge(marks);
                    _lastPostResult = $"Imported {marks.Count} marks for {_worldData.NameOf(_turtleWorld)} ({added} new).";
                    _chatGui.Print($"[Hunt Helper Evolved] {_lastPostResult}");
                }
                _turtleMarks = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) { _turtleMarks = null; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!open) _turtleMarks = null;
    }
}
