using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// Diffs local hunt state and applies remote updates on the framework thread.
/// The socket only queues frames; draining them here keeps game state off the socket thread.
/// </summary>
public sealed partial class SyncCoordinator : IDisposable
{
    private static readonly TimeSpan DiffInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HelloRefresh = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SightingHeartbeat = TimeSpan.FromSeconds(1);

    /// <summary>Matches the server's own expiry, so both forget at the same moment.</summary>
    public static readonly TimeSpan RemoteSightingTtl = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan KillDedupe = TimeSpan.FromMinutes(5);

    private readonly record struct KnownMark(int Signature, long Revision, bool Dead);

    /// <summary>
    /// The SS-event marks and their minions. S rank in the game's data, but
    /// they come from an event rather than a clock and never stand on a
    /// spawn point, so they are relayed as "SS" and kept out of the S-rank
    /// logic on the server. Same pairs as SsEventWatcher's table.
    /// </summary>
    internal static readonly HashSet<uint> SsEventMobs = new() { 8915, 8916, 10615, 10616, 13406, 13407 };

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

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId, uint TerritoryId, uint EntityId), OtherRankSighting> _remote = new();
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), SyncSRankStatus> _sranks = new();
    private readonly Dictionary<(uint TerritoryId, uint WorldId, uint Instance), SyncSpawnZone> _zones = new();
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId, uint TerritoryId, uint EntityId), VisibleMark> _visibleMarks = new();
    public IReadOnlyCollection<VisibleMark> VisibleMarks => _visibleMarks.Values;
    private readonly ActiveMarkGrace _activeMarkGrace = new();
    private readonly RemoteSightingClock _sightingClock = new();
    public DateTime ServerTimeFor(DateTime localTime) => _sightingClock.ToServer(localTime);
    public IReadOnlyCollection<VisibleMark> ActiveMarkDisplay => _activeMarkGrace.Snapshot(DateTime.UtcNow);
    public string ClientId { get; private set; } = string.Empty;
    public bool SupportsVisibleMarks { get; private set; }
    public bool SupportsScopedTrainWatches { get; private set; }
    public bool SupportsManualMapping { get; private set; }
    private List<SyncPresence> _clients = new();
    private SyncFaloopStatus _faloop = new();

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId, uint TerritoryId, uint EntityId), (float Hp, Vector2 Pos, DateTime At, int? Point, bool? InCombat)> _sentSightings = new();
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), DateTime> _reportedKills = new();

    private List<SyncWorld>? _worlds;
    private HelloMessage _hello = new();
    private readonly SyncConnectionPolicy _connectionPolicy = new();
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

    private long _watchRevision;
    private string _watchKnown = "[]";
    private string? _watchSent;
    public int LocalWatchBackupCount => _config.SyncLocalWatchBackup.Count;
    public void UploadWatchBackup()
    {
        if (!IsConnected || !_config.SyncShareTrain) return;
        var merged = ReadLocalWatches();
        foreach (var watch in _config.SyncLocalWatchBackup)
        {
            var index = merged.FindIndex(w => w.Label == watch.Label && w.TerritoryId == watch.TerritoryId && w.X == watch.X && w.Y == watch.Y);
            if (index < 0) merged.Add(watch); else merged[index] = watch;
        }
        SetLocalWatches(merged);
        _config.SyncLocalWatchBackup.Clear(); _config.Save();
    }
    private List<SyncWatch> ReadLocalWatches() => _config.Flags.Select(f => new SyncWatch
    { WorldId=f.WorldId, Instance=f.Instance, Automatic=f.Automatic, Label = f.Label, SpawnStatus = (int)f.SpawnStatus, TerritoryId = f.TerritoryId, HasLocation = f.HasLocation, X = f.X, Y = f.Y }).ToList();
    private void SetLocalWatches(List<SyncWatch> watches)
    {
        _config.Flags = watches.Select(w => new FlagEntry { WorldId=w.WorldId, Instance=w.Instance, Automatic=w.Automatic, Label = w.Label, SpawnStatus = (SpawnStatus)w.SpawnStatus,
            TerritoryId = w.TerritoryId, HasLocation = w.HasLocation, X = w.X, Y = w.Y }).ToList();
        _config.Save();
    }
    private void ApplyWatches(WatchesBroadcast state, bool joining = false)
    {
        if (!_config.SyncShareTrain || (!joining && state.Revision < _watchRevision)) return;
        var local = ReadLocalWatches(); var localJson = SyncProtocol.Serialize(local);
        var incoming = SyncProtocol.Serialize(state.Watches);
        var conflictingLocalEdit = _watchSent != incoming && localJson != _watchKnown && localJson != incoming;
        if ((joining || !state.Accepted || conflictingLocalEdit) && local.Count > 0 && localJson != incoming)
        {
            foreach (var watch in local)
                if (!_config.SyncLocalWatchBackup.Any(w => SyncProtocol.Serialize(w) == SyncProtocol.Serialize(watch)))
                    _config.SyncLocalWatchBackup.Add(watch);
        }
        var preserveLaterEdit = !joining && state.Accepted && _watchSent == incoming && localJson != _watchSent;
        _watchRevision = state.Revision; _watchKnown = incoming; _watchSent = null;
        if (!preserveLaterEdit) SetLocalWatches(state.Watches);
        if (!state.Accepted || (!joining && conflictingLocalEdit)) LastError = "S-rank watches changed on the server. Your list was saved locally; review before uploading it again.";
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
    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId, uint TerritoryId, uint EntityId), OtherRankSighting> RemoteSightings => _remote;

    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), SyncSRankStatus> SRankStatuses => _sranks;
    public IReadOnlyDictionary<(uint TerritoryId, uint WorldId, uint Instance), SyncSpawnZone> SpawnZones => _zones;
    public SyncFaloopStatus Faloop => _faloop;

    /// <summary>
    /// Somebody else emptied the shared train. Raised while remote changes
    /// are being applied, so a handler that clears local state does not
    /// echo the clear straight back to the server.
    /// </summary>
    public event Action<string>? RemoteTrainCleared;

    /// <summary>
    /// Rows the shared train dropped because a report went out for them. The
    /// plugin handles these rather than this class, because forgetting them
    /// means reaching into report history, which the plugin owns.
    /// </summary>
    public event Action<List<ReportedMark>>? ReportedRemoval;
    public event Action<SRankSpawnBroadcast>? SRankSpawned;

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

        var startup = new StartupTransaction();
        startup.Add(_client.Dispose);
        try
        {
            startup.Add(() => { _framework.Update -= OnUpdate; });
            _framework.Update += OnUpdate;
            startup.Add(() => { _detector.Cleared -= OnDetectorCleared; });
            _detector.Cleared += OnDetectorCleared;
            startup.Add(() => { _detector.Scanned -= OnScanned; });
            _detector.Scanned += OnScanned;
            startup.Add(() => { _detector.SightingObservedDead -= OnSightingDeath; });
            _detector.SightingObservedDead += OnSightingDeath;

            ApplySettings();
            startup.Commit();
        }
        catch (Exception ex)
        {
            _disposed = true;
            throw startup.Rollback(ex);
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _framework.Update -= OnUpdate;
        _detector.Cleared -= OnDetectorCleared;
        _detector.Scanned -= OnScanned;
        _detector.SightingObservedDead -= OnSightingDeath;
        _client.Dispose();
        SaveARankSightings(force: true);
    }

    // Settings and status

    /// <summary>
    /// Reads the sync settings and (re)connects if they changed. Called by
    /// the settings UI once an edit is finished, not on every keystroke.
    /// </summary>
    public void ApplySettings(bool force = false)
    {
        var wanted = new SyncConnectionPolicy.Settings(_config.SyncEnabled, _config.SyncServerUrl,
            _config.SyncPassword, _config.SyncDisplayName, _config.SyncShareTrain, _config.SyncAllowPlaintext);
        _connectionPolicy.Apply(wanted, force,
            () => { _client.Stop(); ForgetRemoteState(); },
            error => LastError = error,
            uri => { RefreshHello(); _client.Start(uri, () => _hello); });
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
    public static bool TryBuildUri(string raw, out Uri uri, out string problem, bool allowPlaintext = false) =>
        SyncEndpoint.TryBuild(raw,out uri,out problem,allowPlaintext);

    // Framework tick

    private void OnUpdate(IFramework framework)
    {
        if (_disposed) return;

        try
        {
            SaveARankSightings();
            if (!_config.SyncEnabled) return;
            DrainInbox();
            CheckPresetRequest();
            ExpireRemote();
            var dt = framework.UpdateDelta.TotalSeconds;

            _sinceHello += dt;
            if (_sinceHello >= HelloRefresh.TotalSeconds)
            {
                _sinceHello = 0;
                RefreshHello();
            }

            if (!_client.IsConnected) { _counterReady = false; return; }

            _sinceDiff += dt;
            if (_sinceDiff >= DiffInterval.TotalSeconds)
            {
                _sinceDiff = 0;
                SendCounterContributions();
                DiffTrain();
                SendManualScouts();
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
        var budget=System.Diagnostics.Stopwatch.StartNew();
        for (var processed=0; processed<16 && budget.ElapsedMilliseconds<3 && _client.TryDequeue(out var type,out var payload); processed++)
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

    // Remote -> local

    private void Apply(string type, JObject payload)
    {
        switch (type)
        {
            case "train.presets":
                ApplyPresets(SyncProtocol.Deserialize<TrainPresetsBroadcast>(payload)!);
                break;
            case "marks.visible":
                var visible = SyncProtocol.Deserialize<VisibleMarksBroadcast>(payload)!;
                RememberARankSightings(ARankSightings.FromSightings(visible.Marks.Select(m => m.Mark)));
                foreach (var mark in visible.Marks) { _visibleMarks[mark.Mark.LiveKey] = mark; _activeMarkGrace.Update(mark, DateTime.UtcNow); }
                foreach (var key in visible.Removed) _visibleMarks.Remove(key.ToLiveKey());
                break;
            case "counter.state":
                ApplyCounters(SyncProtocol.Deserialize<CounterBroadcast>(payload)!.Counters);
                break;
            case "train.finished":
            {
                var result=SyncProtocol.Deserialize<TrainFinishResult>(payload)!;
                if (_finishRequests.Remove(result.RequestId, out var pending)) pending.TrySetResult(result);
                break;
            }
            case "train.scouts":
                var scouts=SyncProtocol.Deserialize<TrainScoutsBroadcast>(payload)!;
                _trainScouts=scouts.Names; ApplyScoutCredits(scouts.Credits);
                if (scouts.Reset && _config.SyncShareTrain)
                { _config.AdditionalScouts.Clear(); _config.ScanningPaused=true; _scoutingSent=false; _config.Save(); }
                if (_trainScouts.Count==0) _manualScoutsSent=null;
                break;
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
            {
                var marks = SyncProtocol.Deserialize<TrainUpsertBroadcast>(payload)!.Marks;
                RememberARankSightings(ARankSightings.FromMarks(marks));
                if (ARankHistory.Merge(_config.ARankKills, ARankHistory.FromMarks(marks), DateTime.UtcNow)) _config.Save();
                if (_config.SyncShareTrain) ApplyMarks(marks);
                break;
            }

            case ServerMessageTypes.TrainRemove:
                if (_config.SyncShareTrain)
                {
                    var removal = SyncProtocol.Deserialize<TrainRemoveBroadcast>(payload)!;
                    ApplyRemoves(removal.Keys, removal.Reported, removal.ReportedMarks);
                }
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
                var sightings = SyncProtocol.Deserialize<SightingsBroadcast>(payload)!.Sightings;
                if (!SupportsVisibleMarks) RememberARankSightings(ARankSightings.FromSightings(sightings));
                foreach (var s in sightings)
                    AddRemoteSighting(s);
                Bump();
                break;

            case ServerMessageTypes.SightingsExpired:
                foreach (var k in SyncProtocol.Deserialize<SightingsExpiredBroadcast>(payload)!.Keys)
                    _remote.Remove(k.ToLiveKey());
                Bump();
                break;

            case "train.watches":
                ApplyWatches(SyncProtocol.Deserialize<WatchesBroadcast>(payload)!);
                break;

            case "srank.spawn":
                SRankSpawned?.Invoke(SyncProtocol.Deserialize<SRankSpawnBroadcast>(payload)!);
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
                _sightingClock.Update(SyncProtocol.Deserialize<PongMessage>(payload)!.ServerTime, DateTime.UtcNow);
                if (payload["faloop"] is JObject faloop)
                    _faloop = SyncProtocol.Deserialize<SyncFaloopStatus>(faloop)!;
                break;
        }
    }

    private void ApplyWelcome(WelcomeMessage welcome)
    {
        _sightingClock.Reset();
        _sightingClock.Update(welcome.ServerTime, DateTime.UtcNow);
        SupportsTrainPresets = welcome.SupportsTrainPresets;
        TrainPresets = welcome.TrainPresets;
        PendingPresetRequest = null;
        AcceptedPresetRevision = null;
        CompletedPresetRequest = null;
        PresetStatus = "";
        ResetCompletionConnection();
        SupportsTrainFinish=welcome.SupportsTrainFinish; SupportsPartialFinish=welcome.SupportsPartialFinish && welcome.SupportsReportedHistory;
        _trainScouts=welcome.TrainScouts;
        SupportsScoutRemoval=welcome.SupportsScoutRemoval; ApplyScoutCredits(welcome.ScoutCredits);
        _watchSent = null;
        RememberARankSightings(ARankSightings.FromMarks(welcome.Marks).Concat(ARankSightings.FromSightings(
            welcome.SupportsVisibleMarks ? welcome.VisibleMarks.Select(m => m.Mark) : welcome.Sightings)));
        if (ARankHistory.Merge(_config.ARankKills, welcome.ARankKills.Concat(ARankHistory.FromMarks(welcome.Marks)), DateTime.UtcNow)) _config.Save();
        ClientId = welcome.ClientId;
        SupportsVisibleMarks = welcome.SupportsVisibleMarks;
        SupportsManualMapping = welcome.SupportsManualMapping;
        SupportsScopedTrainWatches = welcome.SupportsScopedTrainWatches;
        _visibleMarks.Clear(); _activeMarkGrace.Clear();
        foreach (var mark in welcome.VisibleMarks) { _visibleMarks[mark.Mark.LiveKey] = mark; _activeMarkGrace.Update(mark, DateTime.UtcNow); }
        WelcomeCounters(welcome);
        ApplyWatches(welcome.WatchState, joining: true);
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
            _applying = true;
            try { ReportedRemoval?.Invoke(welcome.ReportedMarks); }
            finally { _applying = false; }
        }

        Bump();
        _log.Information($"Sync: connected to server {welcome.ServerVersion}; {welcome.Marks.Count} shared marks, {welcome.Clients.Count} online.");
        _trainSnapshotConnectionAt = _client.ConnectedAtUtc;
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
                        DeathObservedAtUtc = m.DeathAt, SnipedAtUtc = m.SnipedAt,
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
                    var untouched = !hasKnown || TrainMarkSignature.Create(local) == known.Signature;

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
                        local.SnipedAtUtc = m.SnipedAt;
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
                var canonical = new DetectedMark { Dead = m.Dead, DeathObservedAtUtc = m.DeathAt, SnipedAtUtc = m.SnipedAt,
                    Spiced = m.Spiced, Name = m.Name, ZoneName = m.ZoneName, IsCustom = m.IsCustom,
                    TerritoryId = m.TerritoryId, MapId = m.MapId, MapPosition = new Vector2(m.X, m.Y), LastSeenUtc = m.LastSeen };
                _known[key] = new KnownMark(TrainMarkSignature.Create(canonical), m.Revision, m.Dead);
            }
        }
        finally
        {
            _applying = false;
        }

        Bump();
    }

    /// <summary>
    /// Takes rows off this client's train because they went off the shared one.
    ///
    /// <paramref name="reported"/> says a train report has just been posted for
    /// them. That matters because report history retains removed dead marks on
    /// purpose - it is what stops "Remove Dead" losing kills from the next
    /// report - and retaining these would instead publish them twice, once now
    /// and again when whoever is still running the train finishes its remaining
    /// legs. So a reported removal forgets rather than retains.
    /// </summary>
    private void ApplyRemoves(List<SyncKey> keys, bool reported = false, List<ReportedMark>? reportedMarks = null)
    {
        _applying = true;
        try
        {
            var removed = keys.Select(k => k.ToTuple()).ToList();
            foreach (var key in removed) _known.Remove(key);

            // A reported removal has report history to settle as well as the
            // row itself, and that history belongs to the plugin rather than
            // here; an ordinary one only has to take the row off the list.
            if (reported) ReportedRemoval?.Invoke(reportedMarks ?? new());
            else foreach (var key in removed) _detector.Remove(key);
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
        if (s.HpPercent <= 0) { _remote.Remove(s.LiveKey); return; }

        // Our own reports come back to us too. They are already on the map
        // from the local scan, and once that expires the map should say the
        // mark is gone — not keep it lit on the strength of our own echo.


        var key = s.LiveKey;
        if (!_remote.TryGetValue(key, out var existing))
        {
            existing = new OtherRankSighting
            {
                EntityId = s.EntityId,
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
        var now = ServerTimeFor(DateTime.UtcNow);
        foreach (var key in _visibleMarks.Where(p => now - p.Value.Mark.SeenAt > RemoteSightingTtl).Select(p => p.Key).ToList())
            _visibleMarks.Remove(key);
        var stale = _remote.Where(kv => now - kv.Value.LastSeenUtc > RemoteSightingTtl).Select(kv => kv.Key).ToList();
        if (stale.Count == 0) return;
        foreach (var key in stale) _remote.Remove(key);
        Bump();
    }

    private void ForgetRemoteState()
    {
        _sightingClock.Reset();
        _trainSnapshotConnectionAt = null;
        SupportsTrainPresets = false;
        TrainPresets = new();
        PendingPresetRequest = null;
        AcceptedPresetRevision = null;
        CompletedPresetRequest = null;
        PresetStatus = "";
        ResetCompletionConnection();
        _visibleMarks.Clear(); _activeMarkGrace.Clear(); ClientId = string.Empty; SupportsVisibleMarks = false; SupportsManualMapping = false; SupportsScopedTrainWatches = false;
        _counterServerId = string.Empty; _counterReady = false; _sharedCounters.Clear();
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

    // Local -> remote

    private void DiffTrain()
    {
        if (!_config.SyncShareTrain || !HasCurrentTrainSnapshot) return;

        var watches = ReadLocalWatches();
        var watchJson = SyncProtocol.Serialize(watches);
        if (_watchSent is null && watchJson != _watchKnown)
        {
            _watchSent = watchJson;
            _client.Send(new WatchesMessage { BaseRevision = _watchRevision, Watches = watches });
        }
        var upserts = new List<SyncMark>();
        var newlyDead = new List<SyncKey>();
        var present = new HashSet<(uint, uint, uint)>();

        foreach (var mark in _detector.Marks.Values)
        {
            var key = mark.Key;
            present.Add(key);

            var signature = TrainMarkSignature.Create(mark);
            var hasKnown = _known.TryGetValue(key, out var known);
            if (hasKnown && known.Signature == signature) continue;

            upserts.Add(ToSyncMark(mark, hasKnown ? known.Revision : 0));
            if (mark.Dead && (!hasKnown || !known.Dead)) newlyDead.Add(SyncKey.From(key));
            _known[key] = new KnownMark(signature, hasKnown ? known.Revision : 0, mark.Dead);
        }

        var removed = _known.Keys.Where(k => !present.Contains(k)).ToList();
        foreach (var key in removed) _known.Remove(key);

        if (upserts.Count > 0) _client.Send(new TrainUpsertMessage { Marks = upserts, Scouting = !_config.ScanningPaused });
        if (removed.Count > 0) _client.Send(new TrainRemoveMessage { Keys = removed.Select(SyncKey.From).ToList() });
        if (newlyDead.Count > 0) _client.Send(new SightingsRemoveMessage { Keys = newlyDead });

        var order = _detector.Ordered().Select(m => m.Key).ToList();
        if (TrainPresets.ActivePresetId is null && !order.SequenceEqual(_lastSentOrder))
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
        try
        {
            RememberARankSightings(_detector.VisibleMarks.Select(s => new ARankSighting {
                NameId = s.NameId, WorldId = s.WorldId, Instance = s.Instance,
                At = s.LastSeenUtc, Alive = s.HealthPercent > 0 }));
            if (IsConnected) PublishSightings();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not share sightings.");
        }
    }

    private void PublishSightings()
    {
        var now = DateTime.UtcNow;
        var visible = _config.SyncShareSightings ? _detector.VisibleMarks.Where(s => !s.IsRemote
            && s.TerritoryId == _clientState.TerritoryType && s.Instance == MarkDetector.GetCurrentInstance()
            && s.WorldId == _detector.CurrentWorldId() && now - s.LastSeenUtc < TimeSpan.FromSeconds(1)).ToList()
            : new List<OtherRankSighting>();
        var changed = visible.Count != _sentSightings.Count || visible.Any(s =>
            !_sentSightings.TryGetValue(s.LiveKey, out var prev) || Math.Abs(prev.Hp - s.HealthPercent) >= 1f
            || Vector2.Distance(prev.Pos, s.MapPosition) >= 0.3f || prev.Point != s.SpawnPointIndex
            || prev.InCombat != s.InCombat || now - prev.At >= SightingHeartbeat);
        if (!changed) return;
        // Protocol 4: the complete visible set. Empty withdraws this player's observations only.
        var batch = visible.Select(s => new SyncSighting
        {
            EntityId = s.EntityId, NameId = s.NameId, Instance = s.Instance, WorldId = s.WorldId, Name = s.Name,
            Rank = SsEventMobs.Contains(s.NameId) ? "SS" : s.Rank.ToString(), TerritoryId = s.TerritoryId,
            MapId = s.MapId, X = s.MapPosition.X, Y = s.MapPosition.Y, HpPercent = s.HealthPercent, NearbyPlayers = s.NearbyPlayers, InCombat = s.InCombat,
            SeenAt = s.LastSeenUtc, SpawnPointIndex = s.SpawnPointIndex
        }).ToList();
        _client.Send(new SightingsMessage { Sightings = batch });
        _sentSightings.Clear();
        foreach (var s in visible) _sentSightings[s.LiveKey] = (s.HealthPercent, s.MapPosition, now, s.SpawnPointIndex, s.InCombat);
    }

    private void MaybeSendPresence()
    {
        var here = (_detector.CurrentWorldId(), _clientState.TerritoryType, MarkDetector.GetCurrentInstance());
        if (here == _lastPresence) return;
        _lastPresence = here;
        _client.Send(new PresenceUpdateMessage { WorldId = here.Item1, TerritoryId = here.Item2, Instance = here.Item3 });
    }

    // Kills, from the tally or by hand

    /// <summary>
    /// A mark died in front of this client. Any rank: its sighting comes
    /// off everyone's map. An S rank also starts the group's clock for it.
    /// </summary>
    public void ReportMarkDeath(uint nameId, uint instance, uint territoryId, DateTime killedAtUtc, bool isSRank)
    {
        if (SsEventMobs.Contains(nameId)) return; // Corpse snapshots identify the individual minion.
        if (!IsConnected || (!_config.SyncShareSightings && !_config.SyncReportSRankKills)) return;

        var world = _detector.CurrentWorldId();
        var key = (nameId, instance, world);

        _client.Send(new SightingsRemoveMessage { Keys = new() { SyncKey.From(key) } });
        foreach (var live in _sentSightings.Keys.Where(k => (k.NameId,k.Instance,k.WorldId) == key).ToList()) _sentSightings.Remove(live);

        if (!isSRank || !_config.SyncReportSRankKills || !SRankTimerData.IsTimerSRank(nameId)) return;
        if (_reportedKills.TryGetValue(key, out var last) && killedAtUtc - last < KillDedupe) return;
        _reportedKills[key] = killedAtUtc;

        float? x = null, y = null;
        int? point = null;
        var seen = _detector.OtherRanks.Values.Concat(_remote.Values).FirstOrDefault(s => s.Key == key);
        if (seen is not null)
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

    // Lookups for the UI and the map

    public SyncSpawnZone? ZoneFor(uint territoryId, uint worldId, uint instance) =>
        _zones.TryGetValue((territoryId, worldId, instance), out var z) ? z : null;

    public SyncSRankStatus? StatusFor(uint nameId, uint worldId, uint instance) =>
        _sranks.TryGetValue((nameId, instance, worldId), out var s) ? s : null;

    /// <summary>Whether anybody — this client or another — can see the mark right now.</summary>
    public bool IsSeenUp(uint nameId, uint worldId, uint instance)
    {
        var key = (nameId, instance, worldId);
        var now = DateTime.UtcNow;
        var serverNow = ServerTimeFor(now);
        var killed = StatusFor(nameId, worldId, instance)?.KilledAt;
        if (_detector.OtherRanks.TryGetValue((key.Item1,key.Item2,key.Item3,0,0), out var local) && ActiveSRankFilter.LivingObservation(
            local.HealthPercent, ServerTimeFor(local.LastSeenUtc), local.LastSeenUtc + RemoteSightingTtl, killed, now, serverNow)) return true;
        if (!IsConnected) return false;
        // Match Active Marks. Legacy sightings can outlive the newer observer removal
        // stream; a previous-cycle sighting must not override the new kill clock.
        if (SupportsVisibleMarks) return _activeMarkGrace.IsAlive(key, killed, now, serverNow);
        return _remote.TryGetValue((key.Item1,key.Item2,key.Item3,0,0), out var remote) && ActiveSRankFilter.LivingObservation(
            remote.HealthPercent, remote.LastSeenUtc, _sightingClock.ToLocal(remote.LastSeenUtc) + RemoteSightingTtl, killed, now, serverNow);
    }

    public void SetManualMapping(uint territory, uint world, uint instance, int index, bool exclude, string source = "Manual")
    {
        if (!IsConnected || !SupportsManualMapping) return;
        LastError = string.Empty;
        _client.Send(new ManualMappingMessage { TerritoryId=territory, WorldId=world, Instance=instance,
            Index=index, Exclude=exclude, Source=source, ExpectedSinceAt=ZoneFor(territory,world,instance)?.SinceAt });
    }

    public string DisplayName()
    {
        var chosen = _config.SyncDisplayName?.Trim();
        if (!string.IsNullOrEmpty(chosen)) return chosen;

        return "Anonymous";
    }

    // Helpers

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
        SnipedAt = m.SnipedAtUtc is { } sniped ? AsUtc(sniped) : null,
        IsCustom = m.IsCustom,
        ZoneName = m.ZoneName,
        Spiced = m.Spiced,
        BaseRevision = baseRevision,
    };

    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
