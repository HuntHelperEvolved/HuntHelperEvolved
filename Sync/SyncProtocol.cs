using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// The wire shapes shared with the sync server, kept in step with its
/// Protocol/Messages.cs and PROTOCOL.md. One JSON object per WebSocket text
/// frame, camelCase, times in UTC. Anything here that changes meaning bumps
/// the version; the server refuses a mismatch with a clear message.
/// </summary>
public static class SyncProtocol
{
    public const int Version = 4;

    public static readonly JsonSerializerSettings Json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
    };

    public static string Serialize(object message) => JsonConvert.SerializeObject(message, Json);

    public static T? Deserialize<T>(JObject payload) where T : class =>
        payload.ToObject<T>(JsonSerializer.Create(Json));
}

public sealed class SyncKey
{
    public uint NameId { get; set; }
    public uint Instance { get; set; }
    public uint WorldId { get; set; }

    public (uint NameId, uint Instance, uint WorldId) ToTuple() => (NameId, Instance, WorldId);

    public static SyncKey From((uint NameId, uint Instance, uint WorldId) key) =>
        new() { NameId = key.NameId, Instance = key.Instance, WorldId = key.WorldId };
}

/// <summary>A train row on the wire. Order travels separately, as a list of keys.</summary>
public sealed class SyncMark
{
    public uint NameId { get; set; }
    public uint Instance { get; set; }
    public uint WorldId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint TerritoryId { get; set; }
    public uint MapId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public bool Dead { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime? DeathAt { get; set; }
    public DateTime? SnipedAt { get; set; }
    public bool IsCustom { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public bool Spiced { get; set; }
    public long Revision { get; set; }
    public long BaseRevision { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    [JsonIgnore]
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Instance, WorldId);
}

public sealed class SyncSighting
{
    public uint NameId { get; set; }
    public uint Instance { get; set; }
    public uint WorldId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Rank { get; set; } = "A";
    public uint TerritoryId { get; set; }
    public uint MapId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int? NearbyPlayers { get; set; }
    public bool? InCombat { get; set; }
    public float HpPercent { get; set; } = 100f;
    public DateTime SeenAt { get; set; }
    public string Reporter { get; set; } = string.Empty;
    public int? SpawnPointIndex { get; set; }

    [JsonIgnore]
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Instance, WorldId);
}

public sealed class SyncSRankKill
{
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public uint TerritoryId { get; set; }
    public DateTime KilledAt { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public int? SpawnPointIndex { get; set; }
    public string Source { get; set; } = "observed";
    public string Reporter { get; set; } = string.Empty;
}

public sealed class SyncSRankStatus
{
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public uint TerritoryId { get; set; }
    public DateTime? KilledAt { get; set; }
    public string? KillSource { get; set; }
    public string? KillReporter { get; set; }
    public bool Maintenance { get; set; }
    public DateTime? LastSeenUpAt { get; set; }
    public float? LastSeenHp { get; set; }
    public DateTime? SpawnedAt { get; set; }

    /// <summary>Freshness of a positive Faloop active report; independent of live scout visibility.</summary>
    public DateTime? FaloopActiveAt { get; set; }
    public DateTime? FaloopActiveUntil { get; set; }
    public float? SpawnX { get; set; }
    public float? SpawnY { get; set; }

    /// <summary>
    /// The kill is a bound, not an observation: Faloop recorded the mark as
    /// sniped, so it died unreported some time after KilledAt. No honest
    /// percentage exists for such a window.
    /// </summary>
    public bool Uncertain { get; set; }
    public DateTime? KilledAtLatest { get; set; }

    /// <summary>What Faloop holds, kept even when a member's own report won, so the board can show the disagreement.</summary>
    public DateTime? FaloopKilledAt { get; set; }

    [JsonIgnore]
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Instance, WorldId);
}

public sealed class SyncEliminatedPoint
{
    public int Index { get; set; }
    public string Rank { get; set; } = "A";
    public uint NameId { get; set; }
    public DateTime SeenAt { get; set; }
}

public sealed class SyncSpawnZone
{
    public uint TerritoryId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public DateTime? SinceAt { get; set; }
    public int? LastSDeathIndex { get; set; }
    public int? SCurrentIndex { get; set; }
    public List<SyncEliminatedPoint> Eliminated { get; set; } = new();

    [JsonIgnore]
    public (uint TerritoryId, uint WorldId, uint Instance) Key => (TerritoryId, WorldId, Instance);

    /// <summary>Whether a spawn point is ruled out for the S in this cycle.</summary>
    public bool IsRuledOut(int index)
    {
        if (LastSDeathIndex == index) return true;
        foreach (var e in Eliminated)
            if (e.Index == index) return true;
        return false;
    }

    public SyncEliminatedPoint? EliminatedBy(int index)
    {
        foreach (var e in Eliminated)
            if (e.Index == index) return e;
        return null;
    }
}

public sealed class SyncPresence
{
    public string ClientId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public uint TerritoryId { get; set; }
    public uint Instance { get; set; }
    public DateTime ConnectedAt { get; set; }
}

public sealed class SyncWorld
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class SyncFaloopStatus
{
    public DateTime? MetadataAt { get; set; }
    public List<string> OfflineWorlds { get; set; } = new();
    public Dictionary<uint, int> ZoneInstances { get; set; } = new();
    public bool IsOffline(string world) => MetadataAt is not null && OfflineWorlds.Contains(world.ToLowerInvariant().Replace(' ', '_'));
    public List<uint> CurrentInstances(uint territory, IEnumerable<uint> observed)
    {
        if (MetadataAt is not null && ZoneInstances.TryGetValue(territory, out var count) && count >= 1 && count <= 9)
            return count == 1 ? new() { 0 } : Enumerable.Range(1, count).Select(i => (uint)i).ToList();
        return observed.Distinct().OrderBy(i => i).ToList();
    }

    public DateTime? LastLiveMessageAt { get; set; }
    public DateTime? LastAlertAt { get; set; }
    public bool LiveConnected { get; set; }
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public string Status { get; set; } = "Off.";
    public DateTime? LastSyncAt { get; set; }
    public List<string> DataCenters { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Client -> server. Each carries its own "type" so serialising is one call.
// ---------------------------------------------------------------------------

public sealed class HelloMessage
{
    public string Type => "hello";
    public int Protocol => SyncProtocol.Version;
    public string Password { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public uint TerritoryId { get; set; }
    public uint Instance { get; set; }
    public List<SyncWorld> Worlds { get; set; } = new();
}

public sealed class TrainUpsertMessage { public string Type => "train.upsert"; public List<SyncMark> Marks { get; set; } = new(); }
public sealed class TrainRemoveMessage { public string Type => "train.remove"; public List<SyncKey> Keys { get; set; } = new(); }
public sealed class TrainClearMessage { public string Type => "train.clear"; }
public sealed class TrainOrderMessage { public string Type => "train.order"; public List<SyncKey> Keys { get; set; } = new(); }
public sealed class SightingsMessage { public string Type => "sightings"; public List<SyncSighting> Sightings { get; set; } = new(); }
public sealed class SightingsRemoveMessage { public string Type => "sightings.remove"; public List<SyncKey> Keys { get; set; } = new(); }
public sealed class SRankKillMessage : ISyncTyped
{
    public string Type => "srank.kill";
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public uint TerritoryId { get; set; }
    public DateTime KilledAt { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public int? SpawnPointIndex { get; set; }
    public string Source { get; set; } = "observed";
}
public sealed class SRankMaintenanceMessage { public string Type => "srank.maintenance"; public uint WorldId { get; set; } public DateTime EndedAt { get; set; } }
public sealed class SRankClearMessage { public string Type => "srank.clear"; public uint NameId { get; set; } public uint WorldId { get; set; } public uint Instance { get; set; } }
public sealed class SpawnResetMessage { public string Type => "spawn.reset"; public uint TerritoryId { get; set; } public uint WorldId { get; set; } public uint Instance { get; set; } }
public sealed class PresenceUpdateMessage { public string Type => "presence.update"; public uint WorldId { get; set; } public uint TerritoryId { get; set; } public uint Instance { get; set; } }
public sealed class PingMessage { public string Type => "ping"; }

public interface ISyncTyped { string Type { get; } }

// ---------------------------------------------------------------------------
// Server -> client
// ---------------------------------------------------------------------------

public static class ServerMessageTypes
{
    public const string Welcome = "welcome";
    public const string Error = "error";
    public const string TrainUpsert = "train.upsert";
    public const string TrainRemove = "train.remove";
    public const string TrainClear = "train.clear";
    public const string TrainOrder = "train.order";
    public const string Sightings = "sightings";
    public const string SightingsExpired = "sightings.expired";
    public const string SRankUpdate = "srank.update";
    public const string SpawnUpdate = "spawn.update";
    public const string Presence = "presence";
    public const string Pong = "pong";
}

public sealed class WelcomeMessage
{
    public bool SupportsTrainFinish { get; set; }
    public List<string> TrainScouts { get; set; } = new();
    public List<ARankKill> ARankKills { get; set; } = new();
    public bool SupportsVisibleMarks { get; set; }
    public List<VisibleMark> VisibleMarks { get; set; } = new();
    public string CounterServerId { get; set; } = string.Empty;
    public List<SharedCounter> Counters { get; set; } = new();
    public WatchesBroadcast WatchState { get; set; } = new();
    public int Protocol { get; set; }
    public string ServerVersion { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public DateTime ServerTime { get; set; }
    public List<SyncMark> Marks { get; set; } = new();
    public List<SyncKey> Order { get; set; } = new();
    public List<SyncSighting> Sightings { get; set; } = new();
    public List<SyncSRankStatus> SRanks { get; set; } = new();
    public List<SyncSpawnZone> SpawnZones { get; set; } = new();
    public List<SyncPresence> Clients { get; set; } = new();
    public SyncFaloopStatus Faloop { get; set; } = new();
}

public sealed class ErrorMessage { public string Code { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; }
public sealed class TrainUpsertBroadcast { public List<SyncMark> Marks { get; set; } = new(); public string By { get; set; } = string.Empty; }
public sealed class TrainRemoveBroadcast { public List<SyncKey> Keys { get; set; } = new(); public string By { get; set; } = string.Empty; }
public sealed class TrainClearBroadcast { public string By { get; set; } = string.Empty; }
public sealed class TrainOrderBroadcast { public List<SyncKey> Keys { get; set; } = new(); public string By { get; set; } = string.Empty; }
public sealed class SightingsBroadcast { public List<SyncSighting> Sightings { get; set; } = new(); }
public sealed class SightingsExpiredBroadcast { public List<SyncKey> Keys { get; set; } = new(); }
public sealed class SRankUpdateBroadcast { public List<SyncSRankStatus> Entries { get; set; } = new(); }
public sealed class SpawnUpdateBroadcast { public List<SyncSpawnZone> Zones { get; set; } = new(); }
public sealed class PresenceBroadcast { public List<SyncPresence> Clients { get; set; } = new(); }
public sealed class PongMessage { public DateTime ServerTime { get; set; } }

public sealed class SRankSpawnBroadcast
{
    public string Event { get; set; } = "spawn";
    public uint NameId { get; set; }
    public uint WorldId { get; set; }
    public uint Instance { get; set; }
    public DateTime SpawnedAt { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public string DataCenter { get; set; } = "";
    public string Source { get; set; } = "Faloop";
}

public sealed class SyncWatch
{
    public string Label { get; set; } = "";
    public int SpawnStatus { get; set; }
    public uint TerritoryId { get; set; }
    public bool HasLocation { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class WatchesBroadcast
{
    public long Revision { get; set; }
    public List<SyncWatch> Watches { get; set; } = new();
    public bool Accepted { get; set; } = true;
}
public sealed class WatchesMessage
{
    public string Type => "train.watches";
    public long BaseRevision { get; set; }
    public List<SyncWatch> Watches { get; set; } = new();
}

public sealed class VisibleMark
{
    public SyncSighting Mark { get; set; } = new();
    public List<string> ObserverIds { get; set; } = new();
    public List<string> Observers { get; set; } = new();
}
public sealed class VisibleMarksBroadcast
{
    public List<VisibleMark> Marks { get; set; } = new();
    public List<SyncKey> Removed { get; set; } = new();
}

public sealed class TrainFinishMessage
{
    public string Type => "train.finish";
    public string RequestId { get; set; } = string.Empty;
    public bool ClearShared { get; set; }
    public long WatchRevision { get; set; }
    public List<SyncMark> ExpectedMarks { get; set; } = new();
    public List<SyncMark> History { get; set; } = new();
}
public sealed class TrainFinishResult
{

    public string RequestId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
}
public sealed class TrainScoutsMessage
{
    public string Type => "train.scouts";
    public List<string> Names { get; set; } = new();
}
public sealed class TrainScoutsBroadcast
{

    public List<string> Names { get; set; } = new();
}
