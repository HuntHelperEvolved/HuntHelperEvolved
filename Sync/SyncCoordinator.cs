using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// Joins this client's hunt to the group's.
///
/// Local state stays where it is — the train in MarkDetector, sightings in
/// its store — and this class sits beside it, diffing what changed since
/// it last looked and sending that, and applying what the server says
/// happened elsewhere. The rest of the plugin never has to know a mark
/// came from someone else; it just appears, the way an imported one does.
///
/// Everything runs on the framework thread. The socket parks frames in a
/// queue and this drains it once per tick, so nothing here needs a lock.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private static readonly TimeSpan DiffInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HelloRefresh = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SightingHeartbeat = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SentSightingMemory = TimeSpan.FromSeconds(60);

    /// <summary>Matches the server's own expiry, so both forget at the same moment.</summary>
    public static readonly TimeSpan RemoteSightingTtl = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan KillDedupe = TimeSpan.FromMinutes(5);

    private readonly record struct KnownMark(int Signature, long Revision, bool Dead);

    /// <summary>
    /// The SS-event marks and their minions. S rank in the game's data, but
    /// they come from an event rather than a clock and never stand on a
    /// spawn point, so they are relayed as "SS" and kept out of the S-rank
    /// logic on the server. Same pairs as SsEventWatcher's table.
    /// </summary>
    private static readonly HashSet<uint> SsEventMobs = new() { 8915, 8916, 10615, 10616, 13406, 13407 };

    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly MarkDetector _detector;
    private readonly WorldData _worldData;
    private readonly string _pluginVersion;
    private readonly SyncClient _client;

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), KnownMark> _known = new();
    private List<(uint NameId, uint Instance, uint WorldId)> _lastSentOrder = new();

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), OtherRankSighting> _remote = new();
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), SyncSRankStatus> _sranks = new();
    private readonly Dictionary<(uint TerritoryId, uint WorldId, uint Instance), SyncSpawnZone> _zones = new();
    private List<SyncPresence> _clients = new();
    private SyncFaloopStatus _faloop = new();

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), (float Hp, Vector2 Pos, DateTime At)> _sentSightings = new();
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), DateTime> _reportedKills = new();

    private List<SyncWorld>? _worlds;
    private HelloMessage _hello = new();
    private string _appliedSettings = string.Empty;
    private bool _applying;
    private List<SyncMark> _localTrainBackup = new();
    public int LocalBackupCount => _localTrainBackup.Count;
    public void UploadLocalBackup()
    {
        if (!IsConnected || !_config.SyncShareTrain || _localTrainBackup.Count == 0) return;
        _client.Send(new TrainUpsertMessage { Marks = _localTrainBackup });
        _localTrainBackup = new();
        _config.SyncLocalTrainBackup = _localTrainBackup;
        _config.Save();
    }

    private double _sinceDiff, _sincePing, _sinceHello, _sincePresence;
    private (uint World, uint Territory, uint Instance) _lastPresence;

    public SyncClient Client => _client;
    public bool IsConnected => _client.IsConnected;
    public string ServerVersion { get; private set; } = string.Empty;
    public string ServerName { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    /// <summary>
    /// Bumped whenever anything remote changes, so the map overlay can fold
    /// it into its refresh signature without walking every collection.
    /// </summary>
    public int RemoteVersion { get; private set; }

    public IReadOnlyList<SyncPresence> Clients => _clients;

    /// <summary>
    /// Marks other members can see, in the same shape as local sightings so
    /// the map can draw them the same way. Reporter says who.
    /// </summary>
    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), OtherRankSighting> RemoteSightings => _remote;

    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), SyncSRankStatus> SRankStatuses => _sranks;
    public IReadOnlyDictionary<(uint TerritoryId, uint WorldId, uint Instance), SyncSpawnZone> SpawnZones => _zones;
    public SyncFaloopStatus Faloop => _faloop;

    /// <summary>
    /// Somebody else emptied the shared train. Raised while remote changes
    /// are being applied, so a handler that clears local state does not
    /// echo the clear straight back to the server.
    /// </summary>
    public event Action<string>? RemoteTrainCleared;

    public SyncCoordinator(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IPluginLog log,
        Configuration config,
        MarkDetector detector,
        WorldData worldData,
        string pluginVersion)
    {
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _log = log;
        _config = config;
        _localTrainBackup = config.SyncLocalTrainBackup ?? new();
        _detector = detector;
        _worldData = worldData;
        _pluginVersion = pluginVersion;
        _client = new SyncClient(log);

        _framework.Update += OnUpdate;
        _detector.Cleared += OnDetectorCleared;
        _detector.Scanned += OnScanned;
        _detector.SightingObservedDead += OnSightingDeath;

        ApplySettings();
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        _detector.Cleared -= OnDetectorCleared;
        _detector.Scanned -= OnScanned;
        _detector.SightingObservedDead -= OnSightingDeath;
        _client.Dispose();
    }

    // -----------------------------------------------------------------------
    // Settings and status
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads the sync settings and (re)connects if they changed. Called by
    /// the settings UI once an edit is finished, not on every keystroke.
    /// </summary>
    public void ApplySettings(bool force = false)
    {
        var wanted = $"{_config.SyncEnabled}|{_config.SyncServerUrl}|{_config.SyncPassword}|{_config.SyncDisplayName}|{_config.SyncShareTrain}";
        if (!force && wanted == _appliedSettings) return;
        _appliedSettings = wanted;

        _client.Stop();
        ForgetRemoteState();
        LastError = string.Empty;

        if (!_config.SyncEnabled) return;

        if (!TryBuildUri(_config.SyncServerUrl, out var uri, out var problem))
        {
            LastError = problem;
            return;
        }

        RefreshHello();
        _client.Start(uri, () => _hello);
    }

    public string Status
    {
        get
        {
            if (!_config.SyncEnabled) return "Off.";
            if (!string.IsNullOrEmpty(LastError) && !IsConnected) return LastError;

            if (IsConnected)
            {
                var others = Math.Max(0, _clients.Count - 1);
                return $"Connected to {ServerName} (server {ServerVersion}) — {others} other{(others == 1 ? "" : "s")} online.";
            }

            return _client.StatusText;
        }
    }

    /// <summary>
    /// Turns what someone typed into a WebSocket URL: adds the scheme if it
    /// is missing, swaps http for ws, and points a bare host at /ws.
    /// </summary>
    public static bool TryBuildUri(string raw, out Uri uri, out string problem)
    {
        uri = null!;
        problem = string.Empty;

        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            problem = "No server URL set.";
            return false;
        }

        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "wss://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            problem = "That server URL could not be read.";
            return false;
        }

        var scheme = parsed.Scheme.ToLowerInvariant() switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" => "ws",
            "wss" => "wss",
            _ => null,
        };

        if (scheme is null)
        {
            problem = "The server URL needs to start with wss:// (or ws:// for a LAN).";
            return false;
        }

        var builder = new UriBuilder(parsed) { Scheme = scheme };
        if (string.IsNullOrEmpty(builder.Path) || builder.Path == "/") builder.Path = "/ws";
        uri = builder.Uri;
        return true;
    }

    // -----------------------------------------------------------------------
    // Framework tick
    // -----------------------------------------------------------------------

    private void OnUpdate(IFramework framework)
    {
        if (!_config.SyncEnabled) return;

        try
        {
            DrainInbox();
            ExpireRemote();
            var dt = framework.UpdateDelta.TotalSeconds;

            _sinceHello += dt;
            if (_sinceHello >= HelloRefresh.TotalSeconds)
            {
                _sinceHello = 0;
                RefreshHello();
            }

            if (!_client.IsConnected) return;

            _sinceDiff += dt;
            if (_sinceDiff >= DiffInterval.TotalSeconds)
            {
                _sinceDiff = 0;
                DiffTrain();
                ExpireRemote();
            }

            _sincePresence += dt;
            if (_sincePresence >= PresenceInterval.TotalSeconds)
            {
                _sincePresence = 0;
                MaybeSendPresence();
            }

            _sincePing += dt;
            if (_sincePing >= PingInterval.TotalSeconds)
            {
                _sincePing = 0;
                _client.Send(new PingMessage());
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Sync update failed.");
        }
    }

    private void DrainInbox()
    {
        while (_client.TryDequeue(out var type, out var payload))
        {
            try
            {
                Apply(type, payload);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, $"Could not apply a \"{type}\" message from the sync server.");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Remote -> local
    // -----------------------------------------------------------------------

    private void Apply(string type, JObject payload)
    {
        switch (type)
        {
            case ServerMessageTypes.Welcome:
                ApplyWelcome(SyncProtocol.Deserialize<WelcomeMessage>(payload)!);
                break;

            case ServerMessageTypes.Error:
            {
                var error = SyncProtocol.Deserialize<ErrorMessage>(payload)!;
                LastError = error.Message;
                _log.Warning($"Sync server: {error.Message}");
                break;
            }

            case ServerMessageTypes.TrainUpsert:
                if (_config.SyncShareTrain)
                    ApplyMarks(SyncProtocol.Deserialize<TrainUpsertBroadcast>(payload)!.Marks);
                break;

            case ServerMessageTypes.TrainRemove:
                if (_config.SyncShareTrain)
                    ApplyRemoves(SyncProtocol.Deserialize<TrainRemoveBroadcast>(payload)!.Keys);
                break;

            case ServerMessageTypes.TrainClear:
                if (_config.SyncShareTrain)
                    ApplyRemoteClear(SyncProtocol.Deserialize<TrainClearBroadcast>(payload)!.By);
                break;

            case ServerMessageTypes.TrainOrder:
                if (_config.SyncShareTrain)
                    ApplyOrder(SyncProtocol.Deserialize<TrainOrderBroadcast>(payload)!.Keys);
                break;

            case ServerMessageTypes.Sightings:
                foreach (var s in SyncProtocol.Deserialize<SightingsBroadcast>(payload)!.Sightings)
                    AddRemoteSighting(s);
                Bump();
                break;

            case ServerMessageTypes.SightingsExpired:
                foreach (var k in SyncProtocol.Deserialize<SightingsExpiredBroadcast>(payload)!.Keys)
                    _remote.Remove(k.ToTuple());
                Bump();
                break;

            case ServerMessageTypes.SRankUpdate:
                foreach (var e in SyncProtocol.Deserialize<SRankUpdateBroadcast>(payload)!.Entries)
                    _sranks[e.Key] = e;
                Bump();
                break;

            case ServerMessageTypes.SpawnUpdate:
                foreach (var z in SyncProtocol.Deserialize<SpawnUpdateBroadcast>(payload)!.Zones)
                    _zones[z.Key] = z;
                Bump();
                break;

            case ServerMessageTypes.Presence:
                _clients = SyncProtocol.Deserialize<PresenceBroadcast>(payload)!.Clients;
                Bump();
                break;

            case ServerMessageTypes.Pong:
                if (payload["faloop"] is JObject faloop)
                    _faloop = SyncProtocol.Deserialize<SyncFaloopStatus>(faloop)!;
                break;
        }
    }

    private void ApplyWelcome(WelcomeMessage welcome)
    {
        ServerVersion = welcome.ServerVersion;
        ServerName = string.IsNullOrEmpty(welcome.ServerName) ? "group server" : welcome.ServerName;
        LastError = string.Empty;
        _clients = welcome.Clients;
        _faloop = welcome.Faloop;

        // A fresh snapshot replaces everything remembered from before the
        // reconnect. The server train is authoritative; local rows are kept
        // as an explicit upload option so deleted marks never return silently.
        _remote.Clear();
        foreach (var s in welcome.Sightings) AddRemoteSighting(s);

        _sranks.Clear();
        foreach (var s in welcome.SRanks) _sranks[s.Key] = s;

        _zones.Clear();
        foreach (var z in welcome.SpawnZones) _zones[z.Key] = z;

        _known.Clear();
        _sentSightings.Clear();
        _lastSentOrder = new();
        _lastPresence = default;

        if (_config.SyncShareTrain)
        {
            var backup = _localTrainBackup.ToDictionary(m => m.Key);
            var sharedKeys = welcome.Marks.Select(m => m.Key).ToHashSet();
            foreach (var local in _detector.Marks.Values.Where(m => !sharedKeys.Contains(m.Key)))
                backup[local.Key] = ToSyncMark(local, 0);
            _localTrainBackup = backup.Values.ToList();
            _config.SyncLocalTrainBackup = _localTrainBackup;
            _config.Save();
            _applying = true;
            try { foreach (var key in _detector.Marks.Keys.ToList()) _detector.Remove(key); }
            finally { _applying = false; }
            ApplyMarks(welcome.Marks);
            ApplyOrder(welcome.Order);
        }

        Bump();
        _log.Information($"Sync: connected to server {welcome.ServerVersion}; {welcome.Marks.Count} shared marks, {welcome.Clients.Count} online.");
    }

    private void ApplyMarks(List<SyncMark> marks)
    {
        _applying = true;
        try
        {
            foreach (var m in marks)
            {
                var key = m.Key;
                if (key.NameId == 0) continue;

                if (!_detector.Marks.TryGetValue(key, out var local))
                {
                    local = new DetectedMark
                    {
                        Name = m.Name,
                        NameId = m.NameId,
                        TerritoryId = m.TerritoryId,
                        MapId = m.MapId,
                        Instance = m.Instance,
                        WorldId = m.WorldId,
                        WorldName = string.IsNullOrEmpty(m.WorldName) ? _worldData.NameOf(m.WorldId) : m.WorldName,
                        MapPosition = new Vector2(m.X, m.Y),
                        Dead = m.Dead,
                        FirstSeenUtc = m.FirstSeen,
                        LastSeenUtc = m.LastSeen,
                        DeathObservedAtUtc = m.DeathAt,
                        IsCustom = m.IsCustom,
                        ZoneName = m.ZoneName,
                        Spiced = m.Spiced,
                    };
                    _detector.Merge(new[] { local });
                }
                else
                {
                    var hasKnown = _known.TryGetValue(key, out var known);

                    // Untouched means "nothing edited here since the last
                    // sync". A same-revision echo of our own position update
                    // must not overwrite a tick the user made a moment ago;
                    // a higher revision is somebody else's edit and wins.
                    var untouched = !hasKnown || Signature(local) == known.Signature;

                    if (m.LastSeen >= local.LastSeenUtc)
                    {
                        local.LastSeenUtc = m.LastSeen;
                        local.MapPosition = new Vector2(m.X, m.Y);
                    }

                    if (m.FirstSeen != default && m.FirstSeen < local.FirstSeenUtc)
                        local.FirstSeenUtc = m.FirstSeen;

                    if (!hasKnown || m.Revision > known.Revision || untouched)
                    {
                        local.Dead = m.Dead;
                        local.DeathObservedAtUtc = m.DeathAt;
                        local.Spiced = m.Spiced;
                        local.IsCustom = m.IsCustom;
                        if (!string.IsNullOrEmpty(m.Name)) local.Name = m.Name;
                        if (m.IsCustom) local.ZoneName = m.ZoneName;
                        if (m.TerritoryId != 0) local.TerritoryId = m.TerritoryId;
                        if (m.MapId != 0) local.MapId = m.MapId;
                    }
                }

                if (local.Dead)
                    _detector.RemoveSighting(key.NameId, key.Instance, key.WorldId);

                // Remember the canonical signature, not any unsent local edit.
                var canonical = new DetectedMark { Dead = m.Dead, DeathObservedAtUtc = m.DeathAt,
                    Spiced = m.Spiced, Name = m.Name, ZoneName = m.ZoneName, IsCustom = m.IsCustom,
                    TerritoryId = m.TerritoryId, MapId = m.MapId, MapPosition = new Vector2(m.X, m.Y), LastSeenUtc = m.LastSeen };
                _known[key] = new KnownMark(Signature(canonical), m.Revision, m.Dead);
            }
        }
        finally
        {
            _applying = false;
        }

        Bump();
    }

    private void ApplyRemoves(List<SyncKey> keys)
    {
        _applying = true;
        try
        {
            foreach (var k in keys)
            {
                var key = k.ToTuple();
                _detector.Remove(key);
                _known.Remove(key);
            }
        }
        finally
        {
            _applying = false;
        }

        Bump();
    }

    private void ApplyRemoteClear(string by)
    {
        _applying = true;
        try
        {
            _known.Clear();
            _lastSentOrder = new();
            RemoteTrainCleared?.Invoke(by);
            if (_detector.Marks.Count > 0) _detector.Clear();
        }
        finally
        {
            _applying = false;
        }

        Bump();
    }

    private void ApplyOrder(List<SyncKey> keys)
    {
        _applying = true;
        try
        {
            var ordered = new List<DetectedMark>();
            var seen = new HashSet<(uint, uint, uint)>();

            foreach (var k in keys)
            {
                var key = k.ToTuple();
                if (_detector.Marks.TryGetValue(key, out var mark) && seen.Add(key))
                    ordered.Add(mark);
            }

            // Anything local the server has not heard of yet keeps its place
            // at the end, in the order it was scouted.
            foreach (var mark in _detector.Ordered())
                if (seen.Add(mark.Key)) ordered.Add(mark);

            _detector.ApplyOrder(ordered);
            _lastSentOrder = ordered.Select(m => m.Key).ToList();
        }
        finally
        {
            _applying = false;
        }

        Bump();
    }

    private void AddRemoteSighting(SyncSighting s)
    {
        if (s.NameId == 0) return;

        // Our own reports come back to us too. They are already on the map
        // from the local scan, and once that expires the map should say the
        // mark is gone — not keep it lit on the strength of our own echo.


        var key = s.Key;
        if (!_remote.TryGetValue(key, out var existing))
        {
            existing = new OtherRankSighting
            {
                Name = s.Name,
                NameId = s.NameId,
                Rank = s.Rank == "B" ? HuntRank.B : s.Rank == "A" ? HuntRank.A : HuntRank.S,
                TerritoryId = s.TerritoryId,
                MapId = s.MapId,
                Instance = s.Instance,
                WorldId = s.WorldId,
                WorldName = _worldData.NameOf(s.WorldId),
                ZoneName = _detector.GetZoneName(s.TerritoryId),
            };
            _remote[key] = existing;
        }

        existing.MapPosition = new Vector2(s.X, s.Y);
        existing.HealthPercent = s.HpPercent;
        existing.LastSeenUtc = s.SeenAt;
        existing.Reporter = string.IsNullOrEmpty(s.Reporter) ? "someone" : s.Reporter;
    }

    private void ExpireRemote()
    {
        var now = DateTime.UtcNow;
        var stale = _remote.Where(kv => now - kv.Value.LastSeenUtc > RemoteSightingTtl).Select(kv => kv.Key).ToList();
        if (stale.Count == 0) return;
        foreach (var key in stale) _remote.Remove(key);
        Bump();
    }

    private void ForgetRemoteState()
    {
        _known.Clear();
        _lastSentOrder = new();
        _remote.Clear();
        _sranks.Clear();
        _zones.Clear();
        _clients = new();
        _faloop = new();
        _sentSightings.Clear();
        _lastPresence = default;
        ServerVersion = string.Empty;
        Bump();
    }

    private void Bump() => RemoteVersion++;

    // -----------------------------------------------------------------------
    // Local -> remote
    // -----------------------------------------------------------------------

    private void DiffTrain()
    {
        if (!_config.SyncShareTrain) return;

        var upserts = new List<SyncMark>();
        var newlyDead = new List<SyncKey>();
        var present = new HashSet<(uint, uint, uint)>();

        foreach (var mark in _detector.Marks.Values)
        {
            var key = mark.Key;
            present.Add(key);

            var signature = Signature(mark);
            var hasKnown = _known.TryGetValue(key, out var known);
            if (hasKnown && known.Signature == signature) continue;

            upserts.Add(ToSyncMark(mark, hasKnown ? known.Revision : 0));
            if (mark.Dead && (!hasKnown || !known.Dead)) newlyDead.Add(SyncKey.From(key));
            _known[key] = new KnownMark(signature, hasKnown ? known.Revision : 0, mark.Dead);
        }

        var removed = _known.Keys.Where(k => !present.Contains(k)).ToList();
        foreach (var key in removed) _known.Remove(key);

        if (upserts.Count > 0) _client.Send(new TrainUpsertMessage { Marks = upserts });
        if (removed.Count > 0) _client.Send(new TrainRemoveMessage { Keys = removed.Select(SyncKey.From).ToList() });
        if (newlyDead.Count > 0) _client.Send(new SightingsRemoveMessage { Keys = newlyDead });

        var order = _detector.Ordered().Select(m => m.Key).ToList();
        if (!order.SequenceEqual(_lastSentOrder))
        {
            _lastSentOrder = order;
            _client.Send(new TrainOrderMessage { Keys = order.Select(SyncKey.From).ToList() });
        }
    }

    private void OnDetectorCleared()
    {
        if (_applying) return;
        _known.Clear();
        _lastSentOrder = new();
        if (IsConnected && _config.SyncShareTrain)
            _client.Send(new TrainClearMessage());
    }

    private void OnSightingDeath(OtherRankSighting sighting, DateTime at) =>
        ReportMarkDeath(sighting.NameId, sighting.Instance, sighting.TerritoryId, at, sighting.Rank == HuntRank.S);

    private void OnScanned()
    {
        if (!IsConnected || !_config.SyncShareSightings) return;

        try
        {
            PublishSightings();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not share sightings.");
        }
    }

    private void PublishSightings()
    {
        var territory = _clientState.TerritoryType;
        var instance = MarkDetector.GetCurrentInstance();
        var world = _detector.CurrentWorldId();
        var now = DateTime.UtcNow;
        var batch = new List<SyncSighting>();

        foreach (var s in _detector.OtherRanks.Values)
        {
            if (s.IsRemote) continue;
            if (s.TerritoryId != territory || s.Instance != instance || s.WorldId != world) continue;

            // Only what this pass actually saw; a sighting a scan or two old
            // is on its way out.
            if ((now - s.LastSeenUtc).TotalSeconds > 2) continue;

            var key = s.Key;
            var send = !_sentSightings.TryGetValue(key, out var prev)
                       || Math.Abs(prev.Hp - s.HealthPercent) >= 1f
                       || Vector2.Distance(prev.Pos, s.MapPosition) >= 0.3f
                       || now - prev.At >= SightingHeartbeat;
            if (!send) continue;

            batch.Add(new SyncSighting
            {
                NameId = s.NameId,
                Instance = s.Instance,
                WorldId = s.WorldId,
                Name = s.Name,
                Rank = SsEventMobs.Contains(s.NameId) ? "SS" : s.Rank.ToString(),
                TerritoryId = s.TerritoryId,
                MapId = s.MapId,
                X = s.MapPosition.X,
                Y = s.MapPosition.Y,
                HpPercent = s.HealthPercent,
                SeenAt = s.LastSeenUtc,
                SpawnPointIndex = s.SpawnPointIndex,
            });
            _sentSightings[key] = (s.HealthPercent, s.MapPosition, now);
        }

        if (batch.Count > 0) _client.Send(new SightingsMessage { Sightings = batch });

        foreach (var key in _sentSightings.Where(kv => now - kv.Value.At > SentSightingMemory).Select(kv => kv.Key).ToList())
            _sentSightings.Remove(key);
    }

    private void MaybeSendPresence()
    {
        var here = (_detector.CurrentWorldId(), _clientState.TerritoryType, MarkDetector.GetCurrentInstance());
        if (here == _lastPresence) return;
        _lastPresence = here;
        _client.Send(new PresenceUpdateMessage { WorldId = here.Item1, TerritoryId = here.Item2, Instance = here.Item3 });
    }

    // -----------------------------------------------------------------------
    // Kills, from the tally or by hand
    // -----------------------------------------------------------------------

    /// <summary>
    /// A mark died in front of this client. Any rank: its sighting comes
    /// off everyone's map. An S rank also starts the group's clock for it.
    /// </summary>
    public void ReportMarkDeath(uint nameId, uint instance, uint territoryId, DateTime killedAtUtc, bool isSRank)
    {
        if (!IsConnected || (!_config.SyncShareSightings && !_config.SyncReportSRankKills)) return;

        var world = _detector.CurrentWorldId();
        var key = (nameId, instance, world);

        _client.Send(new SightingsRemoveMessage { Keys = new() { SyncKey.From(key) } });
        _sentSightings.Remove(key);

        if (!isSRank || !_config.SyncReportSRankKills || !SRankTimerData.IsTimerSRank(nameId)) return;
        if (_reportedKills.TryGetValue(key, out var last) && killedAtUtc - last < KillDedupe) return;
        _reportedKills[key] = killedAtUtc;

        float? x = null, y = null;
        int? point = null;
        if (_detector.OtherRanks.TryGetValue(key, out var seen) || _remote.TryGetValue(key, out seen))
        {
            x = seen.MapPosition.X;
            y = seen.MapPosition.Y;
            point = seen.SpawnPointIndex;
        }

        _client.Send(new SRankKillMessage
        {
            NameId = nameId,
            WorldId = world,
            Instance = instance,
            TerritoryId = territoryId,
            KilledAt = killedAtUtc,
            X = x,
            Y = y,
            SpawnPointIndex = point,
            Source = "observed",
        });

        _log.Information($"Sync: reported S rank {nameId} dead on world {world}.");
    }

    public void ReportManualKill(SRankTimer timer, uint worldId, uint instance, DateTime killedAtUtc)
    {
        if (!IsConnected) return;
        _client.Send(new SRankKillMessage
        {
            NameId = timer.NameId,
            WorldId = worldId,
            Instance = instance,
            TerritoryId = timer.TerritoryId,
            KilledAt = killedAtUtc,
            Source = "manual",
        });
    }

    public void ReportMaintenance(uint worldId, DateTime endedAtUtc)
    {
        if (!IsConnected) return;
        _client.Send(new SRankMaintenanceMessage { WorldId = worldId, EndedAt = endedAtUtc });
    }

    public void ClearKill(uint nameId, uint worldId, uint instance)
    {
        if (!IsConnected) return;
        _client.Send(new SRankClearMessage { NameId = nameId, WorldId = worldId, Instance = instance });
    }

    public void ResetZone(uint territoryId, uint worldId, uint instance)
    {
        if (!IsConnected) return;
        _client.Send(new SpawnResetMessage { TerritoryId = territoryId, WorldId = worldId, Instance = instance });
    }

    // -----------------------------------------------------------------------
    // Lookups for the UI and the map
    // -----------------------------------------------------------------------

    public SyncSpawnZone? ZoneFor(uint territoryId, uint worldId, uint instance) =>
        _zones.TryGetValue((territoryId, worldId, instance), out var z) ? z : null;

    public SyncSRankStatus? StatusFor(uint nameId, uint worldId, uint instance) =>
        _sranks.TryGetValue((nameId, instance, worldId), out var s) ? s : null;

    /// <summary>Whether anybody — this client or another — can see the mark right now.</summary>
    public bool IsSeenUp(uint nameId, uint worldId, uint instance)
    {
        var key = (nameId, instance, worldId);
        var now = DateTime.UtcNow;
        return (_detector.OtherRanks.TryGetValue(key, out var local) && now - local.LastSeenUtc < RemoteSightingTtl)
            || (_remote.TryGetValue(key, out var remote) && now - remote.LastSeenUtc < RemoteSightingTtl);
    }

    public string DisplayName()
    {
        var chosen = _config.SyncDisplayName?.Trim();
        if (!string.IsNullOrEmpty(chosen)) return chosen;

        return "Anonymous";
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void RefreshHello()
    {
        _worlds ??= _worldData.DataCenters
            .SelectMany(dc => _worldData.WorldsIn(dc.Id))
            .Select(w => new SyncWorld { Id = w.RowId, Name = w.Name })
            .ToList();

        _hello = new HelloMessage
        {
            Password = _config.SyncPassword,
            ClientName = DisplayName(),
            ClientVersion = _pluginVersion,
            WorldId = _detector.CurrentWorldId(),
            TerritoryId = _clientState.TerritoryType,
            Instance = MarkDetector.GetCurrentInstance(),
            Worlds = _worlds,
        };
    }

    /// <summary>
    /// Everything about a mark that is worth telling the server. Position
    /// to a tenth of a coordinate and last-seen to ten seconds, so a mark
    /// standing still in range does not produce an update every scan.
    /// </summary>
    private static int Signature(DetectedMark m)
    {
        var h = new HashCode();
        h.Add(m.Dead);
        h.Add(m.DeathObservedAtUtc?.Ticks / TimeSpan.TicksPerSecond ?? 0);
        h.Add(m.Spiced);
        h.Add(m.Name);
        h.Add(m.ZoneName);
        h.Add(m.IsCustom);
        h.Add(m.TerritoryId);
        h.Add(m.MapId);
        h.Add(MathF.Round(m.MapPosition.X, 1));
        h.Add(MathF.Round(m.MapPosition.Y, 1));
        h.Add(m.LastSeenUtc.Ticks / (10 * TimeSpan.TicksPerSecond));
        return h.ToHashCode();
    }

    private static SyncMark ToSyncMark(DetectedMark m, long baseRevision) => new()
    {
        NameId = m.NameId,
        Instance = m.Instance,
        WorldId = m.WorldId,
        Name = m.Name,
        TerritoryId = m.TerritoryId,
        MapId = m.MapId,
        WorldName = m.WorldName,
        X = m.MapPosition.X,
        Y = m.MapPosition.Y,
        Dead = m.Dead,
        FirstSeen = AsUtc(m.FirstSeenUtc),
        LastSeen = AsUtc(m.LastSeenUtc),
        DeathAt = m.DeathObservedAtUtc is { } d ? AsUtc(d) : null,
        IsCustom = m.IsCustom,
        ZoneName = m.ZoneName,
        Spiced = m.Spiced,
        BaseRevision = baseRevision,
    };

    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
