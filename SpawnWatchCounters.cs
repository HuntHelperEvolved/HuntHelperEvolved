using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;

namespace HuntHelperEvolved;

/// <summary>
/// The two S-ranks whose spawn is a live world state rather than a running
/// count of battle-log lines, so <see cref="HuntCounter"/>'s regex model does
/// not fit them:
///
///  - <b>Narrow-rift</b> (Ultima Thule): ten players stand on a spawn point
///    with a Wee Ea minion summoned. We only see our own client's object table,
///    so this reports how many Wee Ea are loaded around us — a lower bound on
///    the real number, but enough to answer "are we there yet".
///
///  - <b>Nunyunuwi</b> (Southern Thanalan): no FATE may fail in the zone for
///    one real hour. We hold the clock and restart it the moment a FATE is seen
///    to fail, so the countdown on screen is always the honest one.
///
/// Adapted from Hunt Helper's <c>CounterUI.Fates.cs</c> and
/// <c>DrawWeeEaCounter</c> (img02/HuntHelper, MIT).
/// </summary>
public sealed class SpawnWatchCounters : IDisposable
{
    public const uint SouthernThanalanTerritory = 146;
    public const uint UltimaThuleTerritory = 960;

    /// <summary>
    /// Companion (minion) row id for Wee Ea — a summoned minion's
    /// <see cref="Dalamud.Game.ClientState.Objects.Types.IGameObject.BaseId"/>.
    /// From Hunt Helper's Constants.cs; confirmed against the Companion sheet.
    /// </summary>
    public const uint WeeEaBaseId = 423;

    /// <summary>Players-with-minions the spawn point needs.</summary>
    public const int NarrowRiftRequiredWeeEa = 10;

    /// <summary>True in a zone one of these watches covers.</summary>
    public static bool AppliesTo(uint territory) =>
        territory is SouthernThanalanTerritory or UltimaThuleTerritory;

    private static readonly TimeSpan NunyunuwiQuietWindow = TimeSpan.FromHours(1);

    /// <summary>A FATE as the counter window wants to show it.</summary>
    public readonly record struct FateSnapshot(
        string Name, int ProgressPercent, TimeSpan TimeRemaining, bool AwaitingActivation);

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly IFateTable fates;
    private readonly IPluginLog log;

    /// <summary>Last seen state of every FATE id present this zone visit.</summary>
    private readonly Dictionary<uint, FateState> lastFateState = new();
    private List<FateSnapshot> activeFates = new();

    /// <summary>
    /// The FATE sweep allocates a little, and a one-hour clock does not need it
    /// 100 times a second. Half a second is still prompt enough to catch a
    /// failure and restart the countdown while the player is looking at it.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.5);
    private DateTime lastPoll = DateTime.MinValue;

    /// <summary>When the current unbroken FATE-clean stretch started.</summary>
    public DateTime NunyunuwiSince { get; private set; } = DateTime.Now;

    /// <summary>Earliest Nunyunuwi can spawn if nothing fails before then.</summary>
    public DateTime NunyunuwiEta => NunyunuwiSince + NunyunuwiQuietWindow;

    /// <summary>Time left on the quiet hour, floored at zero.</summary>
    public TimeSpan NunyunuwiRemaining
    {
        get
        {
            var left = NunyunuwiEta - DateTime.Now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Empty until a FATE has been seen to fail this session.</summary>
    public string NunyunuwiLastFailure { get; private set; } = string.Empty;

    /// <summary>FATEs currently running or pending, soonest to expire first.</summary>
    public IReadOnlyList<FateSnapshot> ActiveFates => activeFates;

    public SpawnWatchCounters(
        IFramework framework, IClientState clientState, IObjectTable objects,
        IFateTable fates, IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objects = objects;
        this.fates = fates;
        this.log = log;

        framework.Update += OnUpdate;
        clientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        clientState.TerritoryChanged -= OnTerritoryChanged;
    }

    /// <summary>Wee Ea minions loaded in our object table right now.</summary>
    public int WeeEaLoaded()
    {
        var n = 0;
        foreach (var obj in objects)
        {
            if (obj.ObjectKind == ObjectKind.Companion && obj.BaseId == WeeEaBaseId)
                n++;
        }
        return n;
    }

    /// <summary>Manual restart, for when a failure happened before you arrived.</summary>
    public void ResetNunyunuwiClock()
    {
        NunyunuwiSince = DateTime.Now;
        NunyunuwiLastFailure = string.Empty;
        lastFateState.Clear();
        activeFates = new();
    }

    private void OnTerritoryChanged(uint territory)
    {
        // FATE ids and the clock only mean anything within one visit to the
        // zone, and a stale Failed entry left over from the last zone would
        // otherwise trip the reset the instant the new table loads.
        lastFateState.Clear();
        activeFates = new();
        NunyunuwiSince = DateTime.Now;
        NunyunuwiLastFailure = string.Empty;
    }

    private void OnUpdate(IFramework _)
    {
        // Only Southern Thanalan needs the FATE watch, and it has to run whether
        // or not the counter window is open — a failure while you are tabbed out
        // still has to restart the clock.
        if (clientState.TerritoryType != SouthernThanalanTerritory)
        {
            if (activeFates.Count > 0)
                activeFates = new();
            return;
        }

        var now = DateTime.UtcNow;
        if (now - lastPoll < PollInterval)
            return;
        lastPoll = now;

        var snapshot = new List<FateSnapshot>();
        var live = new HashSet<uint>();

        foreach (var fate in fates)
        {
            live.Add(fate.FateId);

            var seenBefore = lastFateState.TryGetValue(fate.FateId, out var previous);
            lastFateState[fate.FateId] = fate.State;

            // Edge-triggered, and only on a transition we actually witnessed. A
            // FATE that already reads Failed the first time we see it (walked in
            // late, plugin just loaded) is recorded silently - the game's own
            // clock for that failure is already running and we cannot know how
            // far along it is. "Reset clock" is there for that case.
            if (seenBefore && previous != FateState.Failed && fate.State == FateState.Failed)
                RegisterFailure(fate.Name.ToString(), fate.Progress);

            if (fate.State != FateState.Failed && fate.State != FateState.Ended)
            {
                var awaiting = fate.State == FateState.Preparing;
                var remaining = awaiting || fate.TimeRemaining <= 0
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(fate.TimeRemaining);
                snapshot.Add(new FateSnapshot(fate.Name.ToString(), fate.Progress, remaining, awaiting));
            }
        }

        // A "!" FATE that needed activation and timed out just vanishes from the
        // table without ever reporting Failed. If the last thing we saw it doing
        // was Preparing and now it is gone, that is a failure too.
        foreach (var (id, state) in lastFateState)
        {
            if (!live.Contains(id) && state == FateState.Preparing)
                RegisterFailure("An uninitiated FATE", 0);
        }

        foreach (var id in lastFateState.Keys.Where(k => !live.Contains(k)).ToList())
            lastFateState.Remove(id);

        snapshot.Sort((a, b) => a.TimeRemaining.CompareTo(b.TimeRemaining));
        activeFates = snapshot;
    }

    private void RegisterFailure(string fateName, int progressPercent)
    {
        NunyunuwiSince = DateTime.Now;
        NunyunuwiLastFailure =
            $"{fateName} failed at {progressPercent}% ({DateTime.Now:HH:mm:ss}). Clock restarted.";
        log.Information($"Nunyunuwi clock reset — {NunyunuwiLastFailure}");
    }
}
