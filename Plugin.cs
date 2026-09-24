using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Textures;
using KamiToolKit;
using Dalamud.Plugin;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using HuntTally;
using HuntTally.Windows;
using HuntHelperEvolved.Sync;

namespace HuntHelperEvolved;

public sealed partial class Plugin : IDalamudPlugin
{
    public string Name => "Hunt Helper Evolved";

    private const string ConfigCommand = "/htr";
    private const string TrainCommand = "/htrt";
    private const string CounterCommand = "/htrc";
    private const string NextAetheryteCommand = "/htra";
    private const string MapCommand = "/htrm";
    private const string SRankCommand = "/htrs";

    /// <summary>
    /// The tally's original command, kept verbatim. It was a separate plugin
    /// until this release and people have it in macros and muscle memory, so
    /// merging must not be the thing that breaks it.
    /// </summary>
    private const string TallyCommand = "/hunttally";
    private const int MaxWebhooks = 5;
    private const int MaxAdditionalScouts = 3;

    // The only S-ranks the group actually checks for during trains.
    /// <summary>
    /// The S ranks a train actually stops for, and the zone each is watched in.
    ///
    /// Deliberately not every S rank in the game: a conductor checks a handful
    /// on the way past, and a list of fifty to scroll through would be a worse
    /// answer to the same question. Narrow-rift is absent because it needs a
    /// spawn point picking as well, and has its own control below.
    ///
    /// Territory ids are the same ones the rest of the plugin uses, cross-
    /// checked against SsMinionSpawns rather than typed from memory.
    /// </summary>
    private static readonly (string Name, uint TerritoryId)[] SimpleSRanks =
    {
        ("Ophioneus", 961),      // Elpis
        ("Tyger", 813),          // Lakeland
        ("Neyoozoteel", 1189),   // Yak T'el
    };

    // Narrow-rift's known spawn points (Territory 960 / Map 699, Ultima Thule —
    // confirmed via arealmremapped.com; coordinates from Narrow-rift's own
    // Coordinates table on ffxiv.consolegameswiki.com). Used only to label which
    // spot is being watched — no location system attached anymore, just text.
    private static readonly (float X, float Y)[] NarrowRiftSpawns =
    {
        (8.3f, 20.2f), (12.0f, 21.9f), (13.3f, 10.4f), (14.7f, 36.1f), (16.5f, 26.2f),
        (17.6f, 30.3f), (19.2f, 9.8f), (20.7f, 34.0f), (27.9f, 12.6f),
    };

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;
    private readonly SRankZoneReminder _zoneReminder;
    private readonly MarkDetector _detector;
    private readonly TeleportHelper _teleport;
    private readonly IGameGui _gameGui;
    private readonly ITextureProvider _textureProvider;
    private readonly MarkNotifier _notifier;

    // Asked for once; enumerating voices constructs a synthesiser.
    private string[]? _voices;

    // The game's own aetheryte crystal icon. ID confirmed from Umbra's source
    // (Umbra.Game/src/Travel/TravelDestination.cs: IconAetheryte = 60453).
    private const uint AetheryteIconId = 60453;
    private readonly HuntCounter _counter;
    private readonly SpawnWatchCounters _spawnWatch;
    private readonly TrainIpcProvider _trainIpc;
    private readonly WorldData _worldData;
    private readonly HuntMapOverlay _mapOverlay;
    private readonly SsEventWatcher _ssEvent;

    // Sharing with a group through their own server. See Sync/.
    private readonly SyncCoordinator _sync;
    private readonly SRankWindow _srankWindow;
    private bool _selectSyncTab;
    private readonly ActiveMarksWindow _activeMarksWindow;
    private readonly LifestreamTravel _srankTravel;
    private readonly ARankWindow _arankWindow;
    private int _counterDcIndex;
    private int _counterWorldIndex;

    // The world the picker last auto-followed. A manual selection sticks until
    // the player's world actually changes — otherwise deliberately looking at
    // another world's counts would be yanked back every frame.
    private uint _lastSeenWorldId;
    private double _secondsSinceAutoResetCheck;
    private readonly IClientState _clientState;

    private uint _clientTerritory => _clientState.TerritoryType;

    private readonly Configuration _config;
    private readonly TrainWatcher _watcher;

    // --- The tally, formerly the separate Hunt Tally plugin ---
    //
    // Its configuration is deliberately NOT this plugin's. It stays in
    // HuntTally.json where the standalone plugin left it, so upgrading to the
    // merged build keeps every existing kill count. See TallyConfigStore.
    private readonly HuntTally.Configuration _tallyConfig;
    private readonly WindowSystem _tallyWindows = new("HuntTally");
    private readonly MainWindow _tallyWindow;
    private readonly TallySettingsPanel _tallySettings;
    private readonly KillTracker _tracker;
    private readonly AchievementSeeder _seeder;
    private readonly CharacterContext _characters;
    private readonly DamageWatch _damage;
    private readonly RewardWatch _reward;
    private readonly IpcProvider _tallyIpc;
    private readonly CancellationTokenSource _disposal = new();

    /// <summary>
    /// Achievement data is not ready the instant the login event fires, and the
    /// seeder times out per request rather than hanging, so a late start is
    /// safer than an early one.
    /// </summary>
    private static readonly TimeSpan LoginSeedDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Set when something asks for the Tally tab specifically — "/hunttally
    /// config", which used to open the tally's own settings window. Consumed by
    /// the tab on the next frame it draws.
    /// </summary>
    private bool _selectTallyTab;

    /// <summary>
    /// The release notes window. Its own window rather than a tab because it
    /// arrives unasked after an update — putting it in front of someone means
    /// showing it, not selecting a tab behind whatever they had open.
    /// </summary>
    private bool _releaseNotesVisible;
    private bool _releaseNotesChecked;

    /// <summary>
    /// True when the standalone Hunt Tally plugin is also loaded, which the
    /// merged build has to treat as an error rather than a duplicate.
    /// </summary>
    private bool _standaloneTallyPresent;

    private bool _configWindowVisible;
    private bool _trainPopoutVisible;
    private bool _counterPopoutVisible;
    private string _customFlagLabel = string.Empty;

    // Custom flags removed a few seconds after teleporting to them — instant
    // removal was jarring mid-click.
    private const double CustomFlagRemovalDelaySeconds = 5;
    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), DateTime> _pendingCustomRemovals = new();
    private int _blacklistExpansion;
    private int _blacklistZone;
    private int _blacklistAetheryte;

    // Drag state for the train list. Both are -1 when no drag is in progress.
    private int _dragFromIndex = -1;
    private int _dragToIndex = -1;
    private (uint NameId, uint Instance, uint WorldId)? _dragMarkKey;
    private (uint NameId, uint Instance, uint WorldId)? _dragTargetKey;

    // The same, for dragging whole expansion blocks around when the train is
    // grouped. Kept separate from the mark drag rather than overloaded onto it:
    // the two mean different things (one moves a row, one moves a block), and
    // sharing the fields would let a half-finished block drag be committed as a
    // mark move. Indices are into the expansions actually present in the list.
    private int _dragExpansionFrom = -1;
    private int _dragExpansionTo = -1;
    private (uint WorldId, string Expansion)? _dragExpansionBlock;
    private (uint WorldId, string Expansion)? _dragExpansionTargetBlock;

    private readonly TrainExpansionProgress _expansionProgress = new();

    // The mark the conductor is currently on. Tracked by identity rather than
    // list position, so dragging rows or removing marks can't silently change
    // what "current" points at.
    // Keyed the same way marks are, world included — the same mark on two
    // worlds is two marks, and the pointer has to say which.
    private (uint NameId, uint Instance, uint WorldId)? _currentMark;

    private string _trainResult = string.Empty;
    private bool _trainResultReportsOnly;
    private string _lastPostResult
    {
        get => _trainResult;
        set
        {
            _trainResult = value;
            _trainResultReportsOnly = false;
        }
    }
    private bool _reportPostBusy;
    private int _selectedNarrowRiftSpawn;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        ICommandManager commandManager,
        IChatGui chatGui,
        IObjectTable objectTable,
        IClientState clientState,
        IDataManager dataManager,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        IAddonLifecycle addonLifecycle,
        IFlyTextGui flyTextGui,
        IFateTable fateTable,
        IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _framework = framework;
        _commandManager = commandManager;
        _chatGui = chatGui;
        _objectTable = objectTable;
        _log = pluginLog;

        var startup = new StartupTransaction();
        startup.Add(_disposal.Dispose);
        try
        {
            // The LoadDirect step is not redundant. When Dalamud's loader resolves
            // the file's "$type" to a previous, not-yet-collected copy of this
            // assembly — which is what a dev-plugin reload does — the cast above
            // fails silently and the old code went straight to a fresh
            // Configuration, whose first Save wiped every setting on disk. Reading
            // the file ourselves has no assembly resolution to get wrong.
            // Third in the chain is the rename hand-over: this plugin was Hunt
            // Train Relay, and Dalamud names a config file after the InternalName,
            // so everything a returning user had configured is sitting under the
            // old name. It is only read when there is no file under the new one.
            var loaded = Configuration.LoadDirect(_pluginInterface)
                         ?? _pluginInterface.GetPluginConfig() as Configuration;

            var migrated = false;
            if (loaded is null)
            {
                loaded = Configuration.LoadFromPreviousName(_pluginInterface);
                migrated = loaded is not null;
            }

            _config = loaded ?? new Configuration();
            _config.ScanningPaused = true;
            startup.Add(() => { clientState.Login -= PauseScoutingOnLogin; });
            clientState.Login += PauseScoutingOnLogin;
            _config.Initialize(_pluginInterface);

            if (migrated)
            {
                // Write it out under the new name straight away, so the hand-over
                // happens once rather than on every load until something else
                // saves.
                _config.Save();
                _log.Information("Carried settings over from the Hunt Train Relay config file.");
            }

            _gameGui = gameGui;
            _textureProvider = textureProvider;
            _clientState = clientState;
            _detector = new MarkDetector(objectTable, clientState, dataManager, _config);
            _teleport = new TeleportHelper(_pluginInterface, _log, dataManager);
            SyncBlacklist();
            _watcher = new TrainWatcher(framework, _detector, _config, chatGui, _log);
            startup.Add(_watcher.Dispose);

            // The tally reaches Dalamud through its own injected service class
            // rather than this constructor's parameters, which is how it was built
            // as a standalone plugin. Left that way on purpose: it keeps the merge
            // to a wiring change, so the counting code that people's existing
            // totals were built by is the same code, untouched.
            _pluginInterface.Create<HuntTally.Service>();
            DetectStandaloneTally();
            _tallyConfig = TallyConfigStore.Load(_pluginInterface);

            _characters = new CharacterContext(_tallyConfig);
            startup.Add(_characters.Dispose);
            _seeder = new AchievementSeeder(_tallyConfig, _characters);
            startup.Add(_seeder.Dispose);

            // Constructed before the tracker: the tracker asks it on every poll
            // whether the precise signal is available.
            _damage = new DamageWatch();
            startup.Add(_damage.Dispose);
            _reward = new RewardWatch();
            startup.Add(_reward.Dispose);

            _tallyWindow = new MainWindow(_tallyConfig, _characters);
            startup.Add(_tallyWindow.Dispose);
            startup.Add(_tallyWindows.RemoveAllWindows);
            _tallyWindows.AddWindow(_tallyWindow);

            _tracker = new KillTracker(_tallyConfig, _characters, _damage, _reward);
            startup.Add(_tracker.Dispose);
            _tallySettings = new TallySettingsPanel(
                _tallyConfig, _seeder, _characters, _damage, _reward, _tracker);
            _tallyIpc = new IpcProvider(_tallyConfig);
            startup.Add(_tallyIpc.Dispose);

            if (_standaloneTallyPresent)
            {
                // Unhooks the tracker's framework and territory events, so it never
                // polls and nothing is counted. Standing the tally down has to mean
                // this and not just declining to save: left running it would print a
                // second kill line for every mark alongside the standalone plugin's,
                // and show totals in its window that were never going to be kept.
                //
                // Disposing again in Dispose is harmless — it only unsubscribes.
                _tracker.Dispose();
            }
            else
            {
                // Subscribed separately from the chat notice: a kill should reach
                // the train and any other listening plugin whether or not the user
                // wants it printed.
                startup.Add(() => { _tracker.OnKill -= _tallyIpc.PublishCredited; });
                _tracker.OnKill += _tallyIpc.PublishCredited;
                startup.Add(() => { _tracker.OnMarkDeath -= _tallyIpc.PublishMarkDeath; });
                _tracker.OnMarkDeath += _tallyIpc.PublishMarkDeath;
                startup.Add(() => { _tracker.OnKill -= AnnounceTallyKill; });
                _tracker.OnKill += AnnounceTallyKill;

                // Every death the tally sees, credited or not. An S rank dying in
                // front of anyone in the group is how the group's clock for it
                // starts.
                startup.Add(() => { _tracker.OnMarkDeath -= OnAnyMarkDeath; });
                _tracker.OnMarkDeath += OnAnyMarkDeath;

                // Off the publisher rather than the tracker, so the train sees
                // exactly the feed an external subscriber would have seen over IPC —
                // including the tally's own switch between credited kills and every
                // mark death. That is what the auto-mark behaviour was built
                // against, and keeping the same source is what stops the merge
                // quietly changing it.
                startup.Add(() => { _tallyIpc.KillPublished -= OnTallyKillPublished; });
                _tallyIpc.KillPublished += OnTallyKillPublished;

                startup.Add(() => { HuntTally.Service.ClientState.Login -= OnTallyLogin; });
                HuntTally.Service.ClientState.Login += OnTallyLogin;
                startup.Add(() => { HuntTally.Service.ClientState.Logout -= OnTallyLogout; });
                HuntTally.Service.ClientState.Logout += OnTallyLogout;
                startup.Add(() => { HuntTally.Service.Framework.Update -= OnTallyFrameworkUpdate; });
                HuntTally.Service.Framework.Update += OnTallyFrameworkUpdate;

                // Resolving walks the whole Achievement sheet. One tick later costs
                // nothing and keeps it off the plugin-load path.
                HuntTally.Service.Framework.RunOnTick(
                    _seeder.ResolveAll, TimeSpan.Zero, 0, _disposal.Token);

                if (HuntTally.Service.ClientState.IsLoggedIn && _tallyConfig.AutoSeedOnLogin)
                    ScheduleTallySeed();
            }

            _notifier = new MarkNotifier(chatGui, flyTextGui, _log, _config);
            startup.Add(_notifier.Dispose);
            _zoneReminder = new SRankZoneReminder(clientState, chatGui, _log, _config, _detector, framework, AutomaticWatchAvailable);
            startup.Add(_zoneReminder.Dispose);
            _counter = new HuntCounter(chatGui, clientState, objectTable, _config);
            startup.Add(_counter.Dispose);
            _spawnWatch = new SpawnWatchCounters(framework, clientState, objectTable, fateTable, _log, HuntTally.Service.Condition);
            startup.Add(_spawnWatch.Dispose);
            _worldData = new WorldData(dataManager);

            // Built before the map overlay, which draws what other members can
            // see. Connects straight away if sync is on in the saved settings.
            _sync = new SyncCoordinator(
                framework, clientState, objectTable, _log, _config, _detector, _worldData,
                typeof(Plugin).Assembly.GetName().Version?.ToString(4) ?? "0.0.0");
            startup.Add(_sync.Dispose);
            startup.Add(() => { _counter.PersonalKill -= _sync.RecordCounterKill; });
            _counter.PersonalKill += _sync.RecordCounterKill;
            startup.Add(() => { _sync.RemoteTrainCleared -= OnRemoteTrainCleared; });
            _sync.RemoteTrainCleared += OnRemoteTrainCleared;
            startup.Add(() => { _sync.ReportedRemoval -= OnReportedRemoval; });
            _sync.ReportedRemoval += OnReportedRemoval;
            startup.Add(() => { _sync.SRankSpawned -= OnRemoteSRankSpawn; });
            _sync.SRankSpawned += OnRemoteSRankSpawn;
            _srankTravel = new LifestreamTravel(_pluginInterface, framework, _detector, _chatGui, _log);
            startup.Add(_srankTravel.Dispose);
            _activeMarksWindow = new ActiveMarksWindow(_config, _sync, _worldData, _detector, _gameGui, _srankTravel, () => { _configWindowVisible=true; _selectActiveMarksSettings=true; });
            _srankWindow = new SRankWindow(_config, _sync, _worldData, _detector, _srankTravel);
            _arankWindow = new ARankWindow(_config, _sync, _worldData, _detector);
            // Publish framework-thread snapshots after the detector exists.
            _trainIpc = new TrainIpcProvider(_pluginInterface, _framework, _detector, _log);
            startup.Add(_trainIpc.Dispose);

            // KamiToolKit needs one-time initialisation before any of its
            // controllers can be enabled — without it, AddonController.Enable()
            // throws a null reference on every frame.
            startup.Add(KamiToolKitLibrary.Dispose);
            KamiToolKitLibrary.Initialize(_pluginInterface, "Hunt Helper Evolved");

            _ssEvent = new SsEventWatcher(chatGui, clientState, _log, _detector);
            startup.Add(_ssEvent.Dispose);
            _mapOverlay = new HuntMapOverlay(framework, clientState, objectTable, dataManager, addonLifecycle, gameGui, _log, _config, _detector, _ssEvent, _pluginInterface, _sync);
            startup.Add(_mapOverlay.Dispose);
            startup.Add(() => { _detector.OtherRankDetected -= OnSightingDetected; });
            _detector.OtherRankDetected += OnSightingDetected;
            startup.Add(() => { _clientState.TerritoryChanged -= _detector.ResetAnnouncements; });
            _clientState.TerritoryChanged += _detector.ResetAnnouncements;
            startup.Add(() => { _watcher.PersistRequested -= PersistTrain; });
            _watcher.PersistRequested += PersistTrain;
            RestoreSavedTrain();
            startup.Add(() => { _clientState.Logout -= OnPluginLogout; });
            _clientState.Logout += OnPluginLogout;

            startup.Add(UnregisterCommands);
            RegisterCommands();
            startup.Add(() => { _framework.Update -= OnPluginFrameworkUpdate; });
            _framework.Update += OnPluginFrameworkUpdate;

            startup.Add(() => { _pluginInterface.UiBuilder.Draw -= DrawUI; });
            _pluginInterface.UiBuilder.Draw += DrawUI;
            startup.Add(() => { _pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi; });
            _pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;

            // The tally's display window is the plugin's "main" UI, as it was when
            // the tally was its own plugin. The gear opens settings, which are now
            // a tab of this plugin's config window.
            startup.Add(() => { _pluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow; });
            _pluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
            startup.Commit();
        }
        catch (Exception ex)
        {
            _disposed = true;
            startup.Add(_disposal.Cancel);
            throw startup.Rollback(ex);
        }
    }

    // Tally

    private void OpenMainWindow() => _configWindowVisible = true;

    private void ToggleTallyWindow() => _tallyWindow.Toggle();

    /// <summary>
    /// Notices the standalone Hunt Tally plugin still being installed, and
    /// stands the built-in tally down if it is.
    ///
    /// Both would otherwise count the same kills into the same file on their
    /// own save timers, each overwriting whatever the other had written since
    /// it last read — which loses counts rather than merely duplicating them.
    /// So the file is left entirely to the plugin that has been keeping it.
    ///
    /// Detection is by asking its version gate before we register our own copy
    /// of that gate: an answer means somebody else is already providing it.
    /// This has to run before the IpcProvider is constructed, or the question
    /// would be answered by us.
    /// </summary>
    private void DetectStandaloneTally()
    {
        try
        {
            _pluginInterface.GetIpcSubscriber<int>("HuntTally.ApiVersion").InvokeFunc();
        }
        catch
        {
            // Nothing answered, which is the normal case: the tally is ours.
            return;
        }

        _standaloneTallyPresent = true;

        TallyConfigStore.SuspendWrites(
            "the standalone Hunt Tally plugin is still installed and is keeping that file.");

        _chatGui.PrintError(
            "[Hunt Helper Evolved] Hunt Tally is now built in, but the separate Hunt Tally "
            + "plugin is still installed. Nothing is being counted here and your tally file "
            + "is untouched — uninstall the separate plugin, then reload this one.");
    }

    /// <summary>
    /// The tally's original command, behaving as it always did. "config" now
    /// lands on the Tally tab of this plugin's config window rather than
    /// opening a second settings window of its own.
    /// </summary>
    private void OnTallyCommand(string command, string args)
    {
        var arg = args.Trim();

        if (arg.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            _configWindowVisible = true;
            _selectTallyTab = true;
        }
        else if (arg.Equals("ipc", StringComparison.OrdinalIgnoreCase))
        {
            _chatGui.Print($"[Hunt Tally] {_tallyIpc.ToggleEcho()}");
        }
        else
        {
            ToggleTallyWindow();
        }
    }

    /// <summary>Writes queued tally changes, at the interval Flush enforces.</summary>
    private void OnTallyFrameworkUpdate(IFramework framework) { if (!_disposed) _tallyConfig.Flush(); }

    private void OnPluginFrameworkUpdate(IFramework framework)
    {
        if (_disposed) return;
        if (_commandHelpDirty) RefreshCommandHelp();
        try { _config.Flush(); }
        catch (Exception ex) { _log.Error(ex, "Could not save configuration; will retry."); }
        if (!TrainMutationBusy)
        {
            UpdateAutomaticTrainWatches();
            ApplyLocalTrainPreset();
        }
        UpdateTrainReportPreview();
        if (_clientState.IsLoggedIn)
        {
            _secondsSinceAutoResetCheck += framework.UpdateDelta.TotalSeconds;
            if (_secondsSinceAutoResetCheck >= 30)
            {
                _secondsSinceAutoResetCheck = 0;
                _counter.ApplyAutoResets();
            }
            ProcessPendingCustomRemovals();
            UpdateAutoAdvance();
            DrainPendingSpawnAlerts();
        }
        // During DC transfers the game hides its UI at character selection.
        // Only Active Marks is drawn there; normal hide preferences apply in game.
        _pluginInterface.UiBuilder.DisableUserUiHide = _releaseNotesChecked
            && !_clientState.IsLoggedIn && _config.ActiveSRankWindowOpen;
    }

    private void PauseScoutingOnLogin() => _config.ScanningPaused = true;

    private void OnTallyLogin()
    {
        if (_tallyConfig.AutoSeedOnLogin)
            ScheduleTallySeed();
    }

    private void OnTallyLogout(int type, int code)
    {
        try { _tallyConfig.Flush(force: true); }
        catch (Exception ex) { _log.Error(ex, "Could not save the tally at logout."); }
    }

    private void OnPluginLogout(int type, int code)
    {
        _config.ScanningPaused = true;
        _detector.ClearNearbyPlayers();
        try { PersistTrain(); }
        catch (Exception ex) { _log.Error(ex, "Could not capture the train at logout."); }
        try { _config.Flush(force: true); }
        catch (Exception ex) { _log.Error(ex, "Could not save configuration at logout; will retry."); }
    }

    /// <summary>
    /// RunOnTick rather than Task.Delay: the continuation of a Task runs on a
    /// thread-pool thread, and the seeder's state is read on the framework
    /// thread. It is also cancelled on dispose, so a plugin unloaded inside the
    /// delay does not start seeding afterwards.
    /// </summary>
    private void ScheduleTallySeed() =>
        _framework.RunOnTick(_seeder.Start, LoginSeedDelay, 0, _disposal.Token);

    private void AnnounceTallyKill(KillDetail kill)
    {
        if (!_tallyConfig.ChatOnKill)
            return;

        var info = kill.Mark;

        var profile = _characters.Current;
        if (profile is null)
            return;

        var key = HuntTally.Configuration.CategoryKeyFor(info.Rank);
        if (key is null)
            return;

        _chatGui.Print(
            $"[Hunt Tally] {info.Name} ({MarkData.RankLabel(info.Rank)}) — "
            + $"{profile.TotalFor(key)} {key} ranks on this character, "
            + $"{_tallyConfig.AccountTotalFor(key)} across all.");
    }

    /// <summary>
    /// Hands a counted kill to the train watcher, flattened to the same shape
    /// it used to arrive in over IPC.
    /// </summary>
    private void OnTallyKillPublished(KillDetail kill)
    {
        _watcher.OnHuntTallyKill(new HuntTallyKill(
            kill.Mark.Name,
            kill.Mark.NameId,
            (int)kill.Mark.Rank,
            kill.TerritoryId,
            kill.InstanceId,
            new DateTimeOffset(kill.Time).ToUnixTimeSeconds(), _worldData.IdOf(kill.World)));
    }

    /// <summary>
    /// Hunt Helper's Notifications tab: three channels, each with its own
    /// per-rank switches and its own message.
    ///
    /// Laid out by channel rather than by rank — Hunt Helper puts a tab per
    /// rank and repeats all three channels inside each, which means changing
    /// "how loud is chat" is three tabs' worth of clicking. The settings
    /// themselves are the same ones under the same names, so a message pasted
    /// across from it behaves identically.
    /// </summary>
    private void DrawDetectionNotificationSettings()
    {
        DrawSettingsHeading("Local detection");
        ImGui.Spacing();

        // ---- Chat ----
        var chat = _config.EchoOnDetection;
        if (ImGui.Checkbox("Announce in chat", ref chat))
        {
            _config.EchoOnDetection = chat;
            _config.Save();
        }

        if (_config.EchoOnDetection)
        {
            ImGui.Indent();

            var cB = _config.EchoBRanks;
            if (ImGui.Checkbox("B##chatrank", ref cB)) { _config.EchoBRanks = cB; _config.Save(); }
            ImGui.SameLine();
            var cA = _config.EchoARanks;
            if (ImGui.Checkbox("A##chatrank", ref cA)) { _config.EchoARanks = cA; _config.Save(); }
            ImGui.SameLine();
            var cS = _config.EchoSRanks;
            if (ImGui.Checkbox("S##chatrank", ref cS)) { _config.EchoSRanks = cS; _config.Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled("which ranks");

            if (ImGui.TreeNode("Chat message templates"))
            {
                DrawMessageBox("B message##chat", _config.DetectionChatMessageB, v => _config.DetectionChatMessageB = v);
                DrawMessageBox("A message##chat", _config.DetectionChatMessageA, v => _config.DetectionChatMessageA = v);
                DrawMessageBox("S message##chat", _config.DetectionChatMessageS, v => _config.DetectionChatMessageS = v);

                ImGui.TreePop();
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // ---- Fly text ----
        var fly = _config.DetectionFlyTextEnabled;
        if (ImGui.Checkbox("Show fly text", ref fly))
        {
            _config.DetectionFlyTextEnabled = fly;
            _config.Save();
        }

        if (_config.DetectionFlyTextEnabled)
        {
            ImGui.Indent();
            var fB = _config.FlyTextBRanks;
            if (ImGui.Checkbox("B##flyrank", ref fB)) { _config.FlyTextBRanks = fB; _config.Save(); }
            ImGui.SameLine();
            var fA = _config.FlyTextARanks;
            if (ImGui.Checkbox("A##flyrank", ref fA)) { _config.FlyTextARanks = fA; _config.Save(); }
            ImGui.SameLine();
            var fS = _config.FlyTextSRanks;
            if (ImGui.Checkbox("S##flyrank", ref fS)) { _config.FlyTextSRanks = fS; _config.Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled("which ranks");
            ImGui.TextDisabled("The rank and name use fixed chat colours.");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // ---- Speech ----
        var tts = _config.DetectionTtsEnabled;
        if (ImGui.Checkbox("Speak detections", ref tts))
        {
            _config.DetectionTtsEnabled = tts;
            _config.Save();
        }

        if (_config.DetectionTtsEnabled)
        {
            ImGui.Indent();

            var tB = _config.TtsBRanks;
            if (ImGui.Checkbox("B##ttsrank", ref tB)) { _config.TtsBRanks = tB; _config.Save(); }
            ImGui.SameLine();
            var tA = _config.TtsARanks;
            if (ImGui.Checkbox("A##ttsrank", ref tA)) { _config.TtsARanks = tA; _config.Save(); }
            ImGui.SameLine();
            var tS = _config.TtsSRanks;
            if (ImGui.Checkbox("S##ttsrank", ref tS)) { _config.TtsSRanks = tS; _config.Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled("which ranks");

            if (ImGui.TreeNode("Spoken message templates"))
            {
                DrawMessageBox("B message##tts", _config.DetectionTtsMessageB, v => _config.DetectionTtsMessageB = v);
                DrawMessageBox("A message##tts", _config.DetectionTtsMessageA, v => _config.DetectionTtsMessageA = v);
                DrawMessageBox("S message##tts", _config.DetectionTtsMessageS, v => _config.DetectionTtsMessageS = v);

                ImGui.TreePop();
            }
            DrawVoicePicker();
            ImGui.Unindent();
        }
    }

    /// <summary>One message template, saved when the box is left rather than per keystroke.</summary>
    private void DrawMessageBox(string label, string current, Action<string> apply)
    {
        var value = current;
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputText(label, ref value, 512))
            apply(value);

        if (ImGui.IsItemDeactivatedAfterEdit())
            _config.Save();
    }

    private void DrawVoicePicker()
    {
        _voices ??= _notifier.InstalledVoices();

        if (_voices.Length == 0)
        {
            ImGui.TextDisabled(_notifier.SpeechStatus);
            ImGui.TextDisabled("Chat and fly text are unaffected.");
            return;
        }

        var index = Array.IndexOf(_voices, _config.TtsVoiceName);
        if (index < 0) index = 0;

        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Voice", ref index, _voices, _voices.Length))
        {
            _config.TtsVoiceName = _voices[index];
            _config.Save();
        }

        var volume = _config.TtsVolume;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderInt("Volume", ref volume, 0, 100))
        {
            _config.TtsVolume = Math.Clamp(volume, 0, 100);
            _config.Save();
        }

        if (ImGui.Button("Test"))
            _notifier.Speak("A-Rank Nearby");
    }

    // A command that names a window toggles it. Typing it again to put the
    // window away is what everyone expects, it is what /htrm and /hunttally
    // already did, and it is what Hunt Helper's own commands do — so the /hh
    // aliases would otherwise have been a one-way door.
    //
    // OnOpenConfigUi below is deliberately not one of these: that is Dalamud's
    // own settings button, which has to mean open.
    private void OnCommand(string command, string args) => _configWindowVisible = !_configWindowVisible;

    private void OnTrainCommand(string command, string args) => _trainPopoutVisible = !_trainPopoutVisible;

    private void OnSightingDetected(OtherRankSighting sighting)
    {
        if (sighting.Rank == HuntRank.S && !sighting.IsRemote)
            _spawnAlertFilter.RecordLocal(sighting.NameId,sighting.WorldId,sighting.Instance,DateTime.UtcNow);
        if (_detector.ShouldAnnounce(sighting)) _notifier.Announce(sighting);
    }

    /// <summary>Compact "how long ago was this last seen" label, e.g. 5m / 1h 12m.</summary>
    private static string FormatAge(DateTime lastSeenUtc)
    {
        var age = DateTime.UtcNow - lastSeenUtc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalHours}h {age.Minutes}m";
    }

    private void OnCounterCommand(string command, string args) => _counterPopoutVisible = !_counterPopoutVisible;

    /// <summary>
    /// The map controls live on a bar pinned to the map itself now, rather than
    /// in a window of their own, so this toggles whether that bar is shown at
    /// all. Kept as a command because it was one.
    /// </summary>
    private void OnMapCommand(string command, string args)
    {
        _config.ShowMapControlBar = !_config.ShowMapControlBar;
        _config.Save();
        _chatGui.Print($"[Hunt Helper Evolved] Map control bar {(_config.ShowMapControlBar ? "shown" : "hidden")}.");
    }

    private void OnOpenConfigUi() => _configWindowVisible = true;

    /// <summary>Native report history, including removed dead rows and full world identity.</summary>
    private List<TrackedMark> BuildCurrentMarks() => _watcher.GetTrackedSnapshot().Values.ToList();

    private Task SendTestAsync() => SendReportAsync(
        (webhooks, token) => DiscordRelay.PostTestAsync(webhooks, token));

    private static List<NativeTrainRecord> ScoutRecords(IEnumerable<DetectedMark> marks) => marks.Where(m => !m.IsCustom)
        .Select(m => new NativeTrainRecord(m.Name, m.NameId, m.TerritoryId, m.MapId, m.Instance,
            m.WorldId, m.WorldName, m.MapPosition, m.Dead, m.LastSeenUtc, m.DeathObservedAtUtc, m.SnipedAtUtc)).ToList();

    private IReadOnlyDictionary<uint, int>? ScoutZoneInstanceCounts =>
        _sync.Faloop.MetadataAt is not null ? _sync.Faloop.ZoneInstances : null;

    private Task SendScoutingReportAsync()
    {
        ScoutNoteDraft.ReportCapture? submittedNote = null;
        return SendReportAsync((webhooks, token) =>
        {
            // Snapshot/export preparation stays inside the busy guard and its
            // exception handler; a draw caller never receives a synchronous throw.
            var marks = _detector.Ordered();
            var names = CombinedTrainScouts();
            var ownCode = TrainExchange.Export(marks);
            submittedNote = _scoutNote.CaptureForReport(_detector.TrainGeneration);
            var prepared = DiscordRelay.PrepareScoutingReport(ScoutRecords(marks), names,
                ownCode, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), submittedNote.Text, ScoutZoneInstanceCounts);
            _trainCompletionReport = false;
            SetTrainReportPreview(prepared);
            return DiscordRelay.PostPreparedReportAsync(webhooks, prepared, token);
        }, () =>
        {
            if (submittedNote is not null)
                _scoutNote.CompleteReport(submittedNote, true, _detector.TrainGeneration);
            _trainPreviewFingerprint = null;
        });
    }

    private async Task SendReportAsync(
        Func<List<WebhookEntry>, CancellationToken, Task<(bool Success, string Message)>> post, Action? onSuccess = null)
    {
        if (_disposed) return;
        if (_reportPostBusy || _completion.IsBusy)
        {
            SetReportPostResult("A Discord report is already being sent.");
            return;
        }
        _reportPostBusy = true;
        var token = _disposal.Token;
        try
        {
            var webhooks = _config.Webhooks.Select(w => new WebhookEntry
                { Enabled = w.Enabled, Label = w.Label, Url = w.Url }).ToList();
            var (success, message) = await post(webhooks, token);
            if (token.IsCancellationRequested) return;
            await _framework.RunOnFrameworkThread(() =>
            {
                if (_disposed) return;
                SetReportPostResult(message);
                if (success) onSuccess?.Invoke();
                if (!success) _log.Error($"Hunt Helper Evolved Discord report failed: {message}");
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.Error(ex, "Discord report failed.");
            if (!token.IsCancellationRequested)
                await _framework.RunOnFrameworkThread(() =>
                {
                    if (!_disposed) SetReportPostResult("Report failed. See the plugin log.");
                });
        }
        finally
        {
            if (!token.IsCancellationRequested)
                await _framework.RunOnFrameworkThread(() => _reportPostBusy = false);
        }
    }

    private void SetReportPostResult(string message)
    {
        _lastPostResult = message;
        _trainResultReportsOnly = true;
    }

    private readonly TrainCompletionGuard _completion = new();

    private string CompletionSnapshot() => TrainCompletionSnapshot.Create(
        _detector.TrainGeneration, _detector.Ordered(), BuildCurrentMarks(), _config.Flags,
        _config.SyncEnabled && _config.SyncShareTrain);

    /// <summary>
    /// Whether partial-report support is available. Shared submission is refused
    /// before posting Discord when the server lacks durable completion support.
    /// </summary>
    private bool CanReportPartially =>
        !_config.SyncEnabled || !_config.SyncShareTrain || _sync.SupportsPartialFinish;

    /// <summary>
    /// The only way a "Train Complete" report ever gets posted — reads the
    /// current merged mark set and posts it sorted by the actual order things
    /// died, plus any S-rank check results. Tracking and the watch list only
    /// clear once the post is confirmed to have actually succeeded — if it
    /// fails, everything stays put so this can just be tried again.
    /// </summary>
    private async Task EndTrainNowAsync()
    {
        if (_disposed) return;
        if (_completion.IsBusy || _reportPostBusy) { SetReportPostResult("A Discord report is already being sent."); return; }
        if (_config.SyncEnabled && _config.SyncShareTrain && (!_sync.IsConnected || !_sync.SupportsPartialFinish))
        {
            SetReportPostResult("Shared report not sent: connect to a server with persistent partial-report support first. The train has been kept.");
            _chatGui.PrintError("[Hunt Helper Evolved] " + _lastPostResult);
            return;
        }
        var marks = BuildCurrentMarks();
        if (marks.Count == 0)
        {
            SetReportPostResult("Nothing to post — the train is empty.");
            _chatGui.Print("[Hunt Helper Evolved] " + _lastPostResult);
            return;
        }

        // A report covers only the expansions the train actually killed
        // something in, and only those come off the train when it posts. The
        // legs that were scouted but not run stay put for a later train.
        var partial = CanReportPartially;
        var reportMarks = partial ? TrainReport.ForReport(marks) : marks;
        if (reportMarks.Count == 0)
        {
            SetReportPostResult("Nothing to post — no marks were killed on this train.");
            _chatGui.Print("[Hunt Helper Evolved] " + _lastPostResult);
            return;
        }

        var (reportedWatches, keptWatches) = partial
            ? TrainReport.SplitWatches(_config.Flags, TrainReport.ReportedLegs(marks), marks.Select(m => m.WorldId))
            : (CloneWatches(_config.Flags), new List<FlagEntry>());
        var submitted = partial ? TrainReport.SubmittedMarks(marks) : marks.Where(m => m.Dead).ToList();

        var snapshot = CompletionSnapshot();
        if (!_completion.TryBegin(snapshot)) return;
        var endedBy = _objectTable.LocalPlayer?.Name?.TextValue;
        var requiresServer = _config.SyncEnabled;
        var connectionId = _sync.ClientId;
        TrainFinishMessage? serverSubmission = null;
        try
        {
            var flags = CloneWatches(reportedWatches);
            var webhooks = Newtonsoft.Json.JsonConvert.DeserializeObject<List<WebhookEntry>>(
                Newtonsoft.Json.JsonConvert.SerializeObject(_config.Webhooks))!;
            var prepared = DiscordRelay.PrepareTrainReport(reportMarks, endedBy, flags, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            _trainCompletionReport = true;
            SetTrainReportPreview(prepared);
            var (success, message) = await DiscordRelay.PostPreparedReportAsync(webhooks, prepared, _disposal.Token);
            if (_disposal.IsCancellationRequested) return;
            Task<TrainFinishResult>? serverTask = null;
            await _framework.RunOnFrameworkThread(() =>
            {
                if (_disposal.IsCancellationRequested) return;
                SetReportPostResult(message);
                if (!success) { _chatGui.PrintError($"[Hunt Helper Evolved] Failed to post to Discord: {message}"); return; }
                if (CompletionSnapshot() != snapshot)
                {
                    SetReportPostResult("Discord posted; train changed while sending and was kept.");
                    _chatGui.Print("[Hunt Helper Evolved] " + _lastPostResult);
                    return;
                }
                if (requiresServer)
                {
                    // Use the latest acknowledged revisions after Discord returns, while retaining
                    // the exact report history and clear keys whose meaning was checked above.
                    serverSubmission = _sync.PrepareFinish(marks, partial ? submitted.Select(m => m.Key) : null, keptWatches);
                    CaptureResetUndo("Report completed");
                    _ownResetPendingAt = DateTime.UtcNow;
                    serverTask = _sync.SubmitFinish(serverSubmission!, connectionId);
                }
                else if (_completion.CanClear(CompletionSnapshot(), false))
                {
                    CaptureResetUndo("Report completed");
                    ClearReportedMarks(submitted, keptWatches, partial);
                    _chatGui.Print(partial
                        ? $"[Hunt Helper Evolved] Report posted; {DescribeKept(marks, submitted)} Undo is available."
                        : "[Hunt Helper Evolved] Report posted; local train reset. Undo is available.");
                }
            });
            if (serverTask is not null)
            {
                TrainFinishResult result;
                try { result = await serverTask; }
                catch (TimeoutException) { result = new() { Message="No server acknowledgement. Check the shared train before retrying; Undo is available if it was reset." }; }
                if (_disposal.IsCancellationRequested) return;
                await _framework.RunOnFrameworkThread(() =>
                {
                    _sync.ForgetFinish(serverSubmission!.RequestId);
                    if (_disposal.IsCancellationRequested) return;
                    if (result.Accepted && !serverSubmission.ClearShared && CompletionSnapshot()==snapshot)
                        ClearReportedMarks(submitted, keptWatches, partial);
                    SetReportPostResult("Discord posted. " + result.Message);
                    _chatGui.Print("[Hunt Helper Evolved] " + _lastPostResult);
                });
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Train report failed; the train has been preserved.");
            if (!_disposal.IsCancellationRequested)
                await _framework.RunOnFrameworkThread(() =>
                {
                    if (_disposal.IsCancellationRequested) return;
                    SetReportPostResult("Report failed; the train has been preserved. See the plugin log.");
                    _chatGui.PrintError($"[Hunt Helper Evolved] {_lastPostResult}");
                });
        }
        finally
        {
            if (!_disposal.IsCancellationRequested)
                await _framework.RunOnFrameworkThread(_completion.Finish);
        }
    }

    private void DrawUI()
    {
        if (_disposed) return;
        // Keep startup quiet, but preserve Active Marks after the first login
        // while DC travel passes through character selection.
        if (!_clientState.IsLoggedIn)
        {
            if (_releaseNotesChecked)
                _activeMarksWindow.Draw();
            return;
        }
        if (!_releaseNotesChecked)
        {
            _releaseNotesChecked = true;
            ShowReleaseNotesIfUpdated();
        }

        DrawTrainPopout();
        DrawPresetEditor();
        DrawCounterPopout();
        _activeMarksWindow.Draw();
        _srankWindow.Draw();
        _arankWindow.Draw();
        DrawReleaseNotesWindow();
        DrawMapControlBar();
        _tallyWindows.Draw();

        if (!_configWindowVisible) return;

        ImGui.SetNextWindowSize(new Vector2(900, 620), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(560, 320), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("Hunt Helper Evolved", ref _configWindowVisible, ImGuiWindowFlags.MenuBar))
        {
            DrawWindowMenu();
            if (ImGui.BeginTabBar("HuntHelperEvolvedTabs"))
            {
                if (ImGui.BeginTabItem("Train"))
                {
                    DrawTrainTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("S Ranks"))
                {
                    DrawSRankWorkspace();
                    ImGui.EndTabItem();
                }

                var settingsRequested = _selectSyncTab || _selectTallyTab || _selectActiveMarksSettings;
                if (_selectSyncTab) _settingsPage = SettingsPage.Sharing;
                if (_selectTallyTab) _settingsPage = SettingsPage.Tally;
                if (_selectActiveMarksSettings) _settingsPage = SettingsPage.ActiveMarks;
                if (ImGui.BeginTabItem("Settings", settingsRequested ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    _selectSyncTab = false;
                    _selectTallyTab = false;
                    _selectActiveMarksSettings = false;
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Help"))
                {
                    DrawHelpPage();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }


        }
        ImGui.End();
    }

    /// <summary>
    /// Our own detected train list, with per-row teleport and map-flag actions.
    /// Drawn in both the Train tab and the standalone popout.
    /// </summary>

    /// <summary>
    /// The tally settings, hosted in the shared settings content region.
    /// </summary>
    private void DrawTallyTab()
    {
        if (_standaloneTallyPresent)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(
                "The separate Hunt Tally plugin is still installed, so this one is not "
                + "counting anything and is not writing your tally file. Uninstall it from "
                + "the plugin installer, then reload Hunt Helper Evolved.");
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.Separator();
        }

        _tallySettings.Draw();
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
    /// Advances the pointer when the mark it's on has died — whether that was
    /// a manual tick or Hunt Tally doing it automatically. Runs from the draw
    /// loop so it catches both without either path needing to know about it.
    /// </summary>
    /// <summary>
    /// Clears out custom flags a few seconds after the conductor teleported to
    /// them. Marking them dead immediately on click was abrupt; this gives the
    /// row a moment to be seen before it goes.
    /// </summary>
    private readonly List<(uint NameId, uint Instance, uint WorldId)> _dueCustomRemovals = new();

    private void ProcessPendingCustomRemovals()
    {
        if (TrainMutationBusy || _pendingCustomRemovals.Count == 0) return;

        var now = DateTime.UtcNow;
        _dueCustomRemovals.Clear();
        foreach (var (key, dueAt) in _pendingCustomRemovals)
            if (now >= dueAt) _dueCustomRemovals.Add(key);
        foreach (var key in _dueCustomRemovals)
        {

            if (_detector.Marks.TryGetValue(key, out var mark) && mark.IsCustom)
            {
                mark.Dead = true;
                mark.DeathObservedAtUtc = now;
                _detector.Remove(key);
            }

            _pendingCustomRemovals.Remove(key);
        }
    }

    private void UpdateAutoAdvance()
    {
        if (!_config.AutoAdvance) return;

        var current = CurrentMark();
        if (current != null && !current.Dead) return;
        if (current == null && _currentMark != null)
        {
            // The pointed-at mark was removed from the list entirely.
            _currentMark = null;
        }

        var next = NextLiveMark();
        if (next == null) return;

        SetCurrentMark(next, announce: _config.EchoOnAdvance);
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

    private void OnNextAetheryteCommand(string command, string args)
    {
        var next = NextLiveMark();
        if (next == null)
        {
            _chatGui.Print("[Hunt Helper Evolved] No live marks left in the train.");
            return;
        }

        var aetheryte = TeleportHelper.NearestTo(next.TerritoryId, next.MapPosition);
        if (aetheryte is not { } aeth)
        {
            _chatGui.Print($"[Hunt Helper Evolved] No known aetheryte near {next.Name}.");
            return;
        }

        // Deliberately no map flag here: conductors call this out ahead of time
        // while the train is still travelling, and plugins like Hunt Train
        // Assistant auto-follow flags the conductor posts — dropping one early
        // would pull people off the current mark.
        var line = $"Teleport to {aeth.Name} after this mark!";

        var sb = new Dalamud.Game.Text.SeStringHandling.SeStringBuilder();
        sb.AddUiForeground(TrainChatEcho.GoldColour);
        sb.AddText("[Hunt Helper Evolved] ");
        sb.AddUiForegroundOff();
        sb.AddText(line);
        _chatGui.Print(sb.BuiltString);

        ImGui.SetClipboardText(line);
    }

    /// <summary>Pushes the saved blacklist into the teleport helper.</summary>
    private void SyncBlacklist()
    {
        TeleportHelper.Blacklist.Clear();
        foreach (var id in _config.BlacklistedAetherytes)
            TeleportHelper.Blacklist.Add(id);
    }

    /// <summary>
    /// Writes the in-progress train to disk. Called on a timer and on unload,
    /// so a crash costs at most a few seconds of kill times rather than the
    /// whole train.
    /// </summary>
    private void PersistTrain()
    {
        var history = _watcher.GetTrackedSnapshot().Values.ToList();
        var marks = _detector.ToPersisted();
        // Compare just the persisted train payload at the ten-second checkpoint.
        // Ignore the checkpoint time itself so an unchanged train does not force a write.
        var before = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            Marks = _config.SavedTrain, History = _config.ReportHistory,
            NameId = _config.SavedCurrentNameId, Instance = _config.SavedCurrentInstance,
            WorldId = _config.SavedCurrentWorldId
        });
        var after = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            Marks = marks, History = history,
            NameId = _currentMark?.NameId, Instance = _currentMark?.Instance,
            WorldId = _currentMark?.WorldId
        });
        if (before == after) return;

        _config.ReportHistory = history;
        _config.SavedTrain = marks;
        _config.SavedTrainAtUtc = marks.Count > 0 ? DateTime.UtcNow : null;
        _config.SavedCurrentNameId = _currentMark?.NameId;
        _config.SavedCurrentInstance = _currentMark?.Instance;
        _config.SavedCurrentWorldId = _currentMark?.WorldId;
        _config.Save();
    }

    /// <summary>
    /// Restores a train saved before a crash or reload. Deliberately never
    /// expires it — the instruction is that it lives until Reset — but it does
    /// say how old it is, so a stale one is obvious rather than silently
    /// treated as current.
    /// </summary>
    private void RestoreSavedTrain()
    {
        if (_config.SavedTrain.Count == 0) return;

        _detector.LoadPersisted(_config.SavedTrain);

        if (_config.SavedCurrentNameId is { } nameId && _config.SavedCurrentInstance is { } instance)
            _currentMark = (nameId, instance, _config.SavedCurrentWorldId ?? 0);

        var age = _config.SavedTrainAtUtc is { } at
            ? FormatAge(at)
            : "unknown age";

        var dead = _config.SavedTrain.Count(m => m.Dead);
        _chatGui.Print(
            $"[Hunt Helper Evolved] Restored train from {age} ago — " +
            $"{_config.SavedTrain.Count} marks, {dead} dead. Use Reset if this is stale.");
    }

    /// <summary>Drops the saved train. Only Reset and a posted train do this.</summary>
    private void ClearSavedTrain()
    {
        _config.LocalPresetRallies = new();
        _config.SavedTrain.Clear();
        _config.SavedTrainAtUtc = null;
        _config.SavedCurrentNameId = null;
        _config.SavedCurrentInstance = null;
        _config.SavedCurrentWorldId = null;
        _config.Save();
    }

    /// <summary>
    /// Folds an export code into the train, and says so out loud.
    ///
    /// The result goes to chat as well as to the status line, because the
    /// status line only exists on the main window: importing is offered from
    /// the popout too, and an import that silently did nothing visible from
    /// there would be indistinguishable from a dead button.
    /// </summary>
    private void ImportTrainCode(string code, string source)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            ReportProblem($"Nothing to import — {source} is empty.");
            return;
        }

        var imported = TrainExchange.Import(code);
        if (imported == null)
        {
            ReportProblem($"That import code couldn't be read ({source}).");
            return;
        }

        var added = _detector.Merge(imported);
        _lastPostResult = $"Imported {imported.Count} marks ({added} new).";
        _chatGui.Print($"[Hunt Helper Evolved] {_lastPostResult}");
    }

    /// <summary>
    /// Import straight off the clipboard, the way Hunt Helper does it — codes
    /// arrive pasted into Discord and go back out via Copy, so the trip through
    /// a text box was only ever ceremony.
    /// </summary>
    private void ImportFromClipboard()
    {
        string clipboard;
        try
        {
            clipboard = ImGui.GetClipboardText() ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Reading the clipboard goes out to the OS and can genuinely fail
            // — another process holding it is enough.
            _log.Warning(ex, "Could not read the clipboard for a train import.");
            ReportProblem("Couldn't read the clipboard.");
            return;
        }

        ImportTrainCode(clipboard, "the clipboard");
    }

    private void ReportProblem(string message)
    {
        _lastPostResult = message;
        _chatGui.PrintError($"[Hunt Helper Evolved] {message}");
        _log.Warning(message);
    }

    private static void TrainControlSameLine(string nextLabel, float reservedRight = 0)
    {
        var style = ImGui.GetStyle();
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - reservedRight;
        if (ImGui.GetItemRectMax().X + style.ItemSpacing.X + ImGui.CalcTextSize(nextLabel).X
            + style.FramePadding.X * 2 <= right) ImGui.SameLine();
    }

    private const int MaxBlacklistedAetherytes = 15;

    private static readonly (string Name, uint Min, uint Max)[] ExpansionRanges =
    {
        ("A Realm Reborn", 0, 396),
        ("Heavensward", 397, 611),
        ("Stormblood", 612, 812),
        ("Shadowbringers", 813, 955),
        ("Endwalker", 956, 1186),
        ("Dawntrail", 1187, 9999),
    };

    /// <summary>
    /// Expansion -> zone -> aetheryte picker for the blacklist. Zone names come
    /// from the game's own data rather than a hardcoded list, so they're correct
    /// and localised; expansion grouping is by territory id range.
    /// </summary>
    private void DrawBlacklistPicker()
    {
        var expansionNames = ExpansionRanges.Select(e => e.Name).ToArray();
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Expansion", ref _blacklistExpansion, expansionNames, expansionNames.Length))
        {
            _blacklistZone = 0;
            _blacklistAetheryte = 0;
        }

        var range = ExpansionRanges[Math.Clamp(_blacklistExpansion, 0, ExpansionRanges.Length - 1)];

        var zones = TeleportHelper.All
            .Where(a => a.TerritoryId >= range.Min && a.TerritoryId <= range.Max)
            .Select(a => a.TerritoryId)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        if (zones.Count == 0)
        {
            ImGui.TextDisabled("No aetherytes known for that expansion.");
            return;
        }

        var zoneNames = zones.Select(t => _detector.GetZoneName(t)).ToArray();
        _blacklistZone = Math.Clamp(_blacklistZone, 0, zones.Count - 1);
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Zone", ref _blacklistZone, zoneNames, zoneNames.Length))
        {
            _blacklistAetheryte = 0;
        }

        var inZone = TeleportHelper.All
            .Where(a => a.TerritoryId == zones[_blacklistZone])
            .OrderBy(a => a.Name)
            .ToList();

        if (inZone.Count == 0)
        {
            ImGui.TextDisabled("No aetherytes in that zone.");
            return;
        }

        var aetheryteNames = inZone.Select(a => a.Name).ToArray();
        _blacklistAetheryte = Math.Clamp(_blacklistAetheryte, 0, inZone.Count - 1);
        ImGui.SetNextItemWidth(200);
        ImGui.Combo("Aetheryte", ref _blacklistAetheryte, aetheryteNames, aetheryteNames.Length);

        ImGui.SameLine();
        if (ImGui.Button("Blacklist"))
        {
            var chosen = inZone[_blacklistAetheryte];
            if (_config.BlacklistedAetherytes.Count >= MaxBlacklistedAetherytes)
                _lastPostResult = $"Blacklist is full ({MaxBlacklistedAetherytes}).";
            else if (_config.BlacklistedAetherytes.Contains(chosen.AetheryteId))
                _lastPostResult = $"{chosen.Name} is already blacklisted.";
            else
            {
                _config.BlacklistedAetherytes.Add(chosen.AetheryteId);
                _config.Save();
                SyncBlacklist();
            }
        }

        ImGui.Spacing();

        if (_config.BlacklistedAetherytes.Count == 0)
        {
            ImGui.TextDisabled("Nothing blacklisted.");
            return;
        }

        uint? toUnblock = null;
        foreach (var id in _config.BlacklistedAetherytes)
        {
            var match = TeleportHelper.All.FirstOrDefault(a => a.AetheryteId == id);
            var label = string.IsNullOrEmpty(match.Name) ? $"Aetheryte {id}" : match.Name;
            var zoneLabel = match.TerritoryId == 0 ? "" : $" — {_detector.GetZoneName(match.TerritoryId)}";

            ImGui.PushID((int)id);
            ImGui.TextWrapped($"{label}{zoneLabel}");
            ImGui.SameLine();
            if (ImGui.SmallButton("remove")) toUnblock = id;
            ImGui.PopID();
        }

        if (toUnblock.HasValue)
        {
            _config.BlacklistedAetherytes.Remove(toUnblock.Value);
            _config.Save();
            SyncBlacklist();
        }
    }

    /// <summary>
    /// A small always-available window for the map dot filters, so they can be
    /// flipped mid-scout without opening Settings.
    /// </summary>
    /// <summary>
    /// The ring and facing guide drawn around your character.
    ///
    /// Both stand on their own — either can be used without the other, and
    /// without the spawn points, which is why the overlay's enable check asks
    /// whether ANY of the three is on rather than looking at spawn points.
    /// </summary>
    private void DrawPlayerGuideSettings()
    {
        ImGui.TextWrapped("Around your character");

        const ImGuiColorEditFlags flags =
            ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf;

        var guides = _config.ShowPlayerGuides;
        if (ImGui.Checkbox("Show these at all", ref guides))
        {
            _config.ShowPlayerGuides = guides;
            _config.Save();
        }
        ImGui.TextDisabled("One switch for the four below. They keep their own settings while it's off.");

        ImGui.Spacing();
        using var guideGroup = ImRaii.Disabled(!_config.ShowPlayerGuides);

        var circle = _config.ShowPlayerCircleOnMap;
        if (ImGui.Checkbox("Range circle", ref circle))
        {
            _config.ShowPlayerCircleOnMap = circle;
            _config.Save();
        }

        if (_config.ShowPlayerCircleOnMap)
        {
            var circleColour = _config.PlayerCircleColour;
            if (ImGui.ColorEdit4("Circle colour", ref circleColour, flags))
            {
                _config.PlayerCircleColour = circleColour;
                _config.Save();
            }

            var scale = _config.PlayerCircleRadiusScale;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Circle radius scale", ref scale, 0.25f, 4f, "%.2f"))
            {
                _config.PlayerCircleRadiusScale = Math.Clamp(scale, 0.25f, 4f);
                _config.Save();
            }

            var thickness = _config.PlayerCircleThickness;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Circle line width", ref thickness, 1f, 40f, "%.0f"))
            {
                _config.PlayerCircleThickness = Math.Clamp(thickness, 1f, 40f);
                _config.Save();
            }

        }

        ImGui.Spacing();

        var dirLine = _config.ShowPlayerDirectionLine;
        if (ImGui.Checkbox("Heading line", ref dirLine))
        {
            _config.ShowPlayerDirectionLine = dirLine;
            _config.Save();
        }
        ImGui.TextDisabled("A short line from you to the edge of the circle. Always the circle's radius long.");

        if (_config.ShowPlayerDirectionLine)
        {
            var dirColour = _config.PlayerDirectionLineColour;
            if (ImGui.ColorEdit4("Heading line colour", ref dirColour, flags))
            {
                _config.PlayerDirectionLineColour = dirColour;
                _config.Save();
            }

            // Shown as a percentage of the circle's radius, which is what it
            // is — a proportion, so it holds at any zoom.
            var dirThickness = _config.PlayerDirectionLineThickness * 100f;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Heading line thickness", ref dirThickness, 1f, 40f, "%.0f%%"))
            {
                _config.PlayerDirectionLineThickness = Math.Clamp(dirThickness / 100f, 0.01f, 0.4f);
                _config.Save();
            }
        }

        ImGui.Spacing();

        var posDot = _config.ShowPlayerPositionDot;
        if (ImGui.Checkbox("Position dot", ref posDot))
        {
            _config.ShowPlayerPositionDot = posDot;
            _config.Save();
        }
        ImGui.TextDisabled("A dot on exactly where you are, inside the circle.");

        if (_config.ShowPlayerPositionDot)
        {
            var dotColour = _config.PlayerPositionDotColour;
            if (ImGui.ColorEdit4("Position dot colour", ref dotColour, flags))
            {
                _config.PlayerPositionDotColour = dotColour;
                _config.Save();
            }

            var dotSize = _config.PlayerPositionDotSize * 100f;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Position dot size", ref dotSize, 1f, 50f, "%.0f%%"))
            {
                _config.PlayerPositionDotSize = Math.Clamp(dotSize / 100f, 0.01f, 0.5f);
                _config.Save();
            }
            ImGui.TextDisabled("Both are a percentage of the circle's radius, so they keep their proportions at any zoom.");
        }

        ImGui.Spacing();

        var facing = _config.ShowPlayerFacingOnMap;
        if (ImGui.Checkbox("Projected path", ref facing))
        {
            _config.ShowPlayerFacingOnMap = facing;
            _config.Save();
        }

        if (_config.ShowPlayerFacingOnMap)
        {
            var facingColour = _config.PlayerFacingColour;
            if (ImGui.ColorEdit4("Path colour", ref facingColour, flags))
            {
                _config.PlayerFacingColour = facingColour;
                _config.Save();
            }
        }

    }

    /// <summary>
    /// Colour pickers for each state a spawn point can be in.
    ///
    /// Alpha is editable too. A zone with sixty ARR spawn points is a wall of
    /// dots at full opacity, and turning the empty ones down is what makes the
    /// occupied ones stand out.
    /// </summary>
    private void DrawDotColours()
    {
        ImGui.TextWrapped("Dot colours");

        const ImGuiColorEditFlags flags =
            ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf;

        var empty = _config.SpawnDotColourEmpty;
        if (ImGui.ColorEdit4("Empty point", ref empty, flags))
        {
            _config.SpawnDotColourEmpty = empty;
            _config.Save();
        }

        var inTrain = _config.SpawnDotColourInTrain;
        if (ImGui.ColorEdit4("Spawn point in train", ref inTrain, flags))
        {
            _config.SpawnDotColourInTrain = inTrain;
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Visible spawn points matched to living marks in the current world's train. Returns to the normal colour when dead, sniped or removed. S-rank candidate outlines are preserved.");

        var b = _config.SpawnDotColourB;
        if (ImGui.ColorEdit4("B rank on it", ref b, flags))
        {
            _config.SpawnDotColourB = b;
            _config.Save();
        }

        var a = _config.SpawnDotColourA;
        if (ImGui.ColorEdit4("A rank on it", ref a, flags))
        {
            _config.SpawnDotColourA = a;
            _config.Save();
        }

        var sRank = _config.SpawnDotColourS;
        if (ImGui.ColorEdit4("S rank on it", ref sRank, flags))
        {
            _config.SpawnDotColourS = sRank;
            _config.Save();
        }

        var minion = _config.SsMinionColour;
        if (ImGui.ColorEdit4("SS event minions", ref minion, flags))
        {
            _config.SsMinionColour = minion;
            _config.Save();
        }

        var labelColour = _config.MarkLabelColour;
        if (ImGui.ColorEdit4("Mark name text", ref labelColour, flags))
        {
            _config.MarkLabelColour = labelColour;
            _config.Save();
        }

        var labelOutline = _config.MarkLabelOutlineColour;
        if (ImGui.ColorEdit4("Mark name outline", ref labelOutline, flags))
        {
            _config.MarkLabelOutlineColour = labelOutline;
            _config.Save();
        }
        ImGui.TextDisabled("The outline is what keeps the text readable over a pale map. Dropping its alpha to nothing removes it.");

        if (ImGui.Button("Reset dot colours"))
        {
            var defaults = new Configuration();
            _config.SpawnDotColourEmpty = defaults.SpawnDotColourEmpty;
            _config.SpawnDotColourInTrain = defaults.SpawnDotColourInTrain;
            _config.SpawnDotColourB = defaults.SpawnDotColourB;
            _config.SpawnDotColourA = defaults.SpawnDotColourA;
            _config.SpawnDotColourS = defaults.SpawnDotColourS;
            _config.SsMinionColour = defaults.SsMinionColour;
            _config.MarkLabelColour = defaults.MarkLabelColour;
            _config.MarkLabelOutlineColour = defaults.MarkLabelOutlineColour;
            _config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Restore the default dot colours.");
    }

    /// <summary>
    /// Measured toolbar height: place above the map when it fits, below otherwise.
    /// Keep the map title bar free so the player can drag it away from the screen edge.
    /// </summary>
    private float _mapBarHeight = 56f;

    private void DrawMapControlBar()
    {
        if (!_config.ShowMapControlBar) return;

        var addon = _gameGui.GetAddonByName("AreaMap");
        if (addon.IsNull || !addon.IsVisible) return;

        var width = addon.ScaledWidth;
        if (width <= 0) return;

        var viewport = ImGui.GetMainViewport();
        var top = MapBarPlacement.Top(addon.Y, addon.ScaledHeight, _mapBarHeight,
            viewport.WorkPos.Y, viewport.WorkSize.Y);
        ImGui.SetNextWindowPos(new Vector2(addon.X, top), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, 0f), ImGuiCond.Always);

        // Tighter than the default, to keep two rows from becoming a slab.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 2f));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav;

        if (ImGui.Begin("##HuntHelperEvolvedMapBar", flags))
        {
            DrawMapBarZoneRow();
            DrawMapBarPlayerRow();
            DrawOccupiedSpawnPointSetting();

            _mapBarHeight = ImGui.GetWindowHeight();
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// First row: what is drawn about the zone — the spawn points and the marks,
    /// each with its own ranks, and the SS event.
    ///
    /// Points and marks get their own switches because they answer different
    /// questions: the points are where a mark COULD be, the marks are what is
    /// there now. Wanting only A/S points while still being told about a B rank
    /// that has turned up is an ordinary way to hunt, and one set of switches
    /// could not express it.
    /// </summary>
    private void DrawMapBarZoneRow()
    {
        var points = _config.ShowSpawnPointsOnMap;
        if (ImGui.Checkbox("Spawn points", ref points))
        {
            _config.ShowSpawnPointsOnMap = points;
            _config.Save();
        }

        // The rank filters only mean anything while the points are drawn.
        ImGui.SameLine();
        using (ImRaii.Disabled(!_config.ShowSpawnPointsOnMap))
        {
            var showB = _config.ShowBRankPoints;
            if (ImGui.Checkbox("B##points", ref showB))
            {
                _config.ShowBRankPoints = showB;
                _config.Save();
            }

            ImGui.SameLine();
            var showA = _config.ShowARankPoints;
            if (ImGui.Checkbox("A##points", ref showA))
            {
                _config.ShowARankPoints = showA;
                _config.Save();
            }

            ImGui.SameLine();
            var showS = _config.ShowSRankPoints;
            if (ImGui.Checkbox("S##points", ref showS))
            {
                _config.ShowSRankPoints = showS;
                _config.Save();
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        var marks = _config.ShowMarksOnMap;
        if (ImGui.Checkbox("Marks", ref marks))
        {
            _config.ShowMarksOnMap = marks;
            _config.Save();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!_config.ShowMarksOnMap))
        {
            var markB = _config.ShowBRankMarks;
            if (ImGui.Checkbox("B##marks", ref markB))
            {
                _config.ShowBRankMarks = markB;
                _config.Save();
            }

            ImGui.SameLine();
            var markA = _config.ShowARankMarks;
            if (ImGui.Checkbox("A##marks", ref markA))
            {
                _config.ShowARankMarks = markA;
                _config.Save();
            }

            ImGui.SameLine();
            var markS = _config.ShowSRankMarks;
            if (ImGui.Checkbox("S##marks", ref markS))
            {
                _config.ShowSRankMarks = markS;
                _config.Save();
            }

            ImGui.SameLine();
            var labels = _config.ShowMarkLabelsOnMap;
            if (ImGui.Checkbox("Names", ref labels))
            {
                _config.ShowMarkLabelsOnMap = labels;
                _config.Save();
            }

        }

        // Not a spawn point, so not behind that toggle.
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        var ssEvent = _config.ShowSsEventOnMap;
        if (ImGui.Checkbox("SS event", ref ssEvent))
        {
            _config.ShowSsEventOnMap = ssEvent;
            _config.Save();
        }

    }

    /// <summary>
    /// Second row: the four pieces drawn around your character. Each stands
    /// alone, so all four are here rather than one toggle for the lot.
    /// </summary>
    private void DrawMapBarPlayerRow()
    {
        var guides = _config.ShowPlayerGuides;
        if (ImGui.Checkbox("Around you", ref guides))
        {
            _config.ShowPlayerGuides = guides;
            _config.Save();
        }

        // Scoped rather than disposed by hand: the status below has to sit
        // outside it, and a block says where it ends without depending on
        // ImRaii tolerating a second Dispose.
        using (ImRaii.Disabled(!_config.ShowPlayerGuides))
        {
            ImGui.SameLine();
            var circle = _config.ShowPlayerCircleOnMap;
            if (ImGui.Checkbox("Range", ref circle))
            {
                _config.ShowPlayerCircleOnMap = circle;
                _config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The detection range circle.");

            ImGui.SameLine();
            var facing = _config.ShowPlayerFacingOnMap;
            if (ImGui.Checkbox("Path", ref facing))
            {
                _config.ShowPlayerFacingOnMap = facing;
                _config.Save();
            }

            ImGui.SameLine();
            var dirLine = _config.ShowPlayerDirectionLine;
            if (ImGui.Checkbox("Line", ref dirLine))
            {
                _config.ShowPlayerDirectionLine = dirLine;
                _config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Heading line, out to the edge of the range circle.");

            ImGui.SameLine();
            var posDot = _config.ShowPlayerPositionDot;
            if (ImGui.Checkbox("Dot", ref posDot))
            {
                _config.ShowPlayerPositionDot = posDot;
                _config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("A dot on exactly where you are.");
        }

        // Worth reading whether or not the guides are switched on, so it is
        // outside the block above.
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{_mapOverlay.Status}\n\n/htrm hides this bar.");
    }

    private void DrawTrainNavigation()
    {
        if (ImGui.Button("Next Mark"))
        {
            SetCurrentMark(NextLiveMark(), announce: true);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move to the next live mark and flag it");

        TrainControlSameLine("Next Aetheryte");
        if (ImGui.Button("Next Aetheryte"))
        {
            OnNextAetheryteCommand(NextAetheryteCommand, string.Empty);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Announce the next aetheryte without teleporting or changing the current map flag");
    }

    private void DrawAddTrainFlagControls(float? labelX = null, float labelWidth = 110)
    {
        ImGui.BeginDisabled(TrainMutationBusy);
        var buttonX = ImGui.GetCursorPosX();
        if (ImGui.Button("Add Flag") && !TrainMutationBusy)
        {
            var added = _detector.AddCustomFlag(_customFlagLabel);
            if (added == null)
                ReportProblem("No map flag set — place one with Ctrl+Right-Click first.");
            else
                _customFlagLabel = string.Empty;
        }
        var buttonRight = buttonX + ImGui.GetItemRectSize().X;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Adds your current map flag to the train as a custom stop");
        if (labelX is { } x)
        {
            if (buttonRight + ImGui.GetStyle().ItemSpacing.X <= x) ImGui.SameLine();
            ImGui.SetCursorPosX(x);
        }
        else TrainControlSameLine("flag name (optional)");
        ImGui.SetNextItemWidth(labelWidth);
        ImGui.InputTextWithHint("##customFlagLabel", "flag name", ref _customFlagLabel, 64);
        ImGui.EndDisabled();
    }

    private void DrawTrainControls()
    {
        DrawAddTrainFlagControls();

        // Its own row on purpose. This is the one control here that rewrites
        // the whole list, and it should not sit a mis-click away from Remove
        // Dead. It lives on the shared control bar rather than the Train tab so
        // the popout — the window actually open during a train, and where
        // codes actually arrive — can import without going back to the tab.
        if (ImGui.Button("Import from Clipboard"))
        {
            ImportFromClipboard();
        }

        TrainControlSameLine("Copy Export Code");
        if (ImGui.Button("Copy Export Code"))
        {
            if (_detector.Marks.Count == 0) _lastPostResult = "Nothing to export — no marks detected yet.";
            else
            {
                try
                {
                    ImGui.SetClipboardText(TrainExchange.Export(_detector.Ordered()));
                    _lastPostResult = $"Exported {_detector.Marks.Count} marks to clipboard.";
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Could not copy train export.");
                    _lastPostResult = "Could not copy the train export. See the plugin log.";
                }
            }
        }

        // Display options
        var hideDead = _config.HideDeadMarks;
        if (ImGui.Checkbox("Hide dead", ref hideDead))
        {
            _config.HideDeadMarks = hideDead;
            _config.DeferWindowStateSave();
        }

        TrainControlSameLine("Group expansions within worlds");
        var grouped = _config.GroupTrainByExpansion;
        if (ImGui.Checkbox("Group expansions within worlds", ref grouped))
        {
            _config.GroupTrainByExpansion = grouped;
            if (grouped) _detector.ApplyOrder(GroupByExpansion(_detector.Ordered()));
            _config.Save();
        }

        // Only offered while the train is in blocks, since there is no next
        // block to open without them.
        if (grouped)
        {
            TrainControlSameLine("Open next automatically");
            var autoExpand = _config.AutoExpandNextExpansion;
            if (ImGui.Checkbox("Open next automatically", ref autoExpand))
            {
                _config.AutoExpandNextExpansion = autoExpand;
                _config.DeferWindowStateSave();
            }

        }

    }

    private readonly TrainGroupingState _trainGroupingState = new();
    private readonly List<((uint WorldId, string Expansion) Block, bool Dead)> _trainProgressInput = new();
    private readonly Dictionary<(uint WorldId, string Expansion), int> _trainExpansionCounts = new();
    private readonly Dictionary<(uint WorldId, string Expansion), int> _trainExpansionUpCounts = new();
    private readonly List<(uint WorldId, string Expansion)> _trainPresentExpansions = new();
    private readonly List<DetectedMark> _trainVisibleMarks = new();

    private void ResetTrainExpansionProgress()
    {
        _trainProgressInput.Clear();
        _expansionProgress.Reset();
    }

    private void AutoExpandNextExpansion(List<DetectedMark> allMarks)
    {
        var changed = _trainProgressInput.Count != allMarks.Count;
        for (var i = 0; i < allMarks.Count; i++)
        {
            var state = (TrainBlock(allMarks[i]), allMarks[i].Dead);
            if (i == _trainProgressInput.Count) _trainProgressInput.Add(state);
            else { changed |= _trainProgressInput[i] != state; _trainProgressInput[i] = state; }
        }
        if (_trainProgressInput.Count > allMarks.Count)
            _trainProgressInput.RemoveRange(allMarks.Count, _trainProgressInput.Count - allMarks.Count);
        if (!changed) return;
        var opened = false;
        var blocks = allMarks.Select(TrainBlock).Distinct().ToDictionary(TrainBlockKey);
        foreach (var key in _expansionProgress.Update(allMarks.Select(mark =>
            (TrainBlockKey(TrainBlock(mark)), mark.Dead))))
        {
            SetTrainBlockCollapsed(blocks[key], false);
            opened = true;
        }
        if (opened) _config.DeferWindowStateSave();
    }

    /// <summary>
    /// The train sorted into expansion blocks.
    ///
    /// OrderBy is a stable sort, which is the whole trick: marks keep the order
    /// they were scouted — or dragged — into within their own block, and only
    /// the blocks themselves move.
    /// </summary>
    private List<DetectedMark> GroupByExpansion(List<DetectedMark> marks)
    {
        if (PresetOrderLocked) return marks;
        if (!_config.GroupTrainByExpansion || (_config.SyncEnabled && _config.SyncShareTrain))
            return Sync.SharedRouteGrouping.GroupByWorldInRouteOrder(marks, m => m.WorldId,
                m => ExpansionData.ExpansionOf(m.NameId, m.ZoneName), _config.GroupTrainByExpansion);
        return marks.GroupBy(m => m.WorldId).SelectMany(world =>
        {
            var order = ExpansionDisplayOrder(world);
            return world.OrderBy(m => order.IndexOf(ExpansionData.ExpansionOf(m.NameId, m.ZoneName)));
        }).ToList();
    }

    /// <summary>
    /// Every expansion in the order its block should appear.
    ///
    /// The order the expansions ALREADY stand in comes before the usual
    /// ARR -> Dawntrail one, and that is the important part. Ticking the box
    /// should fold the train into blocks, not rearrange it: a list imported
    /// Dawntrail -> Shadowbringers -> Endwalker was put in that order on
    /// purpose, and re-sorting it into the order the expansions were released
    /// is a change nobody asked for.
    ///
    /// Reading the order back out of the list is a fixed point — once grouped,
    /// first appearance IS block order — so it settles on the first frame and
    /// never drifts afterwards.
    ///
    /// A dragged arrangement outranks both, because it is the one the conductor
    /// actually asked for.
    /// </summary>
    private List<string> ExpansionDisplayOrder(IEnumerable<DetectedMark> marks)
    {
        var order = new List<string>();

        if (!_config.SyncEnabled || !_config.SyncShareTrain)
            foreach (var name in _config.WorldExpansionOrder.GetValueOrDefault(
                         marks.FirstOrDefault()?.WorldId ?? 0, _config.ExpansionOrder))
                if (!order.Contains(name)) order.Add(name);

        foreach (var mark in marks)
        {
            var name = ExpansionData.ExpansionOf(mark.NameId, mark.ZoneName);
            if (!order.Contains(name)) order.Add(name);
        }

        // And last, expansions with nothing in the train at all, so a block has
        // somewhere to appear the moment its first mark is scouted.
        foreach (var name in ExpansionData.Expansions)
            if (!order.Contains(name)) order.Add(name);

        if (!order.Contains(ExpansionData.NoExpansion))
            order.Add(ExpansionData.NoExpansion);

        return order;
    }

    /// <summary>
    /// Puts one expansion where another currently sits, and records the whole
    /// resulting arrangement.
    ///
    /// The full order is saved rather than only the pair that moved, because an
    /// expansion with nothing in the train is not on screen to be dragged —
    /// recording only the visible ones would let the rest drift about as marks
    /// were scouted.
    /// </summary>
    private void MoveExpansion((uint WorldId, string Expansion) moving,
        (uint WorldId, string Expansion) target)
    {
        if (moving.WorldId != target.WorldId) return;
        var allMarks = _detector.Ordered();
        var order = PresetOrderLocked
            ? allMarks.Where(m => m.WorldId == moving.WorldId).Select(m => TrainBlock(m).Expansion).Distinct().ToList()
            : ExpansionDisplayOrder(allMarks.Where(m => m.WorldId == moving.WorldId));
        var from = order.IndexOf(moving.Expansion);
        var to = order.IndexOf(target.Expansion);
        if (from < 0 || to < 0 || from == to) return;

        order.RemoveAt(from);
        order.Insert(to, moving.Expansion);

        if (!ApplyManualTrainOrder(allMarks.GroupBy(m => m.WorldId).SelectMany(world =>
            world.Key == moving.WorldId
                ? world.OrderBy(m => order.IndexOf(ExpansionData.ExpansionOf(m.NameId, m.ZoneName))).ToList()
                : world.ToList()).ToList())) return;
        if (!PresetOrderLocked)
        {
            _config.WorldExpansionOrder[moving.WorldId] = order;
            _config.Save();
        }
    }

    private static (uint WorldId, string Expansion) TrainBlock(DetectedMark mark) =>
        (mark.WorldId, ExpansionData.ExpansionOf(mark.NameId, mark.ZoneName));

    private static string TrainBlockKey((uint WorldId, string Expansion) block) =>
        $"{block.WorldId}:{block.Expansion}";

    private string TrainWorldName(uint worldId, IEnumerable<DetectedMark> marks) =>
        marks.FirstOrDefault(m => m.WorldId == worldId && !string.IsNullOrWhiteSpace(m.WorldName))?.WorldName
        ?? (worldId == 0 ? "World not recorded" : _worldData.NameOf(worldId));

    private void SetTrainBlockCollapsed((uint WorldId, string Expansion) block, bool collapsed)
    {
        // Convert the old expansion-wide preference without opening the same leg on other worlds.
        if (_config.CollapsedExpansions.Remove(block.Expansion))
            foreach (var other in _detector.Ordered().Select(TrainBlock).Distinct()
                         .Where(other => other.Expansion == block.Expansion))
            {
                var otherKey = TrainBlockKey(other);
                if (!_config.CollapsedExpansions.Contains(otherKey)) _config.CollapsedExpansions.Add(otherKey);
            }
        var key = TrainBlockKey(block);
        if (collapsed)
        {
            if (!_config.CollapsedExpansions.Contains(key)) _config.CollapsedExpansions.Add(key);
        }
        else _config.CollapsedExpansions.Remove(key);
    }

    private static bool TrainIconButton(FontAwesomeIcon icon, Vector2 size)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.Button(icon.ToIconString(), size);
    }

    /// <summary>
    /// The Spawned / Didn't Spawn pair for one watch.
    ///
    /// Shared by Train > S-rank watches and the train list rather than written out
    /// twice, because the two are the same fact about the same object and
    /// writing them separately is how they would come to disagree.
    /// </summary>
    private void DrawSpawnStatusBoxes(FlagEntry flag)
    {
        ImGui.BeginDisabled(TrainMutationBusy);
        var spawned = flag.SpawnStatus == SpawnStatus.Spawned;
        var notSpawned = flag.SpawnStatus == SpawnStatus.NotSpawned;

        if (ImGui.Checkbox("Spawned", ref spawned))
        {
            flag.SpawnStatus = spawned ? SpawnStatus.Spawned : SpawnStatus.Unknown;
            _config.Save();
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("Didn't Spawn", ref notSpawned))
        {
            flag.SpawnStatus = notSpawned ? SpawnStatus.NotSpawned : SpawnStatus.Unknown;
            _config.Save();
        }
        ImGui.EndDisabled();
    }

    /// <summary>
    /// The S-rank workspace’s S-rank watches, repeated under the train.
    ///
    /// The same FlagEntry objects, not copies: a box ticked here is ticked
    /// there, and either way it is the same answer that reaches the end-of-
    /// train report.
    ///
    /// Adding and removing watches stays on Train > S-rank watches. This is the
    /// during-the-train view, and the only question it has to answer is whether
    /// the thing was up — an S rank gets checked in passing, between marks, and
    /// walking back to a settings tab to record it is exactly when it gets
    /// forgotten instead.
    /// </summary>
    private void DrawSRankWatchRows()
    {
        if (!_config.ShowSRankWatchesInTrainList) return;
        if (_config.Flags.Count == 0) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.78f, 1f, 1f));
        ImGui.TextUnformatted("S-rank watches");
        ImGui.PopStyleColor();

        for (var i = 0; i < _config.Flags.Count; i++)
        {
            var flag = _config.Flags[i];
            ImGui.PushID($"srankwatch{i}");

            // Boxes before the label, unlike Train > S-rank watches. Watch labels
            // run to very different lengths — "Tyger" against "Narrow-rift —
            // Spawn 3 (23.4, 33.1)" — and a label first would move the boxes
            // for every row, in the one window being clicked at while running.
            DrawSpawnStatusBoxes(flag);

            TrainControlSameLine(flag.Label);
            var colour = flag.SpawnStatus switch
            {
                SpawnStatus.Spawned => new Vector4(0.45f, 0.95f, 0.5f, 1f),
                SpawnStatus.NotSpawned => new Vector4(0.55f, 0.55f, 0.55f, 1f),
                _ => Vector4.One,
            };
            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            ImGui.TextWrapped(flag.Label);
            ImGui.PopStyleColor();

            ImGui.PopID();
        }
    }

    private void DrawTrainTab() => DrawTrainWorkspace(popout: false);

    private List<string> CombinedTrainScouts() => (_sync.IsConnected && _config.SyncShareTrain ? _sync.TrainScouts : Array.Empty<string>())
        .Concat(_config.AdditionalScouts)
        .Append((!_sync.IsConnected || !_config.SyncShareTrain) && !_config.ScanningPaused && _detector.Marks.Count > 0 ? _objectTable.LocalPlayer?.Name?.TextValue ?? string.Empty : string.Empty)
        .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void DrawTrainScouts()
    {
        ImGui.TextWrapped("Scouts: " + string.Join(", ", CombinedTrainScouts()));
        if (ImGui.TreeNode("Add scout credits"))
        {
            DrawStringList(_config.AdditionalScouts, MaxAdditionalScouts, "+ Add scout",
                $"Maximum of {MaxAdditionalScouts} additional scouts reached.");
            if (_sync.IsConnected && _config.SyncShareTrain && _sync.SupportsScoutRemoval)
            {
                ImGui.TextWrapped("Shared credits — removal applies to the group until restored.");
                foreach (var credit in _sync.ScoutCredits.ToList())
                {
                    ImGui.PushID("credit:" + credit.Name);
                    if (ImGui.SmallButton(credit.Removed ? "Restore" : "Remove"))
                        _sync.ChangeScoutCredit(credit.Name, credit.Removed);
                    ImGui.SameLine();
                    ImGui.TextWrapped(credit.Name + (credit.Removed ? " (removed)" : "") + " — " + credit.Source
                        + (string.IsNullOrWhiteSpace(credit.AddedBy) ? "" : " by " + credit.AddedBy));
                    ImGui.PopID();
                }
            }
            else ImGui.TextWrapped("Shared credit removal requires a connected server with scout-removal support.");
            ImGui.TreePop();
        }
    }

    private void DrawTrainPopout()
    {
        if (!_trainPopoutVisible) return;
        ImGui.SetNextWindowSize(new Vector2(600, 420), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 280), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("Hunt Train", ref _trainPopoutVisible)) DrawTrainWorkspace(popout: true);
        ImGui.End();
    }

    /// <summary>
    /// Counter rows for one world. Counts are kept per world, so the same mark
    /// tracked on Mateus and on Zalera are genuinely separate tallies.
    /// </summary>
    private void DrawCounterList(bool currentZoneOnly, uint worldId, uint instance, string worldName)
    {
        var defs = HuntCounter.Definitions.AsEnumerable();
        if (currentZoneOnly)
        {
            var here = _clientTerritory;
            defs = defs.Where(d => d.TerritoryId == here);
        }

        var list = defs.ToList();
        if (list.Count == 0)
        {
            // Narrow-rift and Nunyunuwi are counted S-ranks too, just drawn by
            // DrawSpawnWatches rather than from Definitions - don't claim the
            // zone has nothing when one of them is showing right above.
            if (currentZoneOnly && SpawnWatchCounters.AppliesTo(_clientTerritory))
                return;

            ImGui.TextDisabled(currentZoneOnly
                ? "No counted S-rank in this zone."
                : "No counters available.");
            return;
        }

        foreach (var def in list)
        {
            ImGui.PushID($"{def.MarkName}_{worldId}");
            ImGui.TextWrapped($"{def.MarkName} — {def.Zone} ({worldName})");

            foreach (var mob in def.MobNames)
            {
                var count = _counter.GetTally(worldId, instance, mob);
                var shared = def.TriggerPatterns.Length == 0
                    ? _sync.SharedCounterTotal(worldId, def.TerritoryId, instance, mob) : null;
                ImGui.TextDisabled($"    {mob}: {count}" + (shared is { } total ? $" ({total})" : string.Empty));
                if (ImGui.IsItemHovered() && def.TriggerPatterns.Length == 0)
                    ImGui.SetTooltip(shared is not null
                        ? "Brackets: group total of personal kills since the shared reset. Nearby kills and older local counts are not uploaded. Local Reset/auto-reset does not change the group total."
                        : "Shared total unavailable: connect to a server with counter syncing enabled.");
            }

            var settings = _counter.SettingsFor(def.MarkName);

            var autoReset = settings.AutoResetEnabled;
            if (ImGui.Checkbox("Auto-reset", ref autoReset))
            {
                settings.AutoResetEnabled = autoReset;
                _config.Save();
            }

            if (settings.AutoResetEnabled)
            {
                ImGui.SameLine();
                var hours = settings.AutoResetHours;
                ImGui.SetNextItemWidth(90);
                if (ImGui.InputInt("hrs", ref hours))
                {
                    settings.AutoResetHours = Math.Clamp(hours, 1, 9);
                    _config.Save();
                }

                // Countdown so a reset is never a surprise.
                var last = _counter.GetLastKill(worldId, instance, def.MarkName);
                if (last is { } lastKill)
                {
                    var due = lastKill.AddHours(Math.Clamp(settings.AutoResetHours, 1, 9));
                    var remaining = due - DateTime.UtcNow;
                    ImGui.SameLine();
                    ImGui.TextDisabled(remaining > TimeSpan.Zero
                        ? $"resets in {FormatRemaining(remaining)}"
                        : "resetting…");
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Reset"))
            {
                _counter.ResetFor(def, worldId, instance);
            }
            if (def.TriggerPatterns.Length == 0 && _sync.CountersAvailable)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Reset shared…")) ImGui.OpenPopup("Reset shared counter");
                if (ImGui.BeginPopup("Reset shared counter"))
                {
                    ImGui.TextWrapped($"Clear the group counts for {def.MarkName} on {worldName}" +
                        (instance > 0 ? $" (instance {instance})?" : "?"));
                    if (ImGui.Button("Reset shared counts"))
                    {
                        _sync.ResetSharedCounters(def, worldId, instance);
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private static string FormatRemaining(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
        return "<1m";
    }

    private void DrawCounterPopout()
    {
        if (!_counterPopoutVisible) return;

        ImGui.SetNextWindowSize(new Vector2(300, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Hunt Counter", ref _counterPopoutVisible))
        {
            DrawSpawnWatches();

            DrawCounterList(
                currentZoneOnly: true,
                worldId: _counter.CurrentWorldId(),
                instance: MarkDetector.GetCurrentInstance(),
                worldName: _counter.CurrentWorldName());
        }
        ImGui.End();
    }

    /// <summary>
    /// The two live-state S-rank counters — Narrow-rift's Wee Ea headcount and
    /// Nunyunuwi's quiet-hour clock — shown only in the zone each applies to.
    /// Everything here is read straight off <see cref="_spawnWatch"/>, which
    /// keeps running regardless of whether this window is open.
    /// </summary>
    private static readonly Vector4 _counterGreen = new(0f, 1f, 0f, 1f);
    private static readonly Vector4 _counterWhite = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 _counterRed = new(1f, 0.4f, 0.4f, 1f);

    /// <summary>
    /// The live-state S-rank counter for the current zone — Narrow-rift's Wee
    /// Ea headcount in Ultima Thule, Nunyunuwi's quiet-hour clock in Southern
    /// Thanalan, nothing anywhere else. Returns whether it drew a row, so the
    /// caller can suppress its "nothing here" line.
    /// </summary>
    private bool DrawSpawnWatches()
    {
        var territory = _clientState.TerritoryType;

        if (territory == SpawnWatchCounters.UltimaThuleTerritory)
        {
            var count = _spawnWatch.WeeEaLoaded();
            var enough = count >= SpawnWatchCounters.NarrowRiftRequiredWeeEa;

            ImGui.TextUnformatted("Narrow-rift — Ultima Thule");
            ImGui.TextColored(
                enough ? _counterGreen : _counterWhite,
                $"    Wee Ea nearby: {count} / {SpawnWatchCounters.NarrowRiftRequiredWeeEa}");
            ImGui.TextDisabled("    Only minions your client has loaded — stand on the spawn point with the group.");
            ImGui.Separator();
            return true;
        }

        if (territory == SpawnWatchCounters.SouthernThanalanTerritory)
        {
            var remaining = _spawnWatch.NunyunuwiRemaining;
            var ready = remaining == TimeSpan.Zero;

            ImGui.TextUnformatted("Nunyunuwi — Southern Thanalan");
            ImGui.TextUnformatted(
                $"    Clean since {_spawnWatch.NunyunuwiSince:HH:mm:ss}, "
                + $"eligible {_spawnWatch.NunyunuwiEta:HH:mm:ss}");
            ImGui.TextColored(
                ready ? _counterGreen : _counterWhite,
                ready
                    ? "    Quiet hour complete — Nunyunuwi can spawn."
                    : $"    Quiet hour: {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} left");

            if (ImGui.SmallButton("Reset clock"))
                _spawnWatch.ResetNunyunuwiClock();
            ImGui.SameLine();
            ImGui.TextDisabled("if a FATE failed before you arrived");

            if (!string.IsNullOrEmpty(_spawnWatch.NunyunuwiLastFailure))
                ImGui.TextColored(_counterRed, $"    {_spawnWatch.NunyunuwiLastFailure}");

            var active = _spawnWatch.ActiveFates;
            if (active.Count == 0)
            {
                ImGui.TextDisabled("    No FATEs active.");
            }
            else
            {
                ImGui.TextDisabled($"    Active FATEs ({active.Count}) — don't let any fail:");
                foreach (var fate in active)
                {
                    var time = fate.AwaitingActivation
                        ? "awaiting activation"
                        : $"{(int)fate.TimeRemaining.TotalMinutes:00}:{fate.TimeRemaining.Seconds:00}";
                    ImGui.TextUnformatted($"      {fate.Name}  {fate.ProgressPercent}%  {time}");
                }
            }
            ImGui.Separator();
            return true;
        }

        return false;
    }

    /// <summary>
    /// What changed in each version, newest first.
    ///
    /// Grouped by area within a release rather than listed flat, because the
    /// question being asked is almost always "did anything change about the
    /// map" rather than "what happened in order".
    /// </summary>
    /// <summary>
    /// Puts the release notes up after an update, and only after an update.
    ///
    /// A fresh install records the version and shows nothing. Someone who has
    /// just chosen to install a plugin is not being told what changed since a
    /// version they never ran, and the window would land on top of a plugin
    /// they have not seen yet.
    ///
    /// The version is recorded whether or not the window was actually shown, so
    /// turning the setting off does not leave the plugin permanently convinced
    /// it still owes an update notice.
    /// </summary>
    private void ShowReleaseNotesIfUpdated()
    {
        try
        {
            var current = ReleaseNotes.CurrentVersion;
            var previous = _config.LastSeenReleaseVersion;

            if (!ReleaseNotes.IsNewerThan(current, previous))
                return;

            var freshInstall = string.IsNullOrWhiteSpace(previous);

            _config.LastSeenReleaseVersion = current;
            _config.Save();

            if (freshInstall)
            {
                _log.Information($"First install at {current}; not showing what's new.");
                return;
            }

            if (!_config.ShowReleaseNotesOnUpdate) return;

            _releaseNotesVisible = true;
            _log.Information($"Updated from {previous} to {current}; showing what's new.");
        }
        catch (Exception ex)
        {
            // A window that fails to open is not worth taking the plugin down.
            _log.Warning(ex, "Could not decide whether to show the release notes.");
        }
    }

    private void DrawReleaseNotesWindow()
    {
        if (!_releaseNotesVisible) return;

        ImGui.SetNextWindowSize(new Vector2(560, 520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(380, 240), new Vector2(float.MaxValue, float.MaxValue));

        if (!ImGui.Begin("Hunt Helper Evolved — what's new###HHEReleaseNotes", ref _releaseNotesVisible))
        {
            ImGui.End();
            return;
        }

        DrawReleaseNotesBody();
        ImGui.End();
    }

    private void DrawReleaseNotesBody()
    {
        ImGui.Spacing();

        if (ReleaseNotes.MissingCurrentVersion)
        {
            // Better to say so than to show the previous release as though it
            // were this one.
            ImGui.TextColored(
                new Vector4(1f, 0.6f, 0.3f, 1f),
                $"Running {ReleaseNotes.CurrentVersion}, which has no notes written for it yet.");
            ImGui.Spacing();
        }
        else
        {
            ImGui.TextDisabled($"Running {ReleaseNotes.CurrentVersion}.");
            ImGui.Spacing();
        }

        var notesOnUpdate = _config.ShowReleaseNotesOnUpdate;
        if (ImGui.Checkbox("Show this automatically after an update", ref notesOnUpdate))
        {
            _config.ShowReleaseNotesOnUpdate = notesOnUpdate;
            _config.Save();
        }
        ImGui.TextDisabled("Only after an update — never on a fresh install, and never on an ordinary login.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var newest = true;
        foreach (var release in ReleaseNotes.All)
        {
            // The newest opens on its own; everything older is there to go
            // looking for rather than to scroll past.
            if (newest) ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
            newest = false;

            var isRunning = release.Version == ReleaseNotes.CurrentVersion;
            var heading = isRunning
                ? $"{release.Version}  —  {release.Date}  (running)"
                : $"{release.Version}  —  {release.Date}";

            if (!ImGui.CollapsingHeader($"{heading}###release{release.Version}"))
                continue;

            ImGui.Indent();

            if (!string.IsNullOrEmpty(release.Summary))
            {
                ImGui.TextWrapped(release.Summary);
                ImGui.Spacing();
            }

            string? lastArea = null;
            foreach (var change in release.Changes)
            {
                if (change.Area != lastArea)
                {
                    if (lastArea != null) ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.55f, 0.78f, 1f, 1f), change.Area);
                    lastArea = change.Area;
                }

                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextWrapped(change.Text);

                var credit = change.Issue > 0
                    ? $"{change.Credit}  ·  issue #{change.Issue}"
                    : change.Credit;

                ImGui.Indent();
                ImGui.TextDisabled(credit);
                ImGui.Unindent();
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.55f, 0.78f, 1f, 1f), "Credits");
        ImGui.TextWrapped(
            "Hunt Train Relay by MusicManBowls and Hunt Tally by kihtli, merged and carried on here.");
        ImGui.TextWrapped(
            "Spawn point data, territory ids and the map's design come from Hunt Helper by img02, "
            + "used under the MIT licence. SS minion and mark coordinates are from Faloop.");
        ImGui.TextDisabled("Full notices are in THIRD-PARTY-NOTICES.md in the repository.");
    }

    private void DrawSRankWatches()
    {
        var automatic = _config.AutoTrainWatches;
        var supported = !_config.SyncShareTrain || _sync.SupportsScopedTrainWatches;
        ImGui.BeginDisabled(!supported && !automatic);
        if (ImGui.Checkbox("Automatically follow spawn windows", ref automatic))
        { _config.AutoTrainWatches = automatic; _config.Save(); }
        ImGui.EndDisabled();
        ImGui.TextWrapped(supported
            ? "Enable on the client preparing the train. Watches follow its worlds and zones; completed checks and manual watches are kept."
            : "Connect to an updated sync server to enable automatic train watches.");
        ImGui.Spacing();
        foreach (var (name, territoryId) in SimpleSRanks)
        {
            if (ImGui.Button($"Watch {name}"))
            {
                _config.Flags.Add(new FlagEntry
                {
                    Label = name,
                    TerritoryId = territoryId,
                });
                _config.Save();
            }
        }

        ImGui.Spacing();
        var spawnLabels = NarrowRiftSpawns.Select((s, i) => $"Spawn {i + 1} ({s.X:F1}, {s.Y:F1})").ToArray();
        ImGui.SetNextItemWidth(180);
        ImGui.Combo("##narrowRiftSpawn", ref _selectedNarrowRiftSpawn, spawnLabels, spawnLabels.Length);
        TrainControlSameLine("Watch Narrow-rift");
        if (ImGui.Button("Watch Narrow-rift"))
        {
            var spot = NarrowRiftSpawns[_selectedNarrowRiftSpawn];
            _config.Flags.Add(new FlagEntry
            {
                Label = $"Narrow-rift — Spawn {_selectedNarrowRiftSpawn + 1} ({spot.X:F1}, {spot.Y:F1})",
                TerritoryId = 960, // Ultima Thule
                HasLocation = true,
                X = spot.X,
                Y = spot.Y,
            });
            _config.Save();
        }

        ImGui.Spacing();

        int? toRemove = null;
        for (var i = 0; i < _config.Flags.Count; i++)
        {
            var flag = _config.Flags[i];
            ImGui.PushID(i);

            ImGui.TextWrapped(flag.Label + (flag.Automatic ? " (automatic)" : ""));

            DrawSpawnStatusBoxes(flag);
            ImGui.SameLine();
            ImGui.BeginDisabled(flag.Automatic && _config.AutoTrainWatches);
            if (ImGui.Button("Remove")) toRemove = i;
            ImGui.EndDisabled();

            ImGui.Separator();
            ImGui.PopID();
        }

        if (toRemove.HasValue)
        {
            _config.Flags.RemoveAt(toRemove.Value);
            _config.Save();
        }
    }

    private void DrawMarksSlainTab()
    {
        ImGui.Spacing();
        ImGui.TextWrapped("Individual history for the completion report above. Completed legs are reported; unfinished marks remain.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var marks = BuildCurrentMarks();
        if (marks == null)
        {
            ImGui.TextDisabled("No train history recorded.");
            return;
        }

        if (marks.Count == 0)
        {
            ImGui.TextDisabled("Nothing tracked yet — resume scanning to record marks.");
            return;
        }

        // Exactly what would be posted, which is only the expansions this train
        // actually killed something in. Previewing the unfiltered train would
        // show legs that the report is going to leave out.
        if (CanReportPartially) marks = TrainReport.ForReport(marks);
        if (marks.Count == 0)
        {
            ImGui.TextDisabled("Nothing killed yet — a report covers only the expansions the train has killed in.");
            return;
        }

        var entries = TrainReport.BuildEntries(marks);

        DrawReportEntries(entries.Where(e => !e.Sniped).ToList());

        // Its own section, exactly as the posted report has it — this is the
        // preview of that report, and a preview laid out differently from the
        // thing it previews is worse than none.
        var sniped = entries.Where(e => e.Sniped).ToList();
        if (sniped.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("Sniped (found gone — time is when the train got there)");
            ImGui.Spacing();
            DrawReportEntries(sniped);
        }

        foreach (var mark in marks.Where(m => !m.Dead || (m.DeathObservedAtUtc is null && m.SnipedAtUtc is null)))
            ImGui.TextWrapped($"{mark.Name}{ExpansionData.InstanceGlyph(mark.Instance)} — {mark.WorldName}: {(mark.Dead ? "found dead; kill time unknown" : "unfinished / still alive")}");
        var neverSeen = TrainReport.BuildSniped(marks);
        if (neverSeen.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("Missing / not seen this train (respawn time unknown)");
            foreach (var group in neverSeen)
            {
                ImGui.TextWrapped($"{group.WorldName} / {group.Expansion}: {string.Join(", ", group.Marks)}");
            }
        }
    }

    /// <summary>
    /// One run of report entries, split into expansion blocks. Shared by the
    /// killed and sniped sections for the same reason the posted report shares
    /// its own: the only difference between the two should be which marks are
    /// in them.
    /// </summary>
    private void DrawReportEntries(List<TrainReportEntry> entries)
    {
        string? lastExpansion = null;

        foreach (var entry in entries)
        {
            if (entry.Expansion != lastExpansion)
            {
                if (lastExpansion != null) ImGui.Spacing();
                ImGui.TextWrapped(entry.Expansion.ToUpperInvariant());
                lastExpansion = entry.Expansion;
            }

            var localTime = entry.KillTimeUtc.ToLocalTime().ToString("g");

            if (!entry.HasWindow)
            {
                ImGui.TextWrapped($"{localTime} — {entry.DisplayName} — no fixed respawn timer");
                continue;
            }

            var openLocal = entry.WindowOpensUtc!.Value.ToLocalTime().ToString("t");
            var capLocal = entry.WindowCapsUtc!.Value.ToLocalTime().ToString("t");
            var instanceGlyph = ExpansionData.InstanceGlyph(entry.Instance);
            ImGui.TextWrapped($"{localTime} — {entry.Location} — {entry.DisplayName}{instanceGlyph} — window {openLocal} → {capLocal}");
        }
    }

    /// <summary>
    /// Grouped into collapsible sections — there are enough toggles now that a
    /// single flat list is hard to scan.
    /// </summary>

    private void DrawWebhookList()
    {
        int? toRemove = null;

        for (var i = 0; i < _config.Webhooks.Count; i++)
        {
            ImGui.PushID(i);
            var hook = _config.Webhooks[i];

            var enabled = hook.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                hook.Enabled = enabled;
                _config.Save();
            }

            ImGui.SameLine();
            var label = hook.Label;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputTextWithHint("##label", "Label (optional)", ref label, 128))
            {
                hook.Label = label;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

            ImGui.SameLine();
            var url = hook.Url;
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputTextWithHint("##url", "Webhook URL", ref url, 512))
            {
                hook.Url = url;
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) _config.Save();

            if (_config.Webhooks.Count > 1)
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove"))
                {
                    toRemove = i;
                }
            }

            ImGui.PopID();
        }

        if (toRemove.HasValue)
        {
            _config.Webhooks.RemoveAt(toRemove.Value);
            if (_config.Webhooks.Count == 0) _config.Webhooks.Add(new WebhookEntry());
            _config.Save();
        }

        if (_config.Webhooks.Count < MaxWebhooks)
        {
            if (ImGui.Button("+ Add webhook"))
            {
                _config.Webhooks.Add(new WebhookEntry());
                _config.Save();
            }
        }
        else
        {
            ImGui.TextDisabled($"Maximum of {MaxWebhooks} webhooks reached.");
        }
    }

    // Draft text never enters configuration or the sync diff until explicitly added.
    private string _manualScoutDraft = string.Empty;
    private void DrawStringList(List<string> list, int maxCount, string addLabel, string maxReachedLabel)
    {
        string? toRemove = null;
        var names = list.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in names)
        {
            ImGui.PushID(name);
            ImGui.TextUnformatted(name);
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove")) toRemove = name;
            ImGui.PopID();
        }
        if (toRemove is not null)
        {
            list.RemoveAll(n => n.Trim().Equals(toRemove, StringComparison.OrdinalIgnoreCase));
            _sync.ChangeScoutCredit(toRemove, false);
            _config.Save();
        }
        if (names.Count >= maxCount && toRemove is null)
        {
            ImGui.TextDisabled(maxReachedLabel);
            return;
        }
        ImGui.SetNextItemWidth(Math.Max(80, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(addLabel).X - ImGui.GetStyle().FramePadding.X * 2 - ImGui.GetStyle().ItemSpacing.X));
        var submit = ImGui.InputTextWithHint("##manualScoutDraft", "Scout name", ref _manualScoutDraft,
            100, ImGuiInputTextFlags.EnterReturnsTrue);
        TrainControlSameLine(addLabel);
        var nameToAdd = _manualScoutDraft.Trim();
        var valid = nameToAdd.Length > 0 && !nameToAdd.Any(char.IsControl)
            && !list.Any(n => n.Trim().Equals(nameToAdd, StringComparison.OrdinalIgnoreCase));
        ImGui.BeginDisabled(!valid);
        var clicked = ImGui.Button(addLabel);
        ImGui.EndDisabled();
        if (valid && (submit || clicked))
        {
            list.RemoveAll(string.IsNullOrWhiteSpace);
            list.Add(nameToAdd);
            // An explicit add can restore a name removed earlier in this train.
            _sync.ChangeScoutCredit(nameToAdd, true);
            _manualScoutDraft = string.Empty;
            _config.Save();
        }
    }

    /// <summary>
    /// Which kill feed is driving auto-marking. The tally ships in this plugin
    /// now, so there is no connection to report — but there is still a choice
    /// of feed, and it is the thing that changes what shows up in the train.
    /// </summary>
    private string TallyFeedStatus() =>
        _tallyConfig.PublishAllMarkDeaths
            ? "Following every mark death, including ones killed by other people (Tally tab)."
            : "Following the marks you were credited with (Tally tab).";

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var cleanup = new CleanupSequence();
        // Retire public callbacks first. Each resource gets a cleanup attempt even
        // if an earlier component fails.
        cleanup.Run("_pluginInterface.UiBuilder.Draw", () => { _pluginInterface.UiBuilder.Draw -= DrawUI; });
        cleanup.Run("_pluginInterface.UiBuilder.OpenConfigUi", () => { _pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi; });
        cleanup.Run("_pluginInterface.UiBuilder.OpenMainUi", () => { _pluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow; });
        cleanup.Run("_framework.Update", () => { _framework.Update -= OnPluginFrameworkUpdate; });
        cleanup.Run("HuntTally.Service.Framework.Update", () => { HuntTally.Service.Framework.Update -= OnTallyFrameworkUpdate; });
        cleanup.Run("_clientState.Login", () => { _clientState.Login -= PauseScoutingOnLogin; });
        cleanup.Run("_clientState.Logout", () => { _clientState.Logout -= OnPluginLogout; });
        cleanup.Run("HuntTally.Service.ClientState.Login", () => { HuntTally.Service.ClientState.Login -= OnTallyLogin; });
        cleanup.Run("HuntTally.Service.ClientState.Logout", () => { HuntTally.Service.ClientState.Logout -= OnTallyLogout; });
        cleanup.Run("_watcher.PersistRequested", () => { _watcher.PersistRequested -= PersistTrain; });
        cleanup.Run("UnregisterCommands", () => { UnregisterCommands(); });
        cleanup.Run("_disposal.Cancel", () => { _disposal.Cancel(); });
        cleanup.Run("_tallyIpc.KillPublished", () => { _tallyIpc.KillPublished -= OnTallyKillPublished; });
        cleanup.Run("_tracker.OnKill", () => { _tracker.OnKill -= AnnounceTallyKill; });
        cleanup.Run("_tracker.OnMarkDeath", () => { _tracker.OnMarkDeath -= OnAnyMarkDeath; });
        cleanup.Run("_tracker.OnKill", () => { _tracker.OnKill -= _tallyIpc.PublishCredited; });
        cleanup.Run("_tracker.OnMarkDeath", () => { _tracker.OnMarkDeath -= _tallyIpc.PublishMarkDeath; });
        cleanup.Run("_tracker.Dispose", () => { _tracker.Dispose(); });
        cleanup.Run("_tallyIpc.Dispose", () => { _tallyIpc.Dispose(); });
        cleanup.Run("_damage.Dispose", () => { _damage.Dispose(); });
        cleanup.Run("_notifier.Dispose", () => _notifier.Dispose());
        cleanup.Run("_reward.Dispose", () => { _reward.Dispose(); });
        cleanup.Run("_seeder.Dispose", () => { _seeder.Dispose(); });
        cleanup.Run("_trainIpc.Dispose", () => { _trainIpc.Dispose(); });
        cleanup.Run("_tallyWindows.RemoveAllWindows", () => { _tallyWindows.RemoveAllWindows(); });
        cleanup.Run("_tallyWindow.Dispose", () => { _tallyWindow.Dispose(); });
        cleanup.Run("_tallyConfig.Flush", () => { _tallyConfig.Flush(force: true); });
        cleanup.Run("_characters.Dispose", () => { _characters.Dispose(); });
        cleanup.Run("_disposal.Dispose", () => { _disposal.Dispose(); });
        cleanup.Run("_watcher.Dispose", () => { _watcher.Dispose(); });
        cleanup.Run("_zoneReminder.Dispose", () => { _zoneReminder.Dispose(); });
        cleanup.Run("_counter.PersonalKill", () => { _counter.PersonalKill -= _sync.RecordCounterKill; });
        cleanup.Run("_counter.Dispose", () => { _counter.Dispose(); });
        cleanup.Run("_spawnWatch.Dispose", () => { _spawnWatch.Dispose(); });
        cleanup.Run("_mapOverlay.Dispose", () => { _mapOverlay.Dispose(); });
        cleanup.Run("_ssEvent.Dispose", () => { _ssEvent.Dispose(); });
        cleanup.Run("_sync.RemoteTrainCleared", () => { _sync.RemoteTrainCleared -= OnRemoteTrainCleared; });
        cleanup.Run("_sync.ReportedRemoval", () => { _sync.ReportedRemoval -= OnReportedRemoval; });
        cleanup.Run("_srankTravel.Dispose", () => { _srankTravel.Dispose(); });
        cleanup.Run("_sync.SRankSpawned", () => { _sync.SRankSpawned -= OnRemoteSRankSpawn; });
        cleanup.Run("_sync.Dispose", () => { _sync.Dispose(); });
        cleanup.Run("KamiToolKitLibrary.Dispose", () => { KamiToolKitLibrary.Dispose(); });
        cleanup.Run("_detector.OtherRankDetected", () => { _detector.OtherRankDetected -= OnSightingDetected; });
        cleanup.Run("_clientState.TerritoryChanged", () => { _clientState.TerritoryChanged -= _detector.ResetAnnouncements; });
        cleanup.Run("PersistTrain", () => { PersistTrain(); });
        cleanup.Run("Configuration", () => _config.Flush(force: true));
        try { cleanup.ThrowIfFailed(); }
        catch (AggregateException ex) { _log.Error(ex, "Plugin cleanup encountered errors."); }
    }

    // Sync

    private void OnSRankCommand(string command, string args) => _srankWindow.Toggle();

    /// <summary>
    /// Somebody else emptied the shared train. Everything that Reset does
    /// locally happens here too, minus the posting, so this client does not
    /// keep a pointer into a train that no longer exists.
    /// </summary>
    private readonly Sync.SpawnAlertFilter _spawnAlertFilter = new();
    private string _lastCommunityAlert = "No community spawn/release received this session.";

    private readonly Queue<Sync.SRankSpawnBroadcast> _pendingSpawnAlerts = new();
    private void OnRemoteSRankSpawn(Sync.SRankSpawnBroadcast spawn)
    {
        if (_objectTable.LocalPlayer is null || _detector.CurrentWorldId() == 0)
        {
            if (_pendingSpawnAlerts.Count >= 100) _pendingSpawnAlerts.Dequeue();
            _pendingSpawnAlerts.Enqueue(spawn);
            _lastCommunityAlert = "S-rank alert queued until loading finishes (up to two minutes).";
            return;
        }
        ShowSpawnAlert(spawn);
    }
    private void DrainPendingSpawnAlerts()
    {
        if (!_config.SyncEnabled) { _pendingSpawnAlerts.Clear(); return; }
        while (_pendingSpawnAlerts.TryPeek(out var pending) && DateTime.UtcNow - pending.SpawnedAt > TimeSpan.FromMinutes(2))
        { _pendingSpawnAlerts.Dequeue(); _lastCommunityAlert = "Queued S-rank alert expired during loading."; }
        if (_objectTable.LocalPlayer is null || _detector.CurrentWorldId() == 0) return;
        while (_pendingSpawnAlerts.TryDequeue(out var spawn)) ShowSpawnAlert(spawn);
    }
    private void ShowSpawnAlert(Sync.SRankSpawnBroadcast spawn, bool test = false)
    {
        _lastCommunityAlert = $"{DateTime.Now:HH:mm:ss}: {spawn.Event} received for mark {spawn.NameId}, world {spawn.WorldId}.";
        _log.Information(_lastCommunityAlert);
        if (!_config.SyncSpawnAlerts) { _lastCommunityAlert += " Alerts disabled."; return; }
        if (_objectTable.LocalPlayer == null) { _lastCommunityAlert += " No local player."; return; }
        if (!Sync.SRankTimerData.ByNameId.TryGetValue(spawn.NameId, out var mark)) { _lastCommunityAlert += " Unknown timed S rank."; return; }
        var destination = _worldData.LocateWorld(spawn.WorldId);
        var current = _worldData.LocateWorld(_detector.CurrentWorldId());
        if (destination is null || current is null) { _lastCommunityAlert += " Could not resolve world/DC."; return; }
        var dc = _worldData.DataCenters[destination.Value.DcIndex];
        var allowed = _config.SyncSpawnCurrentDc
            ? destination.Value.DcIndex == current.Value.DcIndex
            : _config.SyncSpawnDataCenters.Contains(dc.Id);
        if (!allowed) { _lastCommunityAlert += " Excluded by DC filter."; return; }
        if (!(test ? new Sync.SpawnAlertFilter() : _spawnAlertFilter).Accept(spawn, true, DateTime.UtcNow)) { _lastCommunityAlert += " Duplicate or invalid event time."; return; }
        if (!test && _detector.OtherRanks.TryGetValue((spawn.NameId,spawn.Instance,spawn.WorldId,0,0),out var local)
            && !local.IsRemote && DateTime.UtcNow-local.LastSeenUtc < TimeSpan.FromSeconds(2))
        { _lastCommunityAlert += " Already detected locally; relay suppressed."; return; }
        var position = SpawnPosition(spawn.X, spawn.Y);
        _notifier.SendRelay(new OtherRankSighting
        {
            NameId=spawn.NameId, Name=mark.Name, Rank=HuntRank.S, Instance=spawn.Instance,
            WorldId=spawn.WorldId, WorldName=_worldData.NameOf(spawn.WorldId), TerritoryId=mark.TerritoryId,
            MapId=_detector.GetMapId(mark.TerritoryId), MapPosition=position ?? Vector2.Zero,
            HealthPercent=float.NaN,
        },position is not null,test,spawn.Event=="release");
        _lastCommunityAlert += test ? " Test shown in chat." : " Shown in chat.";
        _log.Information(_lastCommunityAlert);
        if (_config.SyncSpawnSound)
        {
            try { FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect(6); }
            catch (Exception ex) { _log.Debug(ex, "Could not play S-rank alert sound."); }
        }
    }

    private static Vector2? SpawnPosition(float? x, float? y) => x is { } px && y is { } py
        && float.IsFinite(px) && float.IsFinite(py) && px >= 1 && px <= 100 && py >= 1 && py <= 100 ? new Vector2(px,py) : null;

    private void DrawTrainUndo()
    {
        if (_config.ResetUndoAt is not { } resetAt) return;
        if (ImGui.Button("Undo reset")) UndoTrainReset();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Restore {_config.ResetUndoMarks.Count} marks, {_config.ResetUndoReportHistory.Count} history entries and {_config.ResetUndoFlags.Count} watches from {resetAt.ToLocalTime():ddd HH:mm:ss} ({_config.ResetUndoBy}). No Shift required. Turns train sharing off and restores locally without changing your friends' train.");
        TrainControlSameLine("Restore locally; turns train sharing off");
        ImGui.PushTextWrapPos(0);
        ImGui.TextDisabled("Restore locally; turns train sharing off");
        ImGui.PopTextWrapPos();
    }

    private void ResetTrainWithUndo(bool clearWatches = true)
    {
        if (TrainMutationBusy) { _lastPostResult = "Wait for the report to finish before resetting."; return; }
        CaptureResetUndo("You");
        _ownResetPendingAt = _sync.IsConnected && _config.SyncShareTrain ? DateTime.UtcNow : null;
        _watcher.ResetNow();
        _currentMark = null;
        if (clearWatches) _config.Flags.Clear();
        ClearSavedTrain();
        _scoutNote.ObserveTrain(_detector.TrainGeneration);
        _lastPostResult = "Train reset — nothing was posted.";
        if (_config.ResetUndoAt is not null)
            _chatGui.Print("[Hunt Helper Evolved] Train reset. Use /hht > Setup > Undo reset to restore it locally.");
    }

    private DateTime? _ownResetPendingAt;
    private void CaptureResetUndo(string by)
    {
        var marks = _detector.ToPersisted();
        var history = BuildCurrentMarks();
        if (marks.Count == 0 && _config.Flags.Count == 0 && history.Count == 0) return;
        _config.ResetUndoReportHistory = history;
        _config.ResetUndoMarks = marks;
        _config.ResetUndoPresetRallies = (SharingPresetTrain ? _sync.TrainPresets.Rallies : _config.LocalPresetRallies).Copy();
        _config.ResetUndoFlags = CloneWatches(_config.Flags);
        _config.ResetUndoAt = DateTime.UtcNow;
        _scoutNoteUndo = _scoutNote.CaptureUndo(_detector.TrainGeneration);
        _scoutNoteUndoAt = _config.ResetUndoAt;
        _showTrainUndoNotice = true;
        _config.ResetUndoBy = by;
        _config.ResetUndoCurrentNameId = _currentMark?.NameId;
        _config.ResetUndoCurrentInstance = _currentMark?.Instance;
        _config.ResetUndoCurrentWorldId = _currentMark?.WorldId;
        _config.Save();
    }
    private static List<FlagEntry> CloneWatches(IEnumerable<FlagEntry> watches) => watches.Select(f => new FlagEntry
    { WorldId=f.WorldId, Instance=f.Instance, Automatic=f.Automatic, Label=f.Label, SpawnStatus=f.SpawnStatus, TerritoryId=f.TerritoryId, HasLocation=f.HasLocation, X=f.X, Y=f.Y }).ToList();

    private void UndoTrainReset()
    {
        if (_config.ResetUndoAt is null) return;
        if (TrainMutationBusy) { _lastPostResult = "Wait for the report to finish before restoring."; return; }
        var restoreNote = _scoutNoteUndoAt == _config.ResetUndoAt ? _scoutNoteUndo : null;
        // Stop applying/publishing train changes before restoring; sightings and timers stay connected.
        _config.SyncShareTrain = false;
        var restored = TrainResetRecovery.Merge(_config.ResetUndoMarks, _detector.ToPersisted(),
            m => (m.NameId,m.Instance,m.WorldId),
            m => new[]{m.LastSeenUtc,m.DeathObservedAtUtc ?? DateTime.MinValue,m.SnipedAtUtc ?? DateTime.MinValue}.Max());
        for (var i=0; i<restored.Count; i++) restored[i].Order=i;
        var watches = CloneWatches(_config.ResetUndoFlags);
        foreach (var current in CloneWatches(_config.Flags))
        {
            var index=watches.FindIndex(w=>w.Label==current.Label && w.TerritoryId==current.TerritoryId && w.X==current.X && w.Y==current.Y);
            if (index<0) watches.Add(current); else watches[index]=current;
        }
        var history = TrainResetRecovery.Merge(_config.ResetUndoReportHistory, BuildCurrentMarks(),
            m => m.Key, m => m.DeathObservedAtUtc ?? m.SnipedAtUtc ?? m.LastSeenUtc);
        _watcher.ResetNow();
        _watcher.RestoreHistory(history);
        _config.ResetUndoReportHistory.Clear();
        _detector.LoadPersisted(restored);
        if (restoreNote is not null) _scoutNote.TryRestoreUndo(restoreNote, _detector.TrainGeneration);
        else _scoutNote.ObserveTrain(_detector.TrainGeneration);
        _scoutNoteUndo = null;
        _scoutNoteUndoAt = null;
        _showTrainUndoNotice = false;
        _lastPostResult = "Train restored locally; train sharing is off.";
        _config.LocalPresetRallies = new()
        {
            Rows = _config.ResetUndoPresetRallies.Rows.Concat(_config.LocalPresetRallies.Rows)
                .DistinctBy(r => (r.NameId, r.Visit.Key.Instance, r.Visit.Key.WorldId)).ToList(),
            Completed = _config.ResetUndoPresetRallies.Completed.Concat(_config.LocalPresetRallies.Completed)
                .DistinctBy(v => v.Key).ToList(),
        };
        _config.ResetUndoPresetRallies = new();
        _config.Flags = watches;
        _currentMark = _config.ResetUndoCurrentNameId is { } name && _config.ResetUndoCurrentInstance is { } instance
            ? (name, instance, _config.ResetUndoCurrentWorldId ?? 0) : null;
        _config.ResetUndoMarks.Clear(); _config.ResetUndoFlags.Clear(); _config.ResetUndoAt=null;
        _config.Save(); PersistTrain();
        _chatGui.Print("[Hunt Helper Evolved] Train reset undone locally. Train sharing is off; the group's current train is unchanged.");
    }

    /// <summary>
    /// Clears what a posted report consumed and leaves the rest of the train
    /// standing.
    ///
    /// What goes: the dead marks in the expansions the report covered, out of
    /// both the list and the retained report history that would otherwise put
    /// them in the next report. What stays: every leg the train did not run,
    /// which is the whole point - a conductor who scouts five expansions and
    /// runs three keeps the other two, already scouted, for later. Live marks
    /// on a reported leg stay too: still standing is not reported as anything.
    ///
    /// <paramref name="partial"/> false means the report covered the whole
    /// train, so this is the old wholesale reset. That happens against a sync
    /// server that cannot clear part of its shared train.
    /// </summary>
    private void ClearReportedMarks(List<TrackedMark> submitted, List<FlagEntry> keptWatches, bool partial)
    {
        if (!partial)
        {
            _watcher.ResetNow();
            _currentMark = null; _config.Flags.Clear();
            _config.AdditionalScouts.Clear(); _config.ScanningPaused = true;
            ClearSavedTrain();
            return;
        }

        _watcher.ForgetReported(submitted.Select(m => m.Key));
        _config.Flags = keptWatches;
        ApplyLocalTrainPreset(force: true);
        if (_detector.Marks.Count == 0 && keptWatches.Count == 0)
        { _config.AdditionalScouts.Clear(); _config.ScanningPaused = true; _scoutNote.DetachCurrent(_detector.TrainGeneration); }
        if (_currentMark is { } current && submitted.Any(m => m.Key == current)) _currentMark = null;
        _config.Save();
        PersistTrain();
    }

    /// <summary>Says what a partial report left behind, so it is never a surprise.</summary>
    private static string DescribeKept(List<TrackedMark> before, List<TrackedMark> submitted)
    {
        var kept = before.Count - submitted.Count;
        return kept == 0
            ? "the whole train was reported and cleared."
            : $"{submitted.Count} reported mark(s) cleared, {kept} kept for a later train.";
    }

    /// <summary>
    /// The shared train dropped rows because somebody posted a report covering
    /// them. Mirrors <see cref="ClearReportedMarks"/> for everyone who was not
    /// the one posting, and keeps their report history from carrying the same
    /// kills into the next report.
    ///
    /// The watches are deliberately not touched here: the server sends its own
    /// watch state alongside the removal, and that is what settles them.
    /// </summary>
    private void OnReportedRemoval(List<ReportedMark> reported)
    {
        var keys = ReportedHistory.CompletedKeys(BuildCurrentMarks(), reported);
        if (keys.Count == 0) return;
        var ownEcho = _ownResetPendingAt is { } at && DateTime.UtcNow - at < TimeSpan.FromSeconds(30);
        if (!ownEcho) CaptureResetUndo("Report completed");
        _ownResetPendingAt = null;

        _watcher.ForgetReported(keys);
        if (_detector.Marks.Count == 0) _scoutNote.DetachCurrent(_detector.TrainGeneration);
        if (_currentMark is { } current && keys.Contains(current)) _currentMark = null;
        _config.Save();
        PersistTrain();
    }

    private void OnRemoteTrainCleared(string by)
    {
        var ownEcho = _ownResetPendingAt is { } at && DateTime.UtcNow-at < TimeSpan.FromSeconds(30) && by == _sync.DisplayName();
        if (!ownEcho) CaptureResetUndo(by);
        if (ownEcho) _ownResetPendingAt=null;
        _watcher.ResetNow();
        _scoutNote.ObserveTrain(_detector.TrainGeneration);
        _currentMark = null;
        _config.Flags.Clear();
        _config.Save();
        ClearSavedTrain();
        _chatGui.Print($"[Hunt Helper Evolved] {by} cleared the shared train. Use /hht > Setup > Undo reset to recover it locally.");
    }

    /// <summary>
    /// A mark died where this client could see it. Its dot comes off the
    /// group's maps; an S rank also starts the group's respawn clock.
    /// </summary>
    private void OnAnyMarkDeath(KillDetail kill)
    {
        _detector.ResetAnnouncementOnKill(kill.Mark.NameId, kill.InstanceId, _detector.CurrentWorldId());
        try
        {
            _sync.ReportMarkDeath(
                kill.Mark.NameId,
                kill.InstanceId,
                kill.TerritoryId,
                kill.Time.ToUniversalTime(),
                kill.Mark.Rank == MarkRank.S);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not report a mark death to the sync server.");
        }
    }

    private bool _showSyncPassword;

}
