using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Conditions;
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
///    one real hour. Restart on observed failures or conservatively when a
///    FATE disappears without an observed result. Client observations alone
///    cannot establish the outcome of every out-of-range FATE.
///
/// Adapted from Hunt Helper's <c>CounterUI.Fates.cs</c> and
/// <c>DrawWeeEaCounter</c> (img02/HuntHelper, MIT) — the FATE tracking below
/// uses managed snapshots so removed native FATE objects are never retained.
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
    private readonly ICondition condition;

    // IFate properties dereference native memory. Retain only copied values,
    // never an IFate or its address, beyond the current live-table iteration.
    private readonly record struct TrackedFate(string Name, int Progress, FateState State);
    private Dictionary<uint, TrackedFate> trackedFates = new();
    private List<FateSnapshot> activeFates = new();

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

    /// <summary>Last observed failure or unconfirmed disappearance this visit.</summary>
    public string NunyunuwiLastFailure { get; private set; } = string.Empty;

    /// <summary>FATEs currently running or pending, soonest to expire first.</summary>
    public IReadOnlyList<FateSnapshot> ActiveFates => activeFates;

    public SpawnWatchCounters(
        IFramework framework, IClientState clientState, IObjectTable objects,
        IFateTable fates, IPluginLog log, ICondition condition)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objects = objects;
        this.fates = fates;
        this.log = log;
        this.condition = condition;

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
        if (!CanReadWorld)
            return 0;

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
        trackedFates.Clear();
        if (activeFates.Count > 0)
            activeFates = new();
    }

    private void OnTerritoryChanged(uint territory)
    {
        // Even a return to the same territory starts a fresh observation window.
        ResetNunyunuwiClock();
    }

    private bool CanReadWorld => clientState.IsLoggedIn
        && !condition[ConditionFlag.BetweenAreas]
        && !condition[ConditionFlag.BetweenAreas51];

    private void OnUpdate(IFramework _)
    {
        // TerritoryType can still name the old zone while its objects unload.
        // Discard the visit before touching the FATE table during a transition.
        if (!CanReadWorld || clientState.TerritoryType != SouthernThanalanTerritory)
        {
            ResetNunyunuwiClock();
            return;
        }

        var current = new Dictionary<uint, TrackedFate>();
        var snapshot = new List<FateSnapshot>();
        foreach (var fate in fates)
        {
            // Copy everything while this entry belongs to the live table on
            // the framework update thread. No native wrapper escapes this loop.
            var id = fate.FateId;
            var state = fate.State;
            var name = fate.Name.ToString();
            var progress = fate.Progress;
            current[id] = new TrackedFate(name, progress, state);

            // A failure already present on first observation has no known time.
            // Keep terminal states until removal to avoid repeated resets.
            if (state == FateState.Failed)
            {
                if (trackedFates.TryGetValue(id, out var previous) && previous.State != FateState.Failed)
                    RegisterFailure(name, progress);
                continue;
            }
            if (state == FateState.Ended)
                continue;

            var awaiting = state == FateState.Preparing;
            var seconds = awaiting ? 0 : fate.TimeRemaining;
            var remaining = seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            snapshot.Add(new FateSnapshot(name, progress, remaining, awaiting));
        }

        foreach (var (id, previous) in trackedFates)
        {
            if (current.ContainsKey(id) || previous.State is FateState.Failed or FateState.Ended)
                continue;

            // The final state may never be observed, especially out of range.
            // Removal does not authorize another native read. Conservatively
            // restart from the managed snapshot and report uncertainty honestly.
            NunyunuwiSince = DateTime.Now;
            NunyunuwiLastFailure =
                $"{previous.Name} disappeared without an observed result (last seen at {previous.Progress}%) " +
                $"({DateTime.Now:HH:mm:ss}). Outcome unconfirmed; clock restarted conservatively.";
            log.Information($"Nunyunuwi clock reset — {NunyunuwiLastFailure}");
        }

        trackedFates = current;
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
