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

namespace HuntHelperEvolved;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Hunt Helper Evolved";

    private const string ConfigCommand = "/htr";
    private const string TrainCommand = "/htrt";
    private const string CounterCommand = "/htrc";
    private const string NextAetheryteCommand = "/htra";
    private const string MapCommand = "/htrm";

    /// <summary>
    /// Hunt Helper's own commands, taken over only when Hunt Helper itself is
    /// not installed, so this plugin is a drop-in for someone who has replaced
    /// it and still has the muscle memory.
    ///
    /// Its /hh1, /hh2, /hh1save and /hh2save save and apply map-window presets,
    /// and /hhr opens a spawn point recorder. There is nothing here that does
    /// either, so those are left unclaimed rather than answered with an
    /// apology — typing one gets the game's ordinary unknown-command reply, and
    /// the names stay free if those features ever arrive.
    /// </summary>
    private static readonly (string Command, string Help)[] HuntHelperAliases =
    {
        ("/hh", "Open the main window. Hunt Helper's own command, taken over because it isn't installed."),
        ("/hht", "Open the train list popout. Hunt Helper's /hht."),
        ("/hhn", "Move to the next live mark in the train and flag it. Hunt Helper's /hhn."),
        ("/hhna", "Name the closest aetheryte to the next mark. Hunt Helper's /hhna."),
        ("/hhc", "Open the trigger-mob counter popout. Hunt Helper's /hhc."),
    };

    /// <summary>Which aliases were actually claimed, so Dispose gives back exactly those.</summary>
    private readonly List<string> _claimedAliases = new();

    /// <summary>
    /// The tally's original command, kept verbatim. It was a separate plugin
    /// until this release and people have it in macros and muscle memory, so
    /// merging must not be the thing that breaks it.
    /// </summary>
    private const string TallyCommand = "/hunttally";
    private const int MaxWebhooks = 5;
    private const int MaxAdditionalScouts = 3;

    // The only S-ranks the group actually checks for during trains.
    private static readonly string[] SimpleSRanks = { "Ophioneus", "Tyger" };

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
    private readonly HuntHelperIpc _ipc;
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

    /// <summary>
    /// True when the standalone Hunt Tally plugin is also loaded, which the
    /// merged build has to treat as an error rather than a duplicate.
    /// </summary>
    private bool _standaloneTallyPresent;

    private bool _configWindowVisible;
    private bool _trainPopoutVisible;
    private bool _counterPopoutVisible;
    private string _importCode = string.Empty;
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

    // The mark the conductor is currently on. Tracked by identity rather than
    // list position, so dragging rows or removing marks can't silently change
    // what "current" points at.
    // Keyed the same way marks are, world included — the same mark on two
    // worlds is two marks, and the pointer has to say which.
    private (uint NameId, uint Instance, uint WorldId)? _currentMark;

    // Measured on the previous frame. The drag threshold has to match the real
    // on-screen row pitch (selectable + buttons + separator), not just a line of
    // text — using a smaller value makes each swap jump further than the cursor
    // moved, so the row visibly outruns the mouse.
    private string _lastPostResult = string.Empty;
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
        _commandManager = commandManager;
        _chatGui = chatGui;
        _objectTable = objectTable;
        _log = pluginLog;

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
        var loaded = _pluginInterface.GetPluginConfig() as Configuration
                     ?? Configuration.LoadDirect(_pluginInterface);

        var migrated = false;
        if (loaded is null)
        {
            loaded = Configuration.LoadFromPreviousName(_pluginInterface);
            migrated = loaded is not null;
        }

        _config = loaded ?? new Configuration();
        _config.Initialize(_pluginInterface);

        if (migrated)
        {
            // Write it out under the new name straight away, so the hand-over
            // happens once rather than on every load until something else
            // saves.
            _config.Save();
            _log.Information("Carried settings over from the Hunt Train Relay config file.");
        }

        ShowReleaseNotesIfUpdated();

        _ipc = new HuntHelperIpc(_pluginInterface);
        _gameGui = gameGui;
        _textureProvider = textureProvider;
        _clientState = clientState;
        _detector = new MarkDetector(objectTable, clientState, dataManager, _config);
        _teleport = new TeleportHelper(_pluginInterface, _log, dataManager);
        SyncBlacklist();
        _watcher = new TrainWatcher(framework, _ipc, _detector, _config, chatGui, _log);

        // The tally reaches Dalamud through its own injected service class
        // rather than this constructor's parameters, which is how it was built
        // as a standalone plugin. Left that way on purpose: it keeps the merge
        // to a wiring change, so the counting code that people's existing
        // totals were built by is the same code, untouched.
        _pluginInterface.Create<HuntTally.Service>();
        DetectStandaloneTally();
        _tallyConfig = TallyConfigStore.Load(_pluginInterface);

        _characters = new CharacterContext(_tallyConfig);
        _seeder = new AchievementSeeder(_tallyConfig, _characters);

        // Constructed before the tracker: the tracker asks it on every poll
        // whether the precise signal is available.
        _damage = new DamageWatch();
        _reward = new RewardWatch();

        _tallyWindow = new MainWindow(_tallyConfig, _characters);
        _tallyWindows.AddWindow(_tallyWindow);

        _tracker = new KillTracker(_tallyConfig, _characters, _damage, _reward);
        _tallySettings = new TallySettingsPanel(
            _tallyConfig, _seeder, _characters, _damage, _reward, _tracker);
        _tallyIpc = new IpcProvider(_tallyConfig);

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
            _tracker.OnKill += _tallyIpc.PublishCredited;
            _tracker.OnMarkDeath += _tallyIpc.PublishMarkDeath;
            _tracker.OnKill += AnnounceTallyKill;

            // Off the publisher rather than the tracker, so the train sees
            // exactly the feed an external subscriber would have seen over IPC —
            // including the tally's own switch between credited kills and every
            // mark death. That is what the auto-mark behaviour was built
            // against, and keeping the same source is what stops the merge
            // quietly changing it.
            _tallyIpc.KillPublished += OnTallyKillPublished;

            HuntTally.Service.ClientState.Login += OnTallyLogin;
            HuntTally.Service.ClientState.Logout += OnTallyLogout;
            HuntTally.Service.Framework.Update += OnTallyFrameworkUpdate;

            // Resolving walks the whole Achievement sheet. One tick later costs
            // nothing and keeps it off the plugin-load path.
            HuntTally.Service.Framework.RunOnTick(
                _seeder.ResolveAll, TimeSpan.Zero, 0, _disposal.Token);

            if (HuntTally.Service.ClientState.IsLoggedIn && _tallyConfig.AutoSeedOnLogin)
                ScheduleTallySeed();
        }

        _notifier = new MarkNotifier(chatGui, flyTextGui, _log, _config);
        _zoneReminder = new SRankZoneReminder(clientState, chatGui, _log, _config, _detector);
        _counter = new HuntCounter(chatGui, clientState, objectTable, _config);
        _spawnWatch = new SpawnWatchCounters(framework, clientState, objectTable, fateTable, _log);
        _worldData = new WorldData(dataManager);

        // After the detector exists, since the gates read straight off it.
        _trainIpc = new TrainIpcProvider(_pluginInterface, _detector, _log);

        // KamiToolKit needs one-time initialisation before any of its
        // controllers can be enabled — without it, AddonController.Enable()
        // throws a null reference on every frame.
        try
        {
            KamiToolKitLibrary.Initialize(_pluginInterface, "Hunt Helper Evolved");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "KamiToolKit failed to initialise; the map overlay will stay off.");
        }

        _ssEvent = new SsEventWatcher(chatGui, clientState, _log, _detector);
        _mapOverlay = new HuntMapOverlay(framework, clientState, objectTable, dataManager, addonLifecycle, gameGui, _log, _config, _detector, _ssEvent, _pluginInterface);
        _detector.OtherRankDetected += OnSightingDetected;
        _watcher.PersistRequested += PersistTrain;
        RestoreSavedTrain();

        _commandManager.AddHandler(ConfigCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Hunt Helper Evolved settings.",
        });

        _commandManager.AddHandler(TrainCommand, new CommandInfo(OnTrainCommand)
        {
            HelpMessage = "Open the Hunt Helper Evolved train list popout.",
        });

        _commandManager.AddHandler(CounterCommand, new CommandInfo(OnCounterCommand)
        {
            HelpMessage = "Open the Hunt Helper Evolved mob counter popout.",
        });

        _commandManager.AddHandler(NextAetheryteCommand, new CommandInfo(OnNextAetheryteCommand)
        {
            HelpMessage = "Name the closest aetheryte to the next mark in the train.",
        });

        _commandManager.AddHandler(MapCommand, new CommandInfo(OnMapCommand)
        {
            HelpMessage = "Open the map dot filters.",
        });

        RegisterHuntHelperAliases();

        _commandManager.AddHandler(TallyCommand, new CommandInfo(OnTallyCommand)
        {
            HelpMessage = "Open the hunt tally. \"/hunttally config\" for settings, "
                          + "\"/hunttally ipc\" to test the IPC feed.",
        });

        _pluginInterface.UiBuilder.Draw += DrawUI;
        _pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;

        // The tally's display window is the plugin's "main" UI, as it was when
        // the tally was its own plugin. The gear opens settings, which are now
        // a tab of this plugin's config window.
        _pluginInterface.UiBuilder.OpenMainUi += ToggleTallyWindow;
    }

    // ---------------------------------------------------------------------
    // Tally
    // ---------------------------------------------------------------------

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
    private void OnTallyFrameworkUpdate(IFramework framework) => _tallyConfig.Flush();

    private void OnTallyLogin()
    {
        if (_tallyConfig.AutoSeedOnLogin)
            ScheduleTallySeed();
    }

    private void OnTallyLogout(int type, int code) => _tallyConfig.Flush(force: true);

    /// <summary>
    /// RunOnTick rather than Task.Delay: the continuation of a Task runs on a
    /// thread-pool thread, and the seeder's state is read on the framework
    /// thread. It is also cancelled on dispose, so a plugin unloaded inside the
    /// delay does not start seeding afterwards.
    /// </summary>
    private void ScheduleTallySeed() =>
        HuntTally.Service.Framework.RunOnTick(_seeder.Start, LoginSeedDelay, 0, _disposal.Token);

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
            new DateTimeOffset(kill.Time).ToUnixTimeSeconds()));
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
        ImGui.TextWrapped("What happens the moment a mark is first spotted. All of it is local — nothing here is sent to anyone else.");
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

            DrawMessageBox("B message##chat", _config.DetectionChatMessageB, v => _config.DetectionChatMessageB = v);
            DrawMessageBox("A message##chat", _config.DetectionChatMessageA, v => _config.DetectionChatMessageA = v);
            DrawMessageBox("S message##chat", _config.DetectionChatMessageS, v => _config.DetectionChatMessageS = v);

            DrawPlaceholderHelp();
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // ---- Fly text ----
        var fly = _config.DetectionFlyTextEnabled;
        if (ImGui.Checkbox("Throw it up as fly text", ref fly))
        {
            _config.DetectionFlyTextEnabled = fly;
            _config.Save();
        }
        ImGui.TextDisabled("On your own character, in the channel a crit lands in — the one thing here you can't miss while running.");

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
            ImGui.TextDisabled("The rank and the name are drawn in Hunt Helper's own two colours; there is nothing to set.");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // ---- Speech ----
        var tts = _config.DetectionTtsEnabled;
        if (ImGui.Checkbox("Say it out loud", ref tts))
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

            DrawMessageBox("B message##tts", _config.DetectionTtsMessageB, v => _config.DetectionTtsMessageB = v);
            DrawMessageBox("A message##tts", _config.DetectionTtsMessageA, v => _config.DetectionTtsMessageA = v);
            DrawMessageBox("S message##tts", _config.DetectionTtsMessageS, v => _config.DetectionTtsMessageS = v);

            ImGui.TextDisabled("<name>, <rank> and <hpp> are spoken. Everything else — flags and icons — is dropped rather than read out.");

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

    private static void DrawPlaceholderHelp()
    {
        if (!ImGui.TreeNode("What can go in a message"))
            return;

        ImGui.TextDisabled("<name>   the mark, coloured by rank");
        ImGui.TextDisabled("<rank>   A-Rank / B-Rank / S-Rank");
        ImGui.TextDisabled("<hpp>    health, green above 99% down to red below 70%");
        ImGui.TextDisabled("<flag>   a clickable map link to where it was found");
        ImGui.Spacing();
        ImGui.TextDisabled("Icons: <goldstar> <silverstar> <warning> <nocircle> <alarm>");
        ImGui.TextDisabled("<notoriousmonster> <exclamationrectangle> <priorityworld>");
        ImGui.TextDisabled("<elementallevel> <fanfestival> <controllerbutton0> <controllerbutton1>");
        ImGui.Spacing();
        ImGui.TextDisabled("Same names Hunt Helper uses, so a message pasted from it reads the same.");

        ImGui.TreePop();
    }

    /// <summary>
    /// Voice and volume, plus a way to hear it. The voice list is asked for
    /// once and kept: enumerating them builds a synthesiser, which is not
    /// something to do every frame the settings window is open.
    /// </summary>
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

    private void OnSightingDetected(OtherRankSighting sighting) => _notifier.Announce(sighting);

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

    /// <summary>
    /// Builds the current merged mark set — Hunt Helper's live list plus anything
    /// the background tracker already recorded that's no longer in that live list
    /// (e.g. cleared away mid-train with Remove Dead). Returns null if Hunt Helper
    /// isn't detected at all.
    /// </summary>
    private List<TrackedMark>? BuildCurrentMarks()
    {
        if (_config.UseOwnTrainList)
        {
            return _detector.Ordered().Where(d => !d.IsCustom).Select(d => new TrackedMark
            {
                Name = d.Name,
                ModelId = d.NameId,
                Instance = d.Instance,
                Dead = d.Dead,
                LastSeenUtc = d.LastSeenUtc,
                DeathObservedAtUtc = d.DeathObservedAtUtc,
            }).ToList();
        }

        var list = _ipc.TryGetTrainList();
        if (list == null) return null;

        var now = DateTime.UtcNow;
        var tracked = _watcher.GetTrackedSnapshot();

        var marks = list.Select(m => new TrackedMark
        {
            Name = m.Name,
            ModelId = m.MobID,
            Instance = m.Instance,
            Dead = m.Dead,
            LastSeenUtc = m.LastSeenUTC,
            DeathObservedAtUtc = m.Dead
                ? (tracked.TryGetValue((m.MobID, m.Instance), out var t) ? t.DeathObservedAtUtc : null) ?? now
                : null,
        }).ToList();

        var seenKeys = marks.Select(m => (m.ModelId, m.Instance)).ToHashSet();
        foreach (var (key, trackedMark) in tracked)
        {
            if (!seenKeys.Contains(key))
                marks.Add(trackedMark);
        }

        return marks;
    }

    private async Task SendTestAsync()
    {
        var (success, message) = await DiscordRelay.PostTestAsync(_config.Webhooks);
        _lastPostResult = message;
        if (!success) _log.Error($"Hunt Helper Evolved test post failed: {message}");
    }

    private async Task SendScoutingReportAsync()
    {
        List<HuntHelperMobRecord>? list;

        if (_config.UseOwnTrainList)
        {
            list = _detector.Ordered().Where(d => !d.IsCustom).Select(d => new HuntHelperMobRecord(
                d.Name, d.NameId, d.TerritoryId, d.MapId, d.Instance,
                d.MapPosition, d.Dead, d.LastSeenUtc)).ToList();
        }
        else
        {
            list = _ipc.TryGetTrainList();
        }

        if (list == null)
        {
            _lastPostResult = "Hunt Helper not detected — can't build a scouting report.";
            return;
        }

        var names = new List<string>();
        var selfName = _objectTable.LocalPlayer?.Name?.TextValue;
        if (!string.IsNullOrWhiteSpace(selfName)) names.Add(selfName);
        names.AddRange(_config.AdditionalScouts.Where(n => !string.IsNullOrWhiteSpace(n)));

        var ownCode = _config.UseOwnTrainList ? TrainExchange.Export(_detector.Ordered()) : null;

        var (success, message) = await DiscordRelay.PostScoutingReportAsync(_config.Webhooks, list, names, ownCode);
        _lastPostResult = message;
        if (!success) _log.Error($"Hunt Helper Evolved scouting report failed: {message}");
    }

    /// <summary>
    /// The only way a "Train Complete" report ever gets posted — reads the
    /// current merged mark set and posts it sorted by the actual order things
    /// died, plus any S-rank check results. Tracking and the watch list only
    /// clear once the post is confirmed to have actually succeeded — if it
    /// fails, everything stays put so this can just be tried again.
    /// </summary>
    private async Task EndTrainNowAsync()
    {
        var marks = BuildCurrentMarks();
        if (marks == null)
        {
            _lastPostResult = "Hunt Helper not detected — nothing to post.";
            return;
        }

        if (marks.Count == 0)
        {
            _lastPostResult = "Nothing to post — Hunt Helper's train list is empty.";
            return;
        }

        var endedBy = _objectTable.LocalPlayer?.Name?.TextValue;

        var (success, message) = await DiscordRelay.PostTrainCompleteAsync(_config.Webhooks, marks, endedBy, _config.Flags);
        _lastPostResult = message;

        if (success)
        {
            _chatGui.Print($"[Hunt Helper Evolved] Posted train summary to Discord ({marks.Count} marks).");
            _watcher.ResetNow();
            _config.Flags.Clear();
            _config.Save();
            ClearSavedTrain();
        }
        else
        {
            _chatGui.PrintError($"[Hunt Helper Evolved] Failed to post to Discord: {message}");
            _log.Error($"Hunt Helper Evolved manual end-train post failed: {message}");
        }
    }

    private void DrawUI()
    {
        // Cheap enough to check on a slow tick; counters age out in hours.
        _secondsSinceAutoResetCheck += ImGui.GetIO().DeltaTime;
        if (_secondsSinceAutoResetCheck >= 30)
        {
            _secondsSinceAutoResetCheck = 0;
            _counter.ApplyAutoResets();
        }

        ProcessPendingCustomRemovals();
        UpdateAutoAdvance();
        DrawTrainPopout();
        DrawCounterPopout();
        DrawReleaseNotesWindow();
        DrawMapControlBar();

        // Before the early return below: the tally's window is independent of
        // the config window and has to keep drawing while that one is shut.
        _tallyWindows.Draw();

        if (!_configWindowVisible) return;

        ImGui.SetNextWindowSize(new Vector2(620, 560), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 240), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("Hunt Helper Evolved", ref _configWindowVisible))
        {
            if (ImGui.BeginTabBar("HuntHelperEvolvedTabs"))
            {
                if (ImGui.BeginTabItem("Conductor"))
                {
                    DrawConductorTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Train"))
                {
                    DrawTrainTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Scout"))
                {
                    DrawScoutTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Marks Slain"))
                {
                    DrawMarksSlainTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }

                // The tally's settings, which were their own window until the
                // two plugins merged. Flagged for selection only when something
                // asked for it by name, so it does not steal focus otherwise.
                var tallyFlags = _selectTallyTab
                    ? ImGuiTabItemFlags.SetSelected
                    : ImGuiTabItemFlags.None;
                _selectTallyTab = false;

                if (ImGui.BeginTabItem("Tally", tallyFlags))
                {
                    DrawTallyTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            if (!string.IsNullOrEmpty(_lastPostResult))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextWrapped($"Last post: {_lastPostResult}");
            }
        }
        ImGui.End();
    }


    /// <summary>
    /// Our own detected train list, with per-row teleport and map-flag actions.
    /// Drawn in both the Train tab and the standalone popout.
    /// </summary>

    /// <summary>
    /// The tally's settings, scrolled in their own region.
    ///
    /// The panel was written for a 500x600 window of its own and is taller than
    /// the tab area, so it gets a child to scroll in rather than being
    /// redesigned — the controls and their explanations are unchanged from the
    /// standalone plugin.
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

        ImGui.Spacing();
        if (ImGui.Button("Open the tally"))
            _tallyWindow.IsOpen = true;
        ImGui.SameLine();
        ImGui.TextDisabled("Also \"/hunttally\".");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginChild("##tallysettings", new Vector2(0, 0), false))
        {
            _tallySettings.Draw();
        }
        ImGui.EndChild();
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
    private DetectedMark? NextLiveMark()
    {
        var ordered = _detector.Ordered();
        if (ordered.Count == 0) return null;

        var startIndex = 0;
        if (_currentMark is { } key)
        {
            var idx = ordered.FindIndex(m => m.Key == key);
            if (idx >= 0) startIndex = idx + 1;
        }

        for (var i = startIndex; i < ordered.Count; i++)
            if (!ordered[i].Dead) return ordered[i];

        return null;
    }

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
    private void ProcessPendingCustomRemovals()
    {
        if (_pendingCustomRemovals.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var (key, dueAt) in _pendingCustomRemovals.ToList())
        {
            if (now < dueAt) continue;

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
    /// Claims Hunt Helper's commands, but only when Hunt Helper is not
    /// installed — two plugins cannot hold the same command, and the one that
    /// owns it should be the one it belongs to.
    ///
    /// Each is registered on its own rather than as a batch. Dalamud refuses a
    /// command that is already taken, and some other plugin may well have
    /// claimed one of these in Hunt Helper's absence; losing /hh to that is no
    /// reason to also lose /hhc.
    /// </summary>
    private void RegisterHuntHelperAliases()
    {
        if (HuntHelperIpc.IsHuntHelperInstalled(_pluginInterface))
        {
            _log.Information(
                "Hunt Helper is installed, so its /hh commands are left alone.");
            return;
        }

        foreach (var (command, help) in HuntHelperAliases)
        {
            try
            {
                var handler = command switch
                {
                    "/hh" => new IReadOnlyCommandInfo.HandlerDelegate(OnCommand),
                    "/hht" => OnTrainCommand,
                    "/hhn" => OnHuntHelperNextCommand,
                    "/hhna" => OnNextAetheryteCommand,
                    "/hhc" => OnCounterCommand,
                    _ => null,
                };

                if (handler == null) continue;

                _commandManager.AddHandler(command, new CommandInfo(handler) { HelpMessage = help });
                _claimedAliases.Add(command);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, $"Could not take over {command}; something else holds it.");
            }
        }

        if (_claimedAliases.Count > 0)
        {
            _log.Information(
                $"Hunt Helper is not installed; answering to {string.Join(", ", _claimedAliases)}.");
        }
    }

    /// <summary>
    /// Hunt Helper's /hhn: move to the next live mark and flag it.
    ///
    /// Its own version also ticks the current mark dead on the way past. This
    /// one does not, deliberately. Marks are marked dead here by watching the
    /// kill happen, and those timings are what the train report is built from —
    /// a mistyped /hhn should not be able to write a kill that never occurred.
    /// </summary>
    private void OnHuntHelperNextCommand(string command, string args)
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

    /// <summary>
    /// Scanning play/pause plus the tidy-up actions. Shown on both the Train
    /// tab and the popout, so a conductor working from the popout alone still
    /// has everything they need mid-train.
    /// </summary>


    /// <summary>
    /// Surfaces a problem everywhere it might be looked for: the status line in
    /// the main window, the local chat log, and /xllog. Teleport errors were
    /// previously only written to a field rendered inside the main window, so
    /// clicking teleport from the popout failed in complete silence.
    /// </summary>
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
        var marks = _detector.ToPersisted();

        // Nothing to save and nothing saved: don't churn the config file.
        if (marks.Count == 0 && _config.SavedTrain.Count == 0) return;

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
        _config.SavedTrain.Clear();
        _config.SavedTrainAtUtc = null;
        _config.SavedCurrentNameId = null;
        _config.SavedCurrentInstance = null;
        _config.SavedCurrentWorldId = null;
        _config.Save();
    }

    private void ReportProblem(string message)
    {
        _lastPostResult = message;
        _chatGui.PrintError($"[Hunt Helper Evolved] {message}");
        _log.Warning(message);
    }

    /// <summary>
    /// The actions that actually send something, or throw a train away. All
    /// three require Shift to be held: they're irreversible enough that a
    /// stray click mid-train is genuinely costly.
    ///
    /// Dimming and ignoring clicks manually rather than using ImGui's
    /// BeginDisabled — same result, and it avoids an API this project has
    /// deliberately steered clear of.
    /// </summary>
    private void DrawTrainFooter()
    {
        var armed = ImGui.GetIO().KeyShift;

        if (!armed) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.45f);

        if (ImGui.Button("Send Scouting Report") && armed)
        {
            _ = SendScoutingReportAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button("End Train Now") && armed)
        {
            _ = EndTrainNowAsync();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.35f, 0.35f, 1f));
        var resetPressed = ImGui.Button("Reset");
        ImGui.PopStyleColor();

        if (resetPressed && armed)
        {
            _watcher.ResetNow();
            _detector.Clear();
            _currentMark = null;
            _config.Flags.Clear();
            _config.Save();
            ClearSavedTrain();
            _lastPostResult = "Train reset — nothing was posted.";
        }

        if (!armed) ImGui.PopStyleVar();

        if (!armed)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(hold Shift)");
        }
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
        ImGui.TextDisabled("How far marks are actually picked up — two map coordinates, the same radius Hunt Helper draws. Fixed, because the number is the point.");

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
            ImGui.TextDisabled("1.00 is the real detection range. Anything else is a circle you like the size of, not a range — and the path widens with it.");

            var thickness = _config.PlayerCircleThickness;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Circle line width", ref thickness, 1f, 40f, "%.0f"))
            {
                _config.PlayerCircleThickness = Math.Clamp(thickness, 1f, 40f);
                _config.Save();
            }
            ImGui.TextDisabled("Drawn into the ring, so it thickens with the map's zoom rather than staying a flat number of pixels. 8 matches Hunt Helper.");
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
        ImGui.TextDisabled("The swathe ahead of you that your detection range will sweep. As wide as the circle, and runs off the edge of the map. Keep it translucent — it covers everything under it.");

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
            _config.SpawnDotColourB = defaults.SpawnDotColourB;
            _config.SpawnDotColourA = defaults.SpawnDotColourA;
            _config.SpawnDotColourS = defaults.SpawnDotColourS;
            _config.SsMinionColour = defaults.SsMinionColour;
            _config.MarkLabelColour = defaults.MarkLabelColour;
            _config.MarkLabelOutlineColour = defaults.MarkLabelOutlineColour;
            _config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Back to the original grey/blue/red/green.");
    }

    /// <summary>
    /// The map controls, pinned to the top edge of the game's own map window
    /// and shown only while that map is open.
    ///
    /// Deliberately one row tall. The map has a generous minimum width but no
    /// spare height, so the controls spread sideways and stay out of the way
    /// vertically. Anchored by its bottom-left corner to the map's top-left, so
    /// it sits above the map without covering any of it — and without having to
    /// know its own height first, which it could not until after it had drawn.
    /// </summary>
    /// <summary>
    /// How tall the bar measured on its last draw, used to decide whether it
    /// fits above the map. Seeded with a two-row guess for the first frame,
    /// then always the real figure.
    /// </summary>
    private float _mapBarHeight = 56f;

    private void DrawMapControlBar()
    {
        if (!_config.ShowMapControlBar) return;

        var addon = _gameGui.GetAddonByName("AreaMap");
        if (addon.IsNull || !addon.IsVisible) return;

        var width = addon.ScaledWidth;
        if (width <= 0) return;

        // Pivot (0, 1) treats the given point as the window's bottom-left, which
        // puts the bar above the map.
        //
        // Unless there is no room above, with the map pushed against the top of
        // the screen. Then it anchors by its top-left instead and lies over the
        // map's first rows, which is worth more than being off-screen.
        //
        // The height comes from what the bar actually measured last frame
        // rather than a constant. It was a constant, and adding a second row of
        // toggles made it wrong — this cannot go stale.
        var room = addon.Y >= _mapBarHeight;
        ImGui.SetNextWindowPos(
            new Vector2(addon.X, addon.Y),
            ImGuiCond.Always,
            room ? new Vector2(0f, 1f) : new Vector2(0f, 0f));
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
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Marks that are actually up, drawn where they stand. Separate from the spawn points.");

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
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Each live mark's name and remaining health, written beside its dot.");
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
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The four minion spots and where the mark will spawn, while an event is running.");
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
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Everything drawn around your character. The four keep their settings while this is off.");

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
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The projected path: the swathe ahead that your range will sweep.");

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

    private void DrawTrainControls()
    {
        // Row 1: scanning state.
        if (_config.ScanningPaused)
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
            {
                _config.ScanningPaused = false;
                _config.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Paused — click to resume picking up new marks");
        }
        else
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Pause))
            {
                _config.ScanningPaused = true;
                _config.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scanning — click to stop picking up new marks");
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_config.ScanningPaused ? "Paused" : "Scanning");

        // Row 2: tidy-up and navigation.
        if (ImGui.Button("Remove Dead"))
        {
            _detector.RemoveDead();
        }

        ImGui.SameLine();
        if (ImGui.Button("Next Mark"))
        {
            SetCurrentMark(NextLiveMark(), announce: true);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move to the next live mark and flag it");

        ImGui.SameLine();
        if (ImGui.Button("Add Flag"))
        {
            var added = _detector.AddCustomFlag(_customFlagLabel);
            if (added == null)
                ReportProblem("No map flag set — place one with Ctrl+Right-Click first.");
            else
                _customFlagLabel = string.Empty;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Adds your current map flag to the train as a custom stop");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.InputTextWithHint("##customFlagLabel", "flag name", ref _customFlagLabel, 64);

        ImGui.SameLine();
        if (ImGui.Button("Next Aetheryte"))
        {
            OnNextAetheryteCommand(NextAetheryteCommand, string.Empty);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Names the aetheryte nearest the next mark, and copies a line to your clipboard. No flag — see /htra");

        // Row 3
        var tracking = _config.TrackingEnabled;
        if (ImGui.Checkbox("Tracking this train (records exact kill times)", ref tracking))
        {
            _config.TrackingEnabled = tracking;
            _config.Save();
        }

        // Row 4
        var hideDead = _config.HideDeadMarks;
        if (ImGui.Checkbox("Hide dead", ref hideDead))
        {
            _config.HideDeadMarks = hideDead;
            _config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Only hides them from this list — they stay in the train and in reports.");

        // Row 5 — same setting as the one on the Settings tab, so the two
        // always agree.
        var spicingHere = _config.ShowSpicing;
        if (ImGui.Checkbox("Show spicing markers", ref spicingHere))
        {
            _config.ShowSpicing = spicingHere;
            _config.Save();
        }
    }

    /// <summary>
    /// The train list, with drag-to-reorder.
    ///
    /// The important property here: NOTHING is reordered while the drag is in
    /// progress. Hovering a row only records where the drop would land, and the
    /// list is mutated exactly once, after the loop, when the mouse is
    /// released. Earlier versions swapped rows on every frame the cursor was
    /// off the source row, which made the dragged row race down the list and
    /// snap back — worse the slower you moved.
    ///
    /// The source index is tracked in a field rather than through an ImGui drag
    /// payload; behaviour is the same, and it keeps to API already proven to
    /// compile in this project.
    /// </summary>
    private void DrawTrainList(bool showZones = true)
    {
        var allMarks = _detector.Ordered();

        if (allMarks.Count == 0)
        {
            ImGui.TextDisabled("No marks detected yet — fly near one and it'll appear here.");
            return;
        }

        // What's shown may be a subset, but ordering maths always works against
        // the full list so hidden dead marks keep their place in the train.
        var marks = _config.HideDeadMarks
            ? allMarks.Where(m => !m.Dead).ToList()
            : allMarks;

        if (marks.Count == 0)
        {
            ImGui.TextDisabled($"All {allMarks.Count} marks are dead — untick \"Hide dead marks\" to see them.");
            return;
        }

        (uint NameId, uint Instance, uint WorldId)? toRemove = null;

        var rowHeight = (float)Math.Clamp(_config.TrainRowHeight, 14, 48);
        const float leftPad = 6f;
        const float columnGap = 14f;

        // Size the columns to the widest text actually present, so long zone
        // names (Coerthas Western Highlands) and long marks (Sabotender
        // Bailarina, Yehehetoaua'pyo) can never run into the buttons.
        var zoneColWidth = 0f;
        var nameColWidth = 0f;
        foreach (var m in marks)
        {
            if (showZones)
            {
                // Must match exactly what the row draws below, or long custom
                // flag zone names overflow into the mark name column.
                var z = ExpansionData.Lookup(m.NameId)?.Location
                        ?? (m.IsCustom ? m.ZoneName : "?");
                var measured = m.IsCustom ? $"⚑ 「{z}」" : $"「{z}」";
                zoneColWidth = Math.Max(zoneColWidth, ImGui.CalcTextSize(measured).X);
            }
            nameColWidth = Math.Max(nameColWidth,
                ImGui.CalcTextSize($"{m.Name}{ExpansionData.InstanceGlyph(m.Instance)}").X);
        }

        // Room for the age label, sized off a worst case so it doesn't jitter
        // as the numbers tick over.
        if (_config.ShowMarkAge)
            nameColWidth += ImGui.CalcTextSize("  (00h 00m)").X;

        // The remove button now sits at the far left, so every column shifts
        // right by its width.
        const float removeColWidth = 22f;
        var nameColumnX = leftPad + removeColWidth + zoneColWidth + columnGap;
        var buttonColumnX = nameColumnX + nameColWidth + columnGap;

        var mouseXInWindow = ImGui.GetMousePos().X - ImGui.GetWindowPos().X;
        var dragging = _dragFromIndex != -1;

        for (var i = 0; i < marks.Count; i++)
        {
            var mark = marks[i];
            // World included, because the identity is. Two rows for the same
            // mark on two worlds otherwise share an ImGui id, and ImGui cannot
            // tell their buttons apart — clicking the second row's x did
            // nothing to it.
            ImGui.PushID($"{mark.NameId}_{mark.Instance}_{mark.WorldId}");

            var info = ExpansionData.Lookup(mark.NameId);
            var zone = info?.Location ?? (mark.IsCustom ? mark.ZoneName : "?");
            var glyph = ExpansionData.InstanceGlyph(mark.Instance);

            // Precedence: dead greys out everything, then spiced, then custom.
            var rowColour = Vector4.One;
            if (mark.Dead) rowColour = new Vector4(0.45f, 0.45f, 0.45f, 1f);
            else if (mark.Spiced && _config.ShowSpicing) rowColour = new Vector4(1f, 0.35f, 0.35f, 1f);
            else if (mark.IsCustom) rowColour = new Vector4(0.45f, 0.95f, 0.5f, 1f);

            ImGui.PushStyleColor(ImGuiCol.Text, rowColour);

            ImGui.SetCursorPosX(leftPad);
            ImGui.BeginGroup();

            // Remove button on the far left, well away from teleport so it
            // can't be hit by accident.
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 0));
            if (ImGui.SmallButton("x"))
            {
                toRemove = (mark.NameId, mark.Instance, mark.WorldId);
            }
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove this mark from the train");
            ImGui.SetItemAllowOverlap();
            ImGui.SameLine();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            // Highlighted while it's the row being dragged.
            ImGui.SetCursorPosX(leftPad + removeColWidth);
            // Say the world when it is not the one you are standing on. Marks
            // are per-world now, so scouting across a world change genuinely
            // produces two rows for the same name — which is correct, and
            // unreadable without this.
            var otherWorld = mark.WorldId != 0
                             && mark.WorldId != _detector.CurrentWorldId()
                             && !string.IsNullOrEmpty(mark.WorldName)
                ? $" [{mark.WorldName}]"
                : string.Empty;

            var zoneLabel = showZones
                ? (mark.IsCustom ? $"⚑ 「{zone}」{otherWorld}" : $"「{zone}」{otherWorld}")
                : otherWorld;
            ImGui.Selectable(zoneLabel, _dragFromIndex == i, ImGuiSelectableFlags.None,
                new Vector2(ImGui.GetWindowWidth(), rowHeight));
            ImGui.SetItemAllowOverlap();
            ImGui.PopStyleVar();

            ImGui.SameLine();
            ImGui.SetCursorPosX(nameColumnX);
            var ageSuffix = _config.ShowMarkAge ? $"  ({FormatAge(mark.LastSeenUtc)})" : string.Empty;
            var isCurrent = _currentMark is { } cur && cur == mark.Key;
            if (isCurrent)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.4f, 1f));
                ImGui.Text($"> {mark.Name}{glyph}{ageSuffix}");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.Text($"{mark.Name}{glyph}{ageSuffix}");
            }
            ImGui.SetItemAllowOverlap();

            ImGui.SameLine();
            ImGui.SetCursorPosX(buttonColumnX);
            // The game's own aetheryte crystal, pulled from its texture sheets
            // so it stays correct across patches and ships no assets. Falls back
            // to a text button if the icon can't be resolved.
            var iconSize = new Vector2(rowHeight - 4, rowHeight - 4);
            var teleportPressed = false;

            if (_textureProvider.TryGetFromGameIcon(new GameIconLookup(AetheryteIconId), out var iconTex)
                && iconTex.TryGetWrap(out var iconWrap, out _))
            {
                teleportPressed = ImGui.ImageButton(iconWrap.Handle, iconSize);
            }
            else
            {
                teleportPressed = ImGui.Button("tele", new Vector2(34f, rowHeight - 2));
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Teleport to the nearest aetheryte");

            if (teleportPressed)
            {
                if (!_teleport.TeleportToNearest(mark.TerritoryId, mark.MapPosition))
                {
                    ReportProblem(_teleport.LastError);
                }
                else
                {
                    if (_config.TeleportAlsoFlags)
                        MapFlagHelper.FlagMark(_gameGui, mark);

                    // A custom flag is a rally point — teleporting to it IS
                    // completing it, so tick it off and let auto-advance move
                    // on. Never done for real marks, which aren't dead just
                    // because someone travelled to them.
                    if (mark.IsCustom && !_pendingCustomRemovals.ContainsKey((mark.NameId, mark.Instance, mark.WorldId)))
                    {
                        _pendingCustomRemovals[(mark.NameId, mark.Instance, mark.WorldId)] =
                            DateTime.UtcNow.AddSeconds(CustomFlagRemovalDelaySeconds);
                    }
                }
            }
            ImGui.SetItemAllowOverlap();

            if (_config.ShowSpicing)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(buttonColumnX + 40);
                // Capture the state BEFORE drawing the button. Testing
                // mark.Spiced on both sides let the click flip it in between,
                // so a push could go unmatched by its pop (or vice versa) —
                // an ImGui style stack imbalance, which crashes in native code.
                var wasSpiced = mark.Spiced;
                if (wasSpiced) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.35f, 0.35f, 1f));

                if (ImGuiComponents.IconButton(FontAwesomeIcon.PepperHot))
                {
                    mark.Spiced = !mark.Spiced;
                }

                if (wasSpiced) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(mark.Spiced
                        ? "Being spiced — click to unset"
                        : "Mark as being spiced (prepped before the train arrives)");
                ImGui.SetItemAllowOverlap();
            }

            ImGui.SameLine();
            ImGui.SetCursorPosX(buttonColumnX + (_config.ShowSpicing ? 100 : 68));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 99);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
            var dead = mark.Dead;
            ImGui.Checkbox("##dead", ref dead);
            ImGui.PopStyleVar(2);
            if (dead != mark.Dead)
            {
                mark.Dead = dead;
                mark.DeathObservedAtUtc = dead ? DateTime.UtcNow : null;

                // Keep the map honest: a mark ticked dead shouldn't stay lit.
                if (dead) _detector.RemoveSighting(mark.NameId, mark.Instance, mark.WorldId);
            }
            ImGui.SetItemAllowOverlap();

            ImGui.Separator();
            ImGui.EndGroup();
            ImGui.PopStyleColor();

            // Drop indicator: a bright line on the edge of the row the release
            // would land on, so it's obvious where the mark is going.
            if (dragging && _dragToIndex == i && _dragFromIndex != i)
            {
                var rowMin = ImGui.GetItemRectMin();
                var rowMax = ImGui.GetItemRectMax();
                var edgeY = _dragToIndex < _dragFromIndex ? rowMin.Y : rowMax.Y;
                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(rowMin.X, edgeY),
                    new Vector2(rowMax.X, edgeY),
                    ImGui.GetColorU32(ImGuiCol.DragDropTarget),
                    2.5f);
            }

            // --- drag: only ever RECORDS intent, never mutates the list ---
            if (_dragFromIndex == -1
                && ImGui.IsItemActive()
                && ImGui.IsMouseDragging(ImGuiMouseButton.Left)
                && mouseXInWindow < buttonColumnX)
            {
                _dragFromIndex = i;
            }

            if (dragging && ImGui.IsItemHovered())
            {
                _dragToIndex = i;
            }

            // --- click: a release with no drag in progress ---
            if (!dragging
                && ImGui.IsItemFocused()
                && mouseXInWindow < buttonColumnX
                && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                && Math.Abs(ImGui.GetMouseDragDelta().Y) < 0.1f)
            {
                // Clicking a mark makes it the current one, so a conductor can
                // jump the pointer anywhere just by clicking.
                _currentMark = (mark.NameId, mark.Instance, mark.WorldId);

                if (_config.EchoOnMarkClick)
                {
                    var fullIndex = allMarks.FindIndex(m => m.Key == mark.Key);
                    TrainChatEcho.Send(_chatGui, _gameGui, mark, fullIndex < 0 ? i : fullIndex, allMarks.Count);
                }
                else
                    MapFlagHelper.FlagMark(_gameGui, mark);
            }

            ImGui.PopID();
        }

        // --- floating preview under the cursor while dragging ---
        // Gemini suggested doing this inside BeginDragDropSource, but this
        // implementation tracks the drag manually rather than through an ImGui
        // payload, so a plain tooltip gives the same cursor-following preview.
        if (dragging && _dragFromIndex < marks.Count)
        {
            var source = marks[_dragFromIndex];
            var sourceZone = ExpansionData.Lookup(source.NameId)?.Location ?? "?";
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"「{sourceZone}」 {source.Name}{ExpansionData.InstanceGlyph(source.Instance)}");
            ImGui.EndTooltip();
        }

        // --- commit the move exactly once, on release, after the loop ---
        if (_dragFromIndex != -1 && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (_dragToIndex != -1
                && _dragToIndex != _dragFromIndex
                && _dragFromIndex < marks.Count
                && _dragToIndex < marks.Count)
            {
                // Translate the visible positions back to positions in the full
                // list, so dragging still lands correctly when dead marks are
                // hidden between the rows being moved.
                var moving = marks[_dragFromIndex];
                var target = marks[_dragToIndex];

                var fromFull = allMarks.FindIndex(m => m.Key == moving.Key);
                var toFull = allMarks.FindIndex(m => m.Key == target.Key);

                if (fromFull >= 0 && toFull >= 0)
                {
                    allMarks.RemoveAt(fromFull);
                    allMarks.Insert(toFull, moving);
                    _detector.ApplyOrder(allMarks);
                }
            }

            _dragFromIndex = -1;
            _dragToIndex = -1;
        }

        if (toRemove.HasValue) _detector.Remove(toRemove.Value);

        ImGui.Spacing();
        ImGui.TextDisabled("Click a mark to echo + flag it. Drag a row and release where you want it.");
    }

    private void DrawTrainTab()
    {
        ImGui.Spacing();

        var useOwn = _config.UseOwnTrainList;
        if (ImGui.Checkbox("Use this list for reports (instead of Hunt Helper's)", ref useOwn))
        {
            _config.UseOwnTrainList = useOwn;
            _config.Save();
        }
        ImGui.TextDisabled("Both lists always populate, so you can compare them before switching over.");

        ImGui.Spacing();
        DrawTrainControls();

        ImGui.Spacing();
        if (ImGui.Button("Open Train Popout"))
        {
            _trainPopoutVisible = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear All"))
        {
            _detector.Clear();
        }

        ImGui.Spacing();
        if (ImGui.Button("Copy Export Code"))
        {
            if (_detector.Marks.Count == 0)
            {
                _lastPostResult = "Nothing to export — no marks detected yet.";
            }
            else
            {
                ImGui.SetClipboardText(TrainExchange.Export(_detector.Ordered()));
                _lastPostResult = $"Exported {_detector.Marks.Count} marks to clipboard.";
            }
        }
        ImGui.TextDisabled("Uses Hunt Helper's own format — the code pastes into Hunt Helper too.");

        ImGui.Spacing();
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##importCode", "Paste an import code here", ref _importCode, 65536);
        ImGui.SameLine();
        if (ImGui.Button("Import"))
        {
            var imported = TrainExchange.Import(_importCode);
            if (imported == null)
            {
                _lastPostResult = "That import code couldn't be read.";
            }
            else
            {
                var added = _detector.Merge(imported);
                _importCode = string.Empty;
                _lastPostResult = $"Imported {imported.Count} marks ({added} new).";
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginChild("trainTabScroll", new Vector2(0, 0), false))
        {
            DrawTrainList();
        }
        ImGui.EndChild();
    }

    private void DrawTrainPopout()
    {
        if (!_trainPopoutVisible) return;

        ImGui.SetNextWindowSize(new Vector2(600, 420), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 200), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("Hunt Train", ref _trainPopoutVisible))
        {
            // Controls sit outside the scrolling region so they stay put while
            // the list scrolls underneath.
            DrawTrainControls();
            ImGui.Separator();

            // Reserve room at the bottom for the footer, so the list scrolls
            // between two fixed strips rather than under them.
            var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
            if (ImGui.BeginChild("trainScroll", new Vector2(0, -footerHeight), false))
            {
                DrawTrainList(showZones: !_config.HideZonesInPopout);
            }
            ImGui.EndChild();

            // Footer: the two actions that actually send something. Deliberately
            // separated from the controls above so neither gets hit by accident
            // while reaching for play/pause mid-train.
            ImGui.Separator();
            DrawTrainFooter();
        }
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
                ImGui.TextDisabled($"    {mob}: {count}");
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

    private void DrawConductorTab()
    {
        ImGui.Spacing();

        var tracking = _config.TrackingEnabled;
        if (ImGui.Checkbox("Tracking this train (records exact kill times)", ref tracking))
        {
            _config.TrackingEnabled = tracking;
            _config.Save();
        }
        ImGui.TextDisabled("Turn this on at the start of a train for accurate per-mark kill times. Nothing posts automatically — use End Train Now when it's actually finished.");

        ImGui.Spacing();
        ImGui.TextWrapped($"Status: {_watcher.LastStatus}");

        ImGui.Spacing();
        var autoMark = _config.AutoMarkDeadEnabled;
        if (ImGui.Checkbox("Auto-mark dead using Hunt Tally", ref autoMark))
        {
            _config.AutoMarkDeadEnabled = autoMark;
            _config.Save();
        }
        ImGui.TextDisabled(TallyFeedStatus());
        ImGui.TextDisabled("Marks are recorded dead here automatically, with the tally's exact kill time. Your Hunt Helper list still needs clicking yourself for its own navigation.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("End Train Now"))
        {
            _ = EndTrainNowAsync();
        }
        ImGui.TextDisabled("Posts the report, sorted by the order marks actually died, plus any S-rank checks below. Only clears once the post actually succeeds.");

        ImGui.Spacing();
        if (ImGui.Button("Reset train tracking now"))
        {
            _watcher.ResetNow();
            _detector.Clear();
            _currentMark = null;
            _config.Flags.Clear();
            _config.Save();
            ClearSavedTrain();
        }
        ImGui.TextDisabled("Clears tracking and S-rank watches without posting anything — use if you need to abandon a train.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("S-Rank Watches");

        var reminderOn = _config.SRankZoneReminderEnabled;
        if (ImGui.Checkbox("Remind me on entering an S-rank zone", ref reminderOn))
        {
            _config.SRankZoneReminderEnabled = reminderOn;
            _config.Save();
        }

        if (_config.SRankZoneReminderEnabled)
        {
            ImGui.SameLine();
            var reminderSound = _config.SRankZoneReminderSound;
            if (ImGui.Checkbox("with sound", ref reminderSound))
            {
                _config.SRankZoneReminderSound = reminderSound;
                _config.Save();
            }
        }
        ImGui.TextDisabled("Lakeland (Tyger), Ultima Thule (Narrow-rift), Elpis (Ophioneus). Only you see it.");

        ImGui.Spacing();

        foreach (var name in SimpleSRanks)
        {
            if (ImGui.Button($"Watch {name}"))
            {
                _config.Flags.Add(new FlagEntry
                {
                    Label = name,
                    TerritoryId = name == "Tyger" ? 813u : 961u, // Lakeland / Elpis
                });
                _config.Save();
            }
            ImGui.SameLine();
        }

        var spawnLabels = NarrowRiftSpawns.Select((s, i) => $"Spawn {i + 1} ({s.X:F1}, {s.Y:F1})").ToArray();
        ImGui.SetNextItemWidth(180);
        ImGui.Combo("##narrowRiftSpawn", ref _selectedNarrowRiftSpawn, spawnLabels, spawnLabels.Length);
        ImGui.SameLine();
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

            ImGui.TextWrapped(flag.Label);

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
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                toRemove = i;
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (toRemove.HasValue)
        {
            _config.Flags.RemoveAt(toRemove.Value);
            _config.Save();
        }
    }

    private void DrawScoutTab()
    {
        ImGui.Spacing();
        if (ImGui.Button("Send Scouting Report"))
        {
            _ = SendScoutingReportAsync();
        }
        ImGui.TextDisabled("Posts Hunt Helper's current train list as a paste-able import code, plus a per-expansion up count.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Additional scouts — credit anyone else whose scouting you folded into this report " +
            "(e.g. they sent you their Hunt Helper export code privately and you imported it)."
        );
        ImGui.Spacing();

        ImGui.PushID("scouts");
        DrawStringList(
            _config.AdditionalScouts,
            MaxAdditionalScouts,
            "+ Add scout",
            $"Maximum of {MaxAdditionalScouts} additional scouts reached.");
        ImGui.PopID();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Open Counter Popout"))
        {
            _counterPopoutVisible = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset All Counts"))
        {
            _counter.Reset();
        }
        ImGui.TextDisabled("Counts trigger-mob kills for S-ranks that need them (also /htrc).");

        ImGui.Spacing();
        DrawSpawnWatches();

        // World picker — the popout always shows where you're standing, but a
        // scout may want to check counts for another world entirely.
        var dcs = _worldData.DataCenters;
        if (dcs.Count > 0)
        {
            // Follow the player's world when it changes, then leave the picker
            // alone so a manual selection isn't fought.
            var liveWorld = _counter.CurrentWorldId();
            if (liveWorld != 0 && liveWorld != _lastSeenWorldId)
            {
                _lastSeenWorldId = liveWorld;
                if (_worldData.LocateWorld(liveWorld) is { } located)
                {
                    _counterDcIndex = located.DcIndex;
                    _counterWorldIndex = located.WorldIndex;
                }
            }

            ImGui.Spacing();
            var dcNames = dcs.Select(d => d.Name).ToArray();
            _counterDcIndex = Math.Clamp(_counterDcIndex, 0, dcs.Count - 1);
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Data centre", ref _counterDcIndex, dcNames, dcNames.Length))
                _counterWorldIndex = 0;

            var worlds = _worldData.WorldsIn(dcs[_counterDcIndex].Id);
            if (worlds.Count > 0)
            {
                var worldNames = worlds.Select(w => w.Name).ToArray();
                _counterWorldIndex = Math.Clamp(_counterWorldIndex, 0, worlds.Count - 1);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.Combo("World", ref _counterWorldIndex, worldNames, worldNames.Length);

                var chosen = worlds[_counterWorldIndex];
                ImGui.Spacing();
                DrawCounterList(
                    currentZoneOnly: false,
                    worldId: chosen.RowId,
                    instance: 0,
                    worldName: chosen.Name);
                return;
            }
        }

        // No world list available — fall back to wherever the player is.
        ImGui.Spacing();
        DrawCounterList(
            currentZoneOnly: false,
            worldId: _counter.CurrentWorldId(),
            instance: MarkDetector.GetCurrentInstance(),
            worldName: _counter.CurrentWorldName());

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawMarksSlainTab()
    {
        ImGui.Spacing();
        ImGui.TextWrapped("Preview of what End Train Now would post right now, in the order marks actually died.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var marks = BuildCurrentMarks();
        if (marks == null)
        {
            ImGui.TextDisabled("Hunt Helper not detected.");
            return;
        }

        if (marks.Count == 0)
        {
            ImGui.TextDisabled("Nothing tracked yet — start a train with Tracking this train enabled.");
            return;
        }

        var entries = TrainReport.BuildEntries(marks);
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

            if (entry.Location == null || entry.MinHours == null || entry.MaxHours == null)
            {
                ImGui.TextWrapped($"{localTime} — {entry.Name} — no fixed respawn timer");
                continue;
            }

            var openLocal = entry.KillTimeUtc.AddHours(entry.MinHours.Value).ToLocalTime().ToString("t");
            var capLocal = entry.KillTimeUtc.AddHours(entry.MaxHours.Value).ToLocalTime().ToString("t");
            var instanceGlyph = ExpansionData.InstanceGlyph(entry.Instance);
            ImGui.TextWrapped($"{localTime} — {entry.Location} — {entry.Name}{instanceGlyph} — window {openLocal} → {capLocal}");
        }

        var sniped = TrainReport.BuildSniped(marks);
        if (sniped.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("Assumed Sniped (not seen this train)");
            foreach (var (expansion, names) in sniped)
            {
                ImGui.TextWrapped($"{expansion}: {string.Join(", ", names)}");
            }
        }
    }

    /// <summary>
    /// Grouped into collapsible sections — there are enough toggles now that a
    /// single flat list is hard to scan.
    /// </summary>
    private void DrawSettingsTab()
    {
        ImGui.Spacing();
        if (ImGui.Button("Send test message"))
        {
            _ = SendTestAsync();
        }
        ImGui.TextDisabled("Posts to every ENABLED webhook below.");

        if (ImGui.CollapsingHeader("Discord webhooks", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped(
                "One per Discord server (or channel) to post to. Untick Enabled to keep a " +
                "testing channel around without deleting it. Create a webhook in Discord via " +
                "Channel Settings > Integrations > Webhooks > New Webhook > Copy Webhook URL."
            );
            ImGui.Spacing();
            DrawWebhookList();
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Train list"))
        {
            var echoClick = _config.EchoOnMarkClick;
            if (ImGui.Checkbox("Echo a mark to chat when its row is clicked", ref echoClick))
            {
                _config.EchoOnMarkClick = echoClick;
                _config.Save();
            }
            ImGui.TextDisabled("Off still flags the mark on your map — it just doesn't post the chat line.");

            ImGui.TextDisabled("Announcing marks as they're detected has its own section below.");

            var observedDeaths = _config.MarkDeadOnObservedDefeat;
            if (ImGui.Checkbox("Tick a mark dead when the battle log says it died", ref observedDeaths))
            {
                _config.MarkDeadOnObservedDefeat = observedDeaths;
                _config.Save();
            }
            ImGui.TextDisabled("Whoever killed it — either seeing its health hit zero, or the battle log saying so. Marking dead from the tally only covers kills you were credited with, so a mark the group brought down while you ran in used to stay lit.");

            var teleFlags = _config.TeleportAlsoFlags;
            if (ImGui.Checkbox("Teleport also drops the map flag", ref teleFlags))
            {
                _config.TeleportAlsoFlags = teleFlags;
                _config.Save();
            }

            var showAge = _config.ShowMarkAge;
            if (ImGui.Checkbox("Show how long ago each mark was last seen", ref showAge))
            {
                _config.ShowMarkAge = showAge;
                _config.Save();
            }

            var hideDeadSetting = _config.HideDeadMarks;
            if (ImGui.Checkbox("Hide dead marks in the train list", ref hideDeadSetting))
            {
                _config.HideDeadMarks = hideDeadSetting;
                _config.Save();
            }
            ImGui.TextDisabled("Display only — dead marks stay in the train and in reports.");

            var hideZones = _config.HideZonesInPopout;
            if (ImGui.Checkbox("Hide zone names in the train popout", ref hideZones))
            {
                _config.HideZonesInPopout = hideZones;
                _config.Save();
            }

            var spicing = _config.ShowSpicing;
            if (ImGui.Checkbox("Show spicing markers", ref spicing))
            {
                _config.ShowSpicing = spicing;
                _config.Save();
            }
            ImGui.TextDisabled("A scout flagging a mark they'll prep before the train arrives.");

            var autoAdv = _config.AutoAdvance;
            if (ImGui.Checkbox("Auto-advance to the next mark when the current one dies", ref autoAdv))
            {
                _config.AutoAdvance = autoAdv;
                _config.Save();
            }

            if (_config.AutoAdvance)
            {
                var echoAdv = _config.EchoOnAdvance;
                if (ImGui.Checkbox("Echo and flag the mark it advances to", ref echoAdv))
                {
                    _config.EchoOnAdvance = echoAdv;
                    _config.Save();
                }
            }

            var rowH = _config.TrainRowHeight;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Row height (pixels)", ref rowH))
            {
                _config.TrainRowHeight = Math.Clamp(rowH, 14, 48);
                _config.Save();
            }

            var pollInterval = _config.PollIntervalSeconds;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Detection interval (seconds)", ref pollInterval))
            {
                _config.PollIntervalSeconds = Math.Clamp(pollInterval, 1, 30);
                _config.Save();
            }
            ImGui.TextDisabled("How often marks are scanned for. Lower catches more while flying fast.");
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Scout & counter"))
        {
            var myKills = _config.CountOnlyMyKills;
            if (ImGui.Checkbox("Count only kills I land", ref myKills))
            {
                _config.CountOnlyMyKills = myKills;
                _config.Save();
            }
            ImGui.TextDisabled("Off counts every trigger mob killed nearby — total progress toward the spawn rather than your own share.");
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Map spawn points"))
        {
            var mapPoints = _config.ShowSpawnPointsOnMap;
            if (ImGui.Checkbox("Show spawn points on the in-game map", ref mapPoints))
            {
                _config.ShowSpawnPointsOnMap = mapPoints;
                _config.Save();
            }
            ImGui.TextDisabled("Where a mark could be. A zone's B-rank points alone can run to sixty dots.");

            var mapMarks = _config.ShowMarksOnMap;
            if (ImGui.Checkbox("Show live marks on the in-game map", ref mapMarks))
            {
                _config.ShowMarksOnMap = mapMarks;
                _config.Save();
            }
            ImGui.TextDisabled("What is actually there, drawn where it stands. Separate from the points above, so you can have one without the other.");

            ImGui.TextDisabled(_mapOverlay.Status);

            var bar = _config.ShowMapControlBar;
            if (ImGui.Checkbox("Show a control bar above the map", ref bar))
            {
                _config.ShowMapControlBar = bar;
                _config.Save();
            }
            ImGui.TextDisabled("These same toggles, pinned to the top of the game's map and shown with it. Also /htrm.");

            if (_config.ShowSpawnPointsOnMap || _config.ShowMarksOnMap)
            {
                if (_config.ShowSpawnPointsOnMap)
                {
                    var showA = _config.ShowARankPoints;
                    if (ImGui.Checkbox("A-rank points", ref showA))
                    {
                        _config.ShowARankPoints = showA;
                        _config.Save();
                    }
                    ImGui.SameLine();
                    var showB = _config.ShowBRankPoints;
                    if (ImGui.Checkbox("B-rank##points", ref showB))
                    {
                        _config.ShowBRankPoints = showB;
                        _config.Save();
                    }
                    ImGui.SameLine();
                    var showS = _config.ShowSRankPoints;
                    if (ImGui.Checkbox("S-rank##points", ref showS))
                    {
                        _config.ShowSRankPoints = showS;
                        _config.Save();
                    }
                }

                if (_config.ShowMarksOnMap)
                {
                    var markA = _config.ShowARankMarks;
                    if (ImGui.Checkbox("A-rank marks", ref markA))
                    {
                        _config.ShowARankMarks = markA;
                        _config.Save();
                    }
                    ImGui.SameLine();
                    var markB = _config.ShowBRankMarks;
                    if (ImGui.Checkbox("B-rank##marks", ref markB))
                    {
                        _config.ShowBRankMarks = markB;
                        _config.Save();
                    }
                    ImGui.SameLine();
                    var markS = _config.ShowSRankMarks;
                    if (ImGui.Checkbox("S-rank##marks", ref markS))
                    {
                        _config.ShowSRankMarks = markS;
                        _config.Save();
                    }
                }

                ImGui.TextDisabled("Hover a dot on the map for what's there. Marks are drawn a little larger than a spawn point, at the position they are actually standing on.");

                var clickFlag = _config.ClickSpawnPointToFlag;
                if (ImGui.Checkbox("Click a spawn point on the map to flag it", ref clickFlag))
                {
                    _config.ClickSpawnPointToFlag = clickFlag;
                    _config.Save();
                }
                ImGui.TextDisabled("For sending people to a spot before anything is on it. Marks themselves aren't clickable — one that's up is already drawn where it is.");

                var ssEvent = _config.ShowSsEventOnMap;
                if (ImGui.Checkbox("Mark SS event minion locations", ref ssEvent))
                {
                    _config.ShowSsEventOnMap = ssEvent;
                    _config.Save();
                }
                ImGui.TextDisabled("From the \"minions of an extraordinarily powerful mark\" announcement until the mark spawns or you leave the zone. The spot is learned from the minions themselves — nothing in the game's data holds it.");
                ImGui.TextDisabled(_ssEvent.Status);

                var labels = _config.ShowMarkLabelsOnMap;
                if (ImGui.Checkbox("Write mark names and health on the map", ref labels))
                {
                    _config.ShowMarkLabelsOnMap = labels;
                    _config.Save();
                }
                ImGui.TextDisabled("Only marks that are actually up get one, so this follows the rank filters above. Health updates as the mark is pulled.");

                if (_config.ShowMarkLabelsOnMap)
                {
                    var fontSize = _config.MarkLabelFontSize;
                    ImGui.SetNextItemWidth(90);
                    if (ImGui.InputFloat("Name text size", ref fontSize, 1f))
                    {
                        _config.MarkLabelFontSize = Math.Clamp(fontSize, 6f, 48f);
                        _config.Save();
                    }
                }

                ImGui.Spacing();
                DrawDotColours();
                ImGui.Spacing();

                var dotSize = _config.SpawnDotSize;
                ImGui.SetNextItemWidth(90);
                if (ImGui.InputFloat("Dot size", ref dotSize, 2f))
                {
                    _config.SpawnDotSize = Math.Clamp(dotSize, 6f, 48f);
                    _config.Save();
                }

            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawPlayerGuideSettings();
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Detection notifications"))
        {
            DrawDetectionNotificationSettings();
        }

        if (ImGui.CollapsingHeader("About"))
        {
            ImGui.TextDisabled($"Hunt Helper Evolved {ReleaseNotes.CurrentVersion}");
            ImGui.Spacing();

            // The notes are a window of their own and turn up on their own
            // after an update, so this is the way back to them afterwards.
            if (ImGui.Button("What's new"))
                _releaseNotesVisible = true;

            ImGui.SameLine();
            ImGui.TextDisabled("Changes in this and previous versions, and who to thank.");
            ImGui.Spacing();

            // Worth stating rather than leaving to the log. Whether the HH.*
            // gates are answered here decides whether somebody's other plugin
            // can see this train at all, and it is not otherwise visible.
            ImGui.TextDisabled(_trainIpc.ClaimedHuntHelperGates
                ? "IPC: other plugins can read this train through Hunt Helper's own gates."
                : "IPC: Hunt Helper is installed and keeps its gates. Other plugins see its train, not this one.");
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Teleport"))
        {
            ImGui.TextWrapped("Aetheryte blacklist — never route to these.");
            ImGui.TextDisabled("Affects the teleport button and Next Aetheryte alike.");
            ImGui.Spacing();
            DrawBlacklistPicker();
            ImGui.Spacing();
        }
    }

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

    /// <summary>
    /// Reusable add/remove list editor for simple string lists (currently just
    /// additional scouts).
    /// </summary>
    private void DrawStringList(List<string> list, int maxCount, string addLabel, string maxReachedLabel)
    {
        int? toRemove = null;

        for (var i = 0; i < list.Count; i++)
        {
            ImGui.PushID(i);

            var value = list[i];
            ImGui.SetNextItemWidth(320);
            if (ImGui.InputText("##listItem", ref value, 512))
            {
                list[i] = value;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _config.Save();
            }

            if (list.Count > 1)
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
            list.RemoveAt(toRemove.Value);
            if (list.Count == 0) list.Add(string.Empty);
            _config.Save();
        }

        if (list.Count < maxCount)
        {
            if (ImGui.Button(addLabel))
            {
                list.Add(string.Empty);
                _config.Save();
            }
        }
        else
        {
            ImGui.TextDisabled(maxReachedLabel);
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

    public void Dispose()
    {
        // The tally first: its tracker and seeder both run off framework
        // events, and the cancellation token stops a queued seed starting up
        // after everything it reads has been torn down.
        _disposal.Cancel();

        _tallyIpc.KillPublished -= OnTallyKillPublished;
        _tracker.OnKill -= AnnounceTallyKill;
        _tracker.OnKill -= _tallyIpc.PublishCredited;
        _tracker.OnMarkDeath -= _tallyIpc.PublishMarkDeath;
        _tracker.Dispose();
        _tallyIpc.Dispose();

        // After the tracker, which reads both on every poll.
        _damage.Dispose();
        _reward.Dispose();

        HuntTally.Service.ClientState.Login -= OnTallyLogin;
        HuntTally.Service.ClientState.Logout -= OnTallyLogout;
        HuntTally.Service.Framework.Update -= OnTallyFrameworkUpdate;
        _seeder.Dispose();

        _trainIpc.Dispose();

        _pluginInterface.UiBuilder.OpenMainUi -= ToggleTallyWindow;
        _tallyWindows.RemoveAllWindows();
        _tallyWindow.Dispose();

        // Saving is queued rather than immediate, so the last counts of the
        // session only reach disk because of this. A no-op while the tally is
        // stood down, which is the point of standing it down.
        _tallyConfig.Flush(force: true);

        _characters.Dispose();
        _disposal.Dispose();

        _watcher.Dispose();
        _zoneReminder.Dispose();
        _counter.Dispose();
        _spawnWatch.Dispose();
        _mapOverlay.Dispose();
        _ssEvent.Dispose();

        try
        {
            KamiToolKitLibrary.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "KamiToolKit did not shut down cleanly.");
        }
        _detector.OtherRankDetected -= OnSightingDetected;
        _watcher.PersistRequested -= PersistTrain;

        // One last write, so anything since the last periodic save survives a
        // clean unload too.
        PersistTrain();
        _pluginInterface.UiBuilder.Draw -= DrawUI;
        _pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        _commandManager.RemoveHandler(ConfigCommand);
        _commandManager.RemoveHandler(TrainCommand);
        _commandManager.RemoveHandler(CounterCommand);
        _commandManager.RemoveHandler(NextAetheryteCommand);
        _commandManager.RemoveHandler(MapCommand);
        _commandManager.RemoveHandler(TallyCommand);

        foreach (var alias in _claimedAliases)
            _commandManager.RemoveHandler(alias);
    }
}
