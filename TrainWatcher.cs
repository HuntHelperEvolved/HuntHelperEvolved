using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntTrainRelay;

public class TrackedMark
{
    public string Name = string.Empty;
    public uint ModelId;
    public uint Instance;
    public bool Dead;

    /// <summary>
    /// The moment we personally observed this mark flip to dead while polling.
    /// Falls back to Hunt Helper's LastSeenUTC if the mark was already dead
    /// the first time we saw it (e.g. plugin was (re)loaded mid-train).
    /// </summary>
    public DateTime? DeathObservedAtUtc;
    public DateTime LastSeenUtc;
}

public class TrainWatcher : IDisposable
{
    private readonly IFramework _framework;
    private readonly HuntHelperIpc _ipc;
    private readonly Configuration _config;

    private readonly Dictionary<(uint ModelId, uint Instance), TrackedMark> _tracked = new();
    private double _secondsSinceLastPoll;
    private bool _postedForCurrentSet;

    public event Action<List<TrackedMark>>? TrainCompleted;

    public string LastStatus { get; private set; } = "Idle.";

    /// <summary>
    /// Returns the moment this mark was actually observed flipping to dead during
    /// polling, if the auto-detect loop caught it — null if it was never seen
    /// transition (e.g. auto-post was off, or it's not currently tracked at all).
    /// Used by the manual "End Train Now" fallback to avoid relying on Hunt
    /// Helper's own LastSeenUTC, which doesn't reliably reflect time of death.
    /// </summary>
    public DateTime? GetObservedDeathTime(uint modelId, uint instance) =>
        _tracked.TryGetValue((modelId, instance), out var tracked) ? tracked.DeathObservedAtUtc : null;

    public TrainWatcher(IFramework framework, HuntHelperIpc ipc, Configuration config)
    {
        _framework = framework;
        _ipc = ipc;
        _config = config;
        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!_config.AutoPostEnabled) return;

        _secondsSinceLastPoll += framework.UpdateDelta.TotalSeconds;
        var interval = Math.Max(1, _config.PollIntervalSeconds);
        if (_secondsSinceLastPoll < interval) return;
        _secondsSinceLastPoll = 0;

        Poll();
    }

    /// <summary>
    /// Immediately clears all tracked marks and un-arms posting, so the next mob
    /// Hunt Helper reports is treated as the start of a fresh train.
    /// </summary>
    public void ResetNow()
    {
        ResetInternal();
        LastStatus = "Train tracking reset manually — ready for a new train.";
    }

    private void ResetInternal()
    {
        _tracked.Clear();
        _postedForCurrentSet = false;
    }

    private void Poll()
    {
        var list = _ipc.TryGetTrainList();
        if (list == null)
        {
            LastStatus = "Waiting for Hunt Helper (not loaded, or no version match)...";
            return;
        }

        if (list.Count == 0)
        {
            _tracked.Clear();
            _postedForCurrentSet = false;
            LastStatus = "No active train recorded in Hunt Helper.";
            return;
        }

        var currentKeys = new HashSet<(uint, uint)>();
        var isNewSegment = false;

        foreach (var mob in list)
        {
            var key = (mob.MobID, mob.Instance);
            currentKeys.Add(key);

            if (!_tracked.TryGetValue(key, out var tracked))
            {
                tracked = new TrackedMark
                {
                    Name = mob.Name,
                    ModelId = mob.MobID,
                    Instance = mob.Instance,
                    Dead = mob.Dead,
                    LastSeenUtc = mob.LastSeenUTC,
                    DeathObservedAtUtc = mob.Dead ? mob.LastSeenUTC : null,
                };
                _tracked[key] = tracked;
                isNewSegment = true;
            }
            else
            {
                tracked.LastSeenUtc = mob.LastSeenUTC;
                if (mob.Dead && !tracked.Dead)
                {
                    tracked.Dead = true;
                    tracked.DeathObservedAtUtc = DateTime.UtcNow;
                }
            }
        }

        // Drop marks Hunt Helper no longer reports (e.g. removed via its "clear dead" action).
        foreach (var key in _tracked.Keys.Where(k => !currentKeys.Contains(k)).ToList())
            _tracked.Remove(key);

        // A newly-added mark means a new train segment started; allow posting again.
        if (isNewSegment)
        {
            _postedForCurrentSet = false;
        }

        var deadCount = _tracked.Values.Count(m => m.Dead);
        var allDead = _tracked.Count > 0 && deadCount == _tracked.Count;

        LastStatus = allDead
            ? $"Train cleared — {_tracked.Count} marks."
            : $"Tracking {_tracked.Count} marks, {deadCount} dead.";

        if (allDead && !_postedForCurrentSet)
        {
            _postedForCurrentSet = true;
            TrainCompleted?.Invoke(_tracked.Values.ToList());
        }
    }
}
