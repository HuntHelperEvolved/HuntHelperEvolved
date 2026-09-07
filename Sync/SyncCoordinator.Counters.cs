using System;
using System.Collections.Generic;
using System.Linq;
namespace HuntHelperEvolved.Sync;

public sealed partial class SyncCoordinator
{
    private string _counterServerId = string.Empty;
    private bool _counterReady;
    private readonly Dictionary<string, SharedCounter> _sharedCounters = new();
    public bool CountersAvailable => _config.SyncEnabled && IsConnected && _counterReady;

    private void WelcomeCounters(WelcomeMessage welcome)
    {
        _counterServerId = welcome.CounterServerId;
        _counterReady = !string.IsNullOrEmpty(_counterServerId);
        _sharedCounters.Clear();
        if (!_counterReady) return; // Old servers remain compatible, with local counts only.
        var scope = _config.SyncServerUrl.Trim() + "|" + _counterServerId;
        if (_config.CounterSyncScope != scope)
        {
            _config.CounterContributions.Clear();
            _config.CounterSyncScope = scope;
        }
        ApplyCounters(welcome.Counters);
        _config.Save();
        SendCounterContributions();
    }
    private void ApplyCounters(List<SharedCounter> rows)
    {
        var changed = false;
        foreach (var row in rows)
        {
            _sharedCounters[row.Key] = row;
            var pending = _config.CounterContributions.FirstOrDefault(c => c.Counter.Key == row.Key);
            if (pending is null) continue;
            changed |= pending.Reconcile(row, _config.CounterContributorId);
        }
        if (changed) _config.Save();
    }
    private void SendCounterContributions()
    {
        if (!CountersAvailable) return;
        foreach (var entry in _config.CounterContributions)
        {
            var acknowledged = _sharedCounters.GetValueOrDefault(entry.Counter.Key)?.Contributions
                .GetValueOrDefault(_config.CounterContributorId) ?? 0;
            if (entry.Count > acknowledged)
                _client.Send(new CounterUpdateMessage { Counter=entry.Counter, Contributor=_config.CounterContributorId, Count=entry.Count });
        }
    }
    public void RecordCounterKill(uint world, uint territory, uint instance, string mob)
    {
        // Once a server has been joined, retain contributions through a temporary
        // disconnect. Settings changes clear the active scope before any new events.
        if (!_config.SyncEnabled || string.IsNullOrEmpty(_counterServerId) || world == 0) return;
        var row = new SharedCounter { WorldId=world, TerritoryId=territory, Instance=instance, Mob=mob };
        var entry = _config.CounterContributions.FirstOrDefault(c => c.Counter.Key == row.Key);
        if (entry is null)
        {
            if (_config.CounterContributions.Count >= 10000) return;
            var known = _sharedCounters.GetValueOrDefault(row.Key);
            row.Epoch = known?.Epoch ?? string.Empty;
            entry = new() { Counter=row, Count=known?.Contributions.GetValueOrDefault(_config.CounterContributorId) ?? 0 };
            _config.CounterContributions.Add(entry);
        }
        entry.Count = Math.Min(1000000, entry.Count + 1);
        _config.Save();
    }
    public long? SharedCounterTotal(uint world, uint territory, uint instance, string mob)
    {
        if (!CountersAvailable) return null;
        return _sharedCounters.GetValueOrDefault($"{world}:{territory}:{instance}:{mob}")?.Total ?? 0;
    }
    public void ResetSharedCounters(CounterDefinition def, uint world, uint instance)
    {
        if (!CountersAvailable) return;
        _client.Send(new CounterResetMessage { WorldId=world, TerritoryId=def.TerritoryId, Instance=instance,
            Epochs=def.MobNames.ToDictionary(m => m, m => _sharedCounters.GetValueOrDefault($"{world}:{def.TerritoryId}:{instance}:{m}")?.Epoch ?? string.Empty) });
    }
}
