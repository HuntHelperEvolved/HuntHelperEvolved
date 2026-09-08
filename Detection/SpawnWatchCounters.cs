using System;
using System.Collections.Generic;
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
///    to fail, so the countdown on screen is always the honest one — including
///    a failure that happens while we are too far away to be told about it
///    directly. See the long comment in OnUpdate for how that actually works.
///
/// Adapted from Hunt Helper's <c>CounterUI.Fates.cs</c> and
/// <c>DrawWeeEaCounter</c> (img02/HuntHelper, MIT) — the FATE tracking below in
/// particular reproduces its exact mechanism, not just its intent.
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

    /// <summary>
    /// Every FATE we are still watching this zone visit, holding the same
    /// <see cref="IFate"/> reference — and so the same underlying pointer —
    /// from the poll it was first seen on, alongside the last state we read
    /// off it. Kept even after the FATE stops appearing in <see cref="fates"/>
    /// itself; see OnUpdate for why that is the point of keeping it at all.
    /// </summary>
    private readonly Dictionary<uint, (IFate Fate, FateState LastState)> trackedFates = new();
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
        trackedFates.Clear();
        activeFates = new();
    }

    private void OnTerritoryChanged(uint territory)
    {
        // FATE ids and the clock only mean anything within one visit to the
        // zone, and a stale Failed entry left over from the last zone would
        // otherwise trip the reset the instant the new table loads.
        trackedFates.Clear();
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
            if (trackedFates.Count > 0)
                trackedFates.Clear();
            return;
        }

        // fates enumerates FateManager's own linked list, so an id only ever
        // appears in it while the game still considers that FATE active. This
        // first pass just notices anything new; the actual state reads happen
        // below, off our own held references rather than off this enumeration.
        var live = new HashSet<uint>();

        foreach (var fate in fates)
        {
            live.Add(fate.FateId);
            trackedFates.TryAdd(fate.FateId, (fate, fate.State));
        }

        // Dalamud's Fate is a readonly struct wrapping one raw pointer, and
        // every property on it — State, Name, Progress, all of it — re-reads
        // the native FateContext at that address on every access; nothing is
        // cached at construction. That is what makes the loop below work: it
        // keeps reading .State off the SAME reference a FATE was first grabbed
        // with, on every poll, whether or not that FATE is still showing up in
        // `fates` above.
        //
        // That is deliberate, and it is the actual fix for the bug this was
        // written to catch. FateManager unlinks a FATE from its list — which is
        // what makes it stop appearing in `fates` — as its own step, seemingly
        // separate from writing FateState.Failed into the FateContext itself,
        // and for a FATE failing far enough away that the game does not bother
        // keeping your client closely synced to it, the unlink can already have
        // happened by the time your own next poll runs. Simply enumerating
        // `fates` and asking "is anything here newly Failed" — however often —
        // can end up never once finding it in the table in a Failed state, no
        // matter how tight the poll: not because the write is missed, but
        // because the entry granting access to it is already gone. Keeping the
        // pointer from while it WAS still listed sidesteps that: the memory
        // is not freed just because the entry was unlinked, and reading .State
        // off it later still sees the Failed write when it happens.
        //
        // This is not a theory reached by reasoning about the network model —
        // it is Hunt Helper's own approach, reproduced deliberately rather than
        // reinvented: CounterUI.Fates.cs keeps a HashSet<IFate> it only ever
        // adds to (a HashSet.Add that finds an equal FateId already present is
        // a no-op, so the first struct grabbed for an id is the one kept), and
        // that is what its own out-of-range resets were actually running on —
        // not, as first assumed here, simply how often it polled.
        //
        // One real risk in doing this: if the game frees and reuses that exact
        // memory address for an unrelated FATE before we read it again, this
        // would misreport under the old id. Hunt Helper carries the same risk
        // and it is not a new one introduced here — nothing in IFate exposes a
        // way to check the pointer is still backing the FATE it started as.
        var snapshot = new List<FateSnapshot>();
        var resolved = new List<uint>();

        foreach (var (id, entry) in trackedFates)
        {
            var (fate, lastState) = entry;
            var state = fate.State;
            var stillListed = live.Contains(id);

            // Edge-triggered: only a transition we actually witnessed counts.
            // A FATE that already reads Failed the moment we first grab it
            // (walked in late, plugin just loaded) is recorded silently — the
            // game's own clock for that failure is already running and we
            // cannot know how far along it is. "Reset clock" is there for that.
            if (lastState != FateState.Failed && state == FateState.Failed)
            {
                RegisterFailure(fate.Name.ToString(), fate.Progress);
                resolved.Add(id);
                continue;
            }

            if (state == FateState.Ended)
            {
                resolved.Add(id);
                continue;
            }

            // The one case a held reference does not cover: a "!" FATE that
            // needed activation and timed out is torn down without Failed ever
            // being written, as if it had never started — so re-reading this
            // pointer forever would just keep showing Preparing. Absence from
            // THIS poll's live enumeration is the only signal available for
            // that specific case, which is exactly why it is checked here and
            // nowhere else in this loop.
            if (!stillListed && state == FateState.Preparing)
            {
                RegisterFailure(fate.Name.ToString(), fate.Progress);
                resolved.Add(id);
                continue;
            }

            trackedFates[id] = (fate, state);

            var awaiting = state == FateState.Preparing;
            var remaining = awaiting || fate.TimeRemaining <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(fate.TimeRemaining);
            snapshot.Add(new FateSnapshot(fate.Name.ToString(), fate.Progress, remaining, awaiting));
        }

        foreach (var id in resolved)
            trackedFates.Remove(id);

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
