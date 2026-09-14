using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace HuntTally;

/// <summary>
/// Observes the local player's own actions to answer the one question the
/// object table cannot: did I actually hit this thing?
///
/// The game awards hunt mark credit for dealing damage. Every signal reachable
/// through public API is a proxy for that and gets it wrong in one direction or
/// the other - the combat flag misses a mark killed in a single hit, and
/// targeting counts marks other people killed while you had them selected.
/// ActionEffectHandler.Receive is the game telling the client "this caster's
/// action resolved against these targets", which is as close as the client gets.
///
/// It is a NECESSARY condition for credit, not a sufficient one. The game also
/// applies an undocumented contribution threshold: landing a single action on
/// an S rank puts you on its enmity table but does not by itself earn seals or
/// achievement progress. Nothing here can see that threshold.
///
/// This is a hook, so unlike the rest of the plugin it can break on a patch.
/// The address comes from FFXIVClientStructs rather than a signature string
/// kept here, so a game update is fixed by updating Dalamud rather than this
/// plugin. If the hook cannot be created, <see cref="IsActive"/> stays false
/// and <see cref="KillTracker"/> falls back to the old combat heuristic - a
/// broken hook degrades to the previous behaviour rather than counting nothing.
/// </summary>
public sealed unsafe class DamageWatch : IDisposable
{
    /// <summary>
    /// How long a damaged entity is remembered.
    ///
    /// This only has to bridge the gap between an action resolving and the next
    /// poll noticing, because the tracker copies the flag onto its own entry
    /// the moment it sees it. Seconds would do; 30 is slack for a stalled
    /// frame.
    /// </summary>
    private const double RememberSeconds = 30;

    /// <summary>
    /// Defensive cap on the target array. The game's own limit is 16; reading
    /// past it if the struct ever drifts would be a wild pointer walk.
    /// </summary>
    private const int MaxTargets = 16;

    private readonly Dictionary<uint, DateTime> damaged = new();
    private readonly List<uint> staleBuffer = new();

    private readonly object gate = new();
    private Hook<ActionEffectHandler.Delegates.Receive>? hook;
    private ActionEffectHandler.Delegates.Receive? original;
    private volatile uint localPlayerId;
    private volatile bool stopping;
    private bool active;


    /// <summary>True when the damage signal is live and can be trusted.</summary>
    public bool IsActive => active && !stopping;

    /// <summary>
    /// Actions by the local player seen so far. Surfaced in settings purely so
    /// a hook that silently stops firing is diagnosable rather than mysterious.
    /// </summary>
    public long EventsSeen { get; private set; }

    public string Status { get; private set; } = "Not initialised.";

    public DamageWatch()
    {
        try
        {
            hook = Service.Interop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                (nint)ActionEffectHandler.MemberFunctionPointers.Receive, ReceiveDetour);
            // Capture the trampoline before Enable can invoke our callback.
            original = hook.Original;
            Service.Framework.Update += UpdatePlayerIdentity;
            hook.Enable();
            active = true;
            Status = "Active.";
            Service.Log.Information("Damage detection active; using action effects for kill credit.");
        }
        catch (Exception ex)
        {
            stopping = true;
            var cleanup = new HuntHelperEvolved.CleanupSequence();
            cleanup.Run("damage identity callback", () => Service.Framework.Update -= UpdatePlayerIdentity);
            cleanup.Run("failed damage hook disable", () => hook?.Disable());
            cleanup.Run("failed damage hook dispose", () => { hook?.Dispose(); hook = null; });
            try { cleanup.ThrowIfFailed(); }
            catch (Exception cleanupError) { Service.Log.Error(cleanupError, "Failed to roll back damage hook startup."); }
            Status = "Unavailable - falling back to combat detection.";
            Service.Log.Error(ex, "Could not hook ActionEffectHandler.Receive. "
                                  + "Kill credit falls back to the combat heuristic.");
        }
    }

    public void Dispose()
    {
        stopping = true;
        localPlayerId = 0;
        var cleanup = new HuntHelperEvolved.CleanupSequence();
        cleanup.Run("damage identity callback", () => Service.Framework.Update -= UpdatePlayerIdentity);
        cleanup.Run("damage hook disable", () => hook?.Disable());
        // Disable first, then wait for a running observation/original call before
        // releasing its trampoline. Do not replace the hook under its callback.
        lock (gate)
        {
            cleanup.Run("damage hook dispose", () => { hook?.Dispose(); hook = null; });
            damaged.Clear();
            active = false;
        }
        cleanup.ThrowIfFailed();
    }

    private void UpdatePlayerIdentity(Dalamud.Plugin.Services.IFramework framework)
    {
        if (stopping) return;
        var id = HuntHelperEvolved.GameReadiness.CanReadCharacters
            ? Service.Objects.LocalPlayer?.EntityId ?? 0 : 0;
        lock (gate)
        {
            if (localPlayerId != id) damaged.Clear();
            localPlayerId = id;
        }
    }

    /// <summary>Whether the local player's action has resolved against this entity recently.</summary>
    public bool WasDamagedByPlayer(uint entityId) { lock (gate) return damaged.ContainsKey(entityId); }

    public void Prune(DateTime now)
    {
        lock (gate)
        {
            if (damaged.Count == 0)
                return;

            staleBuffer.Clear();
            foreach (var (id, seen) in damaged)
            {
                if ((now - seen).TotalSeconds > RememberSeconds)
                    staleBuffer.Add(id);
            }

            foreach (var id in staleBuffer)
                damaged.Remove(id);
        }
    }

    public void Clear() { lock (gate) damaged.Clear(); }

    private void ReceiveDetour(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        lock (gate)
        {
            // Observation failures (including logging failures) cannot skip forwarding.
            try
            {
                if (!stopping) Observe(casterEntityId, header, targetEntityIds);
            }
            catch (Exception ex)
            {
                try { Service.Log.Error(ex, "Failed to read an action effect."); }
                catch { /* Logging must not interrupt the game callback. */ }
            }
            finally
            {
                original!(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
            }
        }
    }

    private void Observe(uint casterEntityId, ActionEffectHandler.Header* header, GameObjectId* targetEntityIds)
    {
        if (header is null || targetEntityIds is null)
            return;

        // Game objects are sampled on the framework thread, never from this hook.
        if (localPlayerId is 0 or 0xE0000000 || casterEntityId != localPlayerId)
            return;

        EventsSeen++;

        var now = DateTime.UtcNow;
        var count = Math.Min((int)header->NumTargets, MaxTargets);

        // Every target of the action counts, not just those taking damage.
        // A debuff or a DoT application puts you on the enmity table and earns
        // credit just as a hit does, and decoding effect type bytes to tell
        // them apart would be guessing at values FFXIVClientStructs does not
        // name.
        for (var i = 0; i < count; i++)
            damaged[targetEntityIds[i].ObjectId] = now;
    }
}
