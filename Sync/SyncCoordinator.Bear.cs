using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed partial class SyncCoordinator
{
    private Dictionary<(uint NameId, uint Instance, uint WorldId), BearMark> _bearMarks = new();
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), DateTime> _bearPluginDeaths = new();
    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), DateTime> BearPluginDeaths => _bearPluginDeaths;
    public bool SupportsBearFeed { get; private set; }
    public BearFeedStatus BearStatus { get; private set; } = new();
    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), BearMark> BearMarks => _bearMarks;

    private void ApplyBearSnapshot(BearSnapshot snapshot)
    {
        if (!_config.SyncReceiveBearFeed || !SupportsBearFeed || !IsConnected) return;
        var now = ServerTimeFor(DateTime.UtcNow);
        BearStatus = snapshot.Status ?? new();
        _bearMarks = BearStatus.Enabled ? BearFeed.Map(snapshot.Marks ?? new(), _worldData.IdOf, now) : new();
        if (!BearStatus.Connected)
            foreach (var mark in _bearMarks.Values)
            {
                mark.Report.ActiveUntil = null;
                mark.Report.HealthExpiresAt = null;
            }
        Bump();
    }

    private void RememberBearPluginDeath((uint NameId, uint Instance, uint WorldId) key, DateTime death)
    {
        var now = ServerTimeFor(DateTime.UtcNow);
        if (death <= DateTime.UnixEpoch || death > now.AddSeconds(10) || now-death > TimeSpan.FromDays(14)) return;
        if (!_bearPluginDeaths.TryGetValue(key, out var previous) || death > previous) _bearPluginDeaths[key] = death;
    }

    private void CaptureBearPluginDeaths()
    {
        if (!_config.SyncReceiveBearFeed) return;
        // Keep source clocks explicit: detector times are local; shared sightings are already server time.
        foreach (var mark in _detector.VisibleMarks)
            if (!mark.IsRemote && mark.HealthPercent == 0)
                RememberBearPluginDeath((mark.NameId,mark.Instance,mark.WorldId),ServerTimeFor(mark.LastSeenUtc));
        foreach (var visible in _visibleMarks.Values)
            if (visible.Mark.HpPercent == 0) RememberBearPluginDeath(visible.Mark.Key,visible.Mark.SeenAt);
        var cutoff = ServerTimeFor(DateTime.UtcNow).AddDays(-14);
        foreach (var key in _bearPluginDeaths.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray())
            _bearPluginDeaths.Remove(key);
    }

    private void ClearBearFeed()
    {
        if (_bearMarks.Count == 0 && _bearPluginDeaths.Count == 0 && !BearStatus.Enabled) return;
        _bearMarks.Clear();
        _bearPluginDeaths.Clear();
        BearStatus = new();
        Bump();
    }

}
