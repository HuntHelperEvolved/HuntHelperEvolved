using System;
using Dalamud.Bindings.ImGui;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private void OnTrainCommand(string command, string args)
    {
        var argument = args.Trim();
        if (argument.Length == 0)
            _trainPopoutVisible = !_trainPopoutVisible;
        else if (argument.Equals("on", StringComparison.OrdinalIgnoreCase))
            SetFollowTrain(true);
        else if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
            SetFollowTrain(false);
        else
            _chatGui.Print($"[Hunt Helper Evolved] Usage: {command} [on|off]. Without an option, toggles the train popout.");
    }

    private void SetFollowTrain(bool enabled)
    {
        // Do not let a rally completion queued before opting out advance the
        // train later. New, explicit row actions remain available while off.
        if (!enabled) _pendingCustomRemovals.Clear();
        if (_config.FollowTrain != enabled)
        {
            _config.FollowTrain = enabled;
            _config.Save();
        }
        _chatGui.Print(enabled
            ? "[Hunt Helper Evolved] Following train: on."
            : "[Hunt Helper Evolved] Following train: off. Automatic train flags and chat echoes paused.");
    }

    private void DrawTrainFollowButton()
    {
        if (ImGui.Button(_config.FollowTrain ? "Follow train: On##trainFollow" : "Follow train: Off##trainFollow"))
            SetFollowTrain(!_config.FollowTrain);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip((_config.FollowTrain
                ? "Stop following the train on this client (/hht off). Pauses automatic next-mark flags and chat echoes."
                : "Resume following the train on this client (/hht on). Uses your existing auto-advance and echo settings.")
                + "\nManual flags and Next Mark remain available. Sharing and scouting are unchanged.");
    }

    /// <summary>The mark the pointer is on, or null if it's been cleared/removed.</summary>
    private DetectedMark? CurrentMark()
    {
        if (_currentMark is not { } key) return null;
        return _detector.Marks.TryGetValue(key, out var mark) ? mark : null;
    }

    /// <summary>
    /// The next live (not dead) mark after the current one, in list order.
    /// Falls back to the first live mark when there's no pointer yet.
    /// </summary>
    private DetectedMark? NextLiveMark() => TrainNavigation.Next(_detector.Marks.Values, CurrentMark());

    /// <summary>
    /// Moves the pointer to a mark, optionally announcing it. Announcing echoes
    /// to chat and drops the map flag, which is what makes hands-free
    /// conducting work: kill a mark, the next one is already flagged.
    /// </summary>
    private void SetCurrentMark(DetectedMark? mark, bool announce)
    {
        if (mark == null)
        {
            _currentMark = null;
            return;
        }

        _currentMark = (mark.NameId, mark.Instance, mark.WorldId);

        if (!announce) return;

        var ordered = _detector.Ordered();
        var index = ordered.FindIndex(m => m.Key == mark.Key);
        TrainChatEcho.Send(_chatGui, _gameGui, mark, index < 0 ? 0 : index, ordered.Count);
    }

    /// <summary>
    /// Announces the next mark after the current mark dies. Finding a starting
    /// point after scouting, removal or reset is silent, even while following.
    /// </summary>
    private void UpdateAutoAdvance()
    {
        if (!_config.FollowTrain || !_config.AutoAdvance) return;

        var current = CurrentMark();
        if (current == null)
        {
            SetCurrentMark(NextLiveMark(), announce: false);
            return;
        }
        if (!current.Dead) return;

        // Consume this death even at the end of the route. A mark scouted later
        // must not be announced as a delayed advance from an earlier kill.
        SetCurrentMark(NextLiveMark(), announce: _config.EchoOnAdvance && current.SnipedAtUtc is null);
    }

    /// <summary>
    /// Move to the next live mark and flag it.
    ///
    /// Marks are marked dead by watching the
    /// kill happen, and those timings are what the train report is built from —
    /// a mistyped /hhn should not be able to write a kill that never occurred.
    /// </summary>
    private void OnNextMarkCommand(string command, string args)
    {
        var next = NextLiveMark();
        if (next == null)
        {
            _chatGui.Print("[Hunt Helper Evolved] No live marks left in the train.");
            return;
        }

        // Announcing is what echoes it to chat and drops the flag on it.
        SetCurrentMark(next, announce: true);
    }

}
