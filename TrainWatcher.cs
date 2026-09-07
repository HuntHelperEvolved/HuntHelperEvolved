using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HuntHelperEvolved;

/// <summary>
/// One counted kill as reported by the tally.
///
/// Kept as a flat primitive record even though the tally is now part of this
/// same assembly: it was the payload of the old cross-plugin IPC contract, and
/// it is still exactly what the train needs - an identity, a place and a time -
/// without coupling the watcher to the tally's own richer kill type.
/// </summary>
public readonly record struct HuntTallyKill(
    string Name,
    uint NameId,
    int Rank,
    uint TerritoryId,
    uint InstanceId,
    long UnixSeconds,
    uint WorldId);

/// <summary>Schedules local detection, applies scoped kill evidence and retains native report history.</summary>
public class TrainWatcher : IDisposable
{
    private readonly IFramework _framework;
    private readonly MarkDetector _detector;
    private readonly Configuration _config;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;

    /// <summary>
    /// The channels the battle log arrives on. No player channel is here, and
    /// that is the point: "Someone defeats the Chernobog" typed into party chat
    /// must not be able to tick a mark off the train.
    /// </summary>
    private static readonly XivChatType[] BattleChannels =
    {
        XivChatType.SystemMessage,
        XivChatType.SystemError,
        XivChatType.Damage,
        XivChatType.Action,
    };

    private readonly TrainReportHistory _history = new();

    // Kills are queued rather than applied where they arrive, so a kill and a
    // poll can never interleave halfway through updating train history. Since the
    // merge these come from the tally on the framework thread rather than over
    // IPC on its own one, so the queue no longer has to be concurrent - but it
    // stays that way, because it costs nothing and the ordering guarantee it
    // gives ApplyPendingKills is the point, not the thread safety.
    private readonly ConcurrentQueue<HuntTallyKill> _pendingKills = new();

    // Cheap insurance against a death being reported twice.
    private readonly HashSet<(uint, uint, uint, uint, long)> _seenKills = new();

    private double _secondsSinceLastPoll;
    private double _secondsSinceSave;
    private double _secondsSinceScan;
    private double _secondsSinceRecordScan;

    /// <summary>
    /// Raised periodically so the in-progress train can be written to disk.
    /// Fires regardless of whether tracking is on, since kill times and the
    /// pointer are worth keeping either way.
    /// </summary>
    public event Action? PersistRequested;

    public string LastStatus { get; private set; } = "Idle.";

    /// <summary>A defensive snapshot of native report history, including removed dead rows.</summary>
    public Dictionary<(uint ModelId, uint Instance, uint WorldId), TrackedMark> GetTrackedSnapshot()
    {
        CaptureHistory();
        return _history.Snapshot();
    }

    /// <summary>Number of marks auto-marked dead by Hunt Tally this train.</summary>
    public int AutoMarkedCount { get; private set; }

    public TrainWatcher(
        IFramework framework, MarkDetector detector, Configuration config,
        IChatGui chatGui, IPluginLog log)
    {
        _framework = framework;
        _detector = detector;
        _config = config;
        _chatGui = chatGui;
        _log = log;

        _framework.Update += OnUpdate;
        _chatGui.ChatMessage += OnChatMessage;
        _detector.MarkObservedDead += OnMarkObservedDead;
        _detector.Removing += CaptureHistory;
        _detector.Cleared += ClearHistory;
        _history.Restore(config.ReportHistory);
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        _chatGui.ChatMessage -= OnChatMessage;
        _detector.MarkObservedDead -= OnMarkObservedDead;
        _detector.Removing -= CaptureHistory;
        _detector.Cleared -= ClearHistory;
    }

    /// <summary>
    /// The detector saw a mark at zero health. It has already flagged its own
    /// row; this keeps native report history in step and clears the dot.
    /// </summary>
    private void OnMarkObservedDead(DetectedMark mark)
    {
        try
        {
            ObservedDeathCount++;
            _detector.RemoveSighting(mark.NameId, mark.Instance, mark.WorldId);

            CaptureHistory();

            _log.Information($"{mark.Name} was seen at zero health; marked dead in the train.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not record an observed mark death.");
        }
    }

    /// <summary>How many marks were ticked off by watching them die rather than killing them.</summary>
    public int ObservedDeathCount { get; private set; }

    /// <summary>
    /// Ticks a mark dead when the battle log says it died, whoever landed the
    /// kill.
    ///
    /// The tally can only report kills you were credited with, so a mark the
    /// group brought down while you were still running in stayed lit as though
    /// it were up. The battle log does not care who tagged it.
    ///
    /// The mark's name is matched at the END of the line rather than anywhere
    /// in it. "defeats the Ker" is a prefix of "defeats the Ker Shroud", and
    /// an unanchored match would tick the mark off when its minion died.
    /// </summary>
    private void OnChatMessage(IChatMessage message)
    {
        try
        {
            if (!_config.MarkDeadOnObservedDefeat) return;
            if (Array.IndexOf(BattleChannels, message.LogKind) < 0) return;

            var text = message.Message.TextValue;
            if (text.IndexOf("defeat", StringComparison.OrdinalIgnoreCase) < 0) return;

            var instance = MarkDetector.GetCurrentInstance();
            var worldId = _detector.CurrentWorldId();
            var now = DateTime.UtcNow;

            foreach (var mark in _detector.Marks.Values)
            {
                if (mark.Dead || mark.IsCustom) continue;
                if (mark.Instance != instance || mark.WorldId != worldId) continue;
                if (string.IsNullOrWhiteSpace(mark.Name)) continue;
                if (!DefeatedInLine(text, mark.Name)) continue;

                mark.Dead = true;
                mark.DeathObservedAtUtc = now;
                ObservedDeathCount++;

                // Its dot goes out now rather than waiting for the sighting to
                // expire, the same as a kill the tally reported.
                _detector.RemoveSighting(mark.NameId, mark.Instance, mark.WorldId);

                _log.Information($"{mark.Name} was defeated; marked dead in the train.");
            }

            CaptureHistory();
        }
        catch (Exception ex)
        {
            // Never throw back into the chat pipeline.
            _log.Warning(ex, "Could not read a battle log line while watching for mark deaths.");
        }
    }

    private static bool DefeatedInLine(string text, string markName) =>
        Regex.IsMatch(
            text,
            $@"defeats?\s+the\s+{Regex.Escape(markName)}\s*[.!]?\s*$",
            RegexOptions.IgnoreCase);

    /// <summary>
    /// Takes a kill from the tally. Public because the tally is wired to this
    /// from Plugin now; it used to arrive on this class's own IPC subscription.
    ///
    /// Only queued while actively tracking — otherwise the queue would grow
    /// unbounded across a long session of ordinary hunting.
    /// </summary>
    public void OnHuntTallyKill(HuntTallyKill kill)
    {
        if (!_config.TrackingEnabled) return;
        if (!_config.AutoMarkDeadEnabled) return;
        if (kill.WorldId != 0) _pendingKills.Enqueue(kill);
    }

    private void OnUpdate(IFramework framework)
    {
        _detector.RefreshNearbyPlayers();
        // Periodic save so a crash mid-train doesn't lose kill times. Ten
        // seconds keeps writes cheap while bounding the worst case loss.
        //
        // The tally connection used to be retried here as well, because plugin
        // load order made it possible to start before it existed. It ships in
        // this assembly now, so there is nothing left to wait for.
        _secondsSinceSave += framework.UpdateDelta.TotalSeconds;
        if (_secondsSinceSave >= 10)
        {
            _secondsSinceSave = 0;
            PersistRequested?.Invoke();
        }

        // Detection runs regardless of tracking: the map wants to know about
        // B, A and S ranks all the time. Only whether A-ranks get RECORDED into
        // the train is gated by tracking and the pause button.
        _secondsSinceScan += framework.UpdateDelta.TotalSeconds;
        _secondsSinceRecordScan += framework.UpdateDelta.TotalSeconds;
        if (_secondsSinceScan >= 0.5)
        {
            _secondsSinceScan = 0;
            try
            {
                var recordNow = _secondsSinceRecordScan >= Math.Max(1, _config.PollIntervalSeconds);
                if (recordNow) _secondsSinceRecordScan = 0;
                _detector.Scan(recordNew: recordNow && _config.TrackingEnabled && !_config.ScanningPaused);
            }
            catch (Exception ex)
            {
                LastStatus = $"Detection error: {ex.Message}";
            }
        }

        if (!_config.TrackingEnabled) return;

        _secondsSinceLastPoll += framework.UpdateDelta.TotalSeconds;
        var interval = Math.Max(1, _config.PollIntervalSeconds);
        if (_secondsSinceLastPoll < interval) return;
        _secondsSinceLastPoll = 0;

        Poll();
    }

    /// <summary>
    /// Immediately clears all tracked marks, so the next mob Hunt Helper reports
    /// is treated as the start of a fresh train. Called manually, or automatically
    /// right after "End Train Now" posts a report.
    /// </summary>
    public void ResetNow()
    {
        _history.Clear();
        _detector.Clear();
        _seenKills.Clear();
        AutoMarkedCount = 0;
        while (_pendingKills.TryDequeue(out _)) { }
        LastStatus = "Train tracking reset — ready for a new train.";
    }

    private void ClearHistory()
    {
        _history.Clear();
        _config.ReportHistory.Clear();
    }

    public void RestoreHistory(IEnumerable<TrackedMark> history) => _history.Restore(history);

    private void CaptureHistory() => _history.Update(_detector.Ordered().Where(mark => !mark.IsCustom)
        .Select(mark => new TrackedMark { Name = mark.Name, ModelId = mark.NameId,
            Instance = mark.Instance, WorldId = mark.WorldId, WorldName = mark.WorldName,
            TerritoryId = mark.TerritoryId, Dead = mark.Dead, LastSeenUtc = mark.LastSeenUtc,
            DeathObservedAtUtc = mark.DeathObservedAtUtc, SnipedAtUtc = mark.SnipedAtUtc }));

    private void Poll()
    {
        ApplyPendingKills();
        CaptureHistory();
        LastStatus = $"Train: {OwnSummary()}";
    }

    /// <summary>Summarizes the active native train.</summary>
    private string OwnSummary()
    {
        var total = _detector.Marks.Count;
        var dead = _detector.Marks.Values.Count(m => m.Dead);
        var autoPart = AutoMarkedCount > 0 ? $", {AutoMarkedCount} auto" : string.Empty;
        return $"{total} marks, {dead} dead{autoPart}";
    }

    private void ApplyPendingKills()
    {
        while (_pendingKills.TryDequeue(out var kill))
        {
            var dedupeKey = (kill.NameId, kill.TerritoryId, kill.InstanceId, kill.WorldId, kill.UnixSeconds);
            if (!_seenKills.Add(dedupeKey)) continue;

            var killTime = DateTimeOffset.FromUnixTimeSeconds(kill.UnixSeconds).UtcDateTime;
            var matched = false;

            // Use the world captured by the tally, even when delivery follows travel.
            if (_detector.Marks.TryGetValue((kill.NameId, kill.InstanceId, kill.WorldId), out var own) && !own.Dead)
            {
                own.Dead = true;
                own.DeathObservedAtUtc = killTime;
                matched = true;
            }

            if (matched)
            {
                AutoMarkedCount++;

                // Its dot should go back to grey immediately rather than
                // waiting for the proximity check to notice it's gone.
                // The kill happened where the player is.
                _detector.RemoveSighting(
                    kill.NameId, kill.InstanceId, kill.WorldId);
            }
        }
    }
}
