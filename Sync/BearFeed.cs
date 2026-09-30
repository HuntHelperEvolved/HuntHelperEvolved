using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

public sealed class BearFeedStatus
{
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public string Status { get; set; } = "Waiting for the server.";
    public List<string> DataCenters { get; set; } = new();
    public DateTime? LastMessageAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
}

public sealed class BearReport
{
    public string HuntName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public uint Instance { get; set; }
    public DateTime? SeenAt { get; set; }
    public DateTime? ActiveUntil { get; set; }
    public DateTime? ActiveConfirmedAt { get; set; }
    public float? HpPercent { get; set; }
    public DateTime? HealthObservedAt { get; set; }
    public DateTime? HealthReceivedAt { get; set; }
    public DateTime? HealthExpiresAt { get; set; }
    public DateTime? KilledAt { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public bool Maintenance { get; set; }
    public bool Uncertain { get; set; }
}

public sealed class BearSnapshot
{
    public DateTime ServerTime { get; set; }
    public BearFeedStatus Status { get; set; } = new();
    public List<BearReport> Marks { get; set; } = new();
}

/// <summary>A display-only report. Never added to train, detector, persisted history or outgoing sync.</summary>
public sealed record BearMark(uint NameId, uint WorldId, uint TerritoryId, string Name, string Rank, BearReport Report)
{
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Report.Instance, WorldId);
    public bool IsActive(DateTime now) => Report.SeenAt is { } seen && seen > DateTime.UnixEpoch
        && seen <= now.AddSeconds(10) && Report.ActiveUntil is { } expiry && expiry > now
        && (Report.ActiveConfirmedAt is null || Report.ActiveConfirmedAt <= now.AddSeconds(10))
        && expiry <= (Report.ActiveConfirmedAt ?? seen).AddMinutes(5)
        && (Report.KilledAt is null || seen > Report.KilledAt);
    public bool HealthFresh(DateTime now) => IsActive(now) && Report.HpPercent is { } hp
        && float.IsFinite(hp) && hp is >= 0 and <= 100 && Report.HealthObservedAt is { } observed
        && observed > DateTime.UnixEpoch && observed <= now.AddSeconds(10)
        && (Report.HealthReceivedAt is null || Report.HealthReceivedAt <= now.AddSeconds(10))
        && Report.HealthExpiresAt is { } expires && now < expires
        && expires <= (Report.HealthReceivedAt ?? observed).AddSeconds(15)
        && (Report.KilledAt is null || observed > Report.KilledAt);
    public bool RecentDeath(DateTime now) => !Report.Maintenance && !Report.Uncertain
        && Report.KilledAt is { } killed && killed <= now && now - killed < TimeSpan.FromSeconds(30)
        && (Report.SeenAt is null || killed >= Report.SeenAt);
    public bool HasDeath(DateTime now) => Report.KilledAt is { } killed && killed > DateTime.UnixEpoch
        && killed <= now.AddSeconds(10) && now - killed <= TimeSpan.FromDays(14);

    public SyncSighting Sighting(DateTime now, bool dead = false) => new()
    {
        NameId = NameId, WorldId = WorldId, Instance = Report.Instance, TerritoryId = TerritoryId,
        Name = Name, Rank = Rank, SeenAt = Report.SeenAt ?? default,
        X = Report.X ?? float.NaN, Y = Report.Y ?? float.NaN,
        HpPercent = dead ? 0 : HealthFresh(now) ? Report.HpPercent!.Value : 100,
        // Bear does not supply combat state or player counts. Damaged is not necessarily pulled.
        InCombat = null
    };
}

public static class BearFeed
{
    private static readonly Dictionary<string, (uint Id, uint Territory, string Name, string Rank)> Identities = BuildIdentities();
    private static Dictionary<string, (uint, uint, string, string)> BuildIdentities()
    {
        var result = new Dictionary<string, (uint, uint, string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var timer in SRankTimerData.All)
            result.TryAdd(timer.Name, (timer.NameId, timer.TerritoryId, timer.Name, "S"));
        foreach (var entry in ExpansionData.ModelIdToMark)
            if (ARankZoneInstances.ZoneTerritories.TryGetValue(entry.Value.Location, out var territories) && territories.Length > 0)
                result.TryAdd(entry.Value.Name, (entry.Key, territories[0], entry.Value.Name, "A"));
        return result;
    }

    public static Dictionary<(uint NameId, uint Instance, uint WorldId), BearMark> Map(
        IEnumerable<BearReport> reports, Func<string, uint> worldId, DateTime now)
    {
        var result = new Dictionary<(uint, uint, uint), BearMark>();
        foreach (var report in reports.Take(10000))
        {
            if (report is null || report.Instance > 9 || !Identities.TryGetValue((report.HuntName ?? string.Empty).Trim(), out var identity)) continue;
            var world = worldId((report.WorldName ?? string.Empty).Trim());
            if (world == 0) continue;
            var mark = new BearMark(identity.Id, world, identity.Territory, identity.Name, identity.Rank, report);
            if (!mark.IsActive(now) && !mark.HasDeath(now)) continue;
            // The server sends one current report per identity. Last entry wins if an older server duplicates it.
            result[mark.Key] = mark;
        }
        return result;
    }

}
