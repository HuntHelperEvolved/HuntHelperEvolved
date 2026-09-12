using System;
using Dalamud.Game.ClientState.Conditions;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace HuntHelperEvolved.Sync;

/// <summary>User-requested travel only. Wait for the destination world before issuing teleport.</summary>
public sealed class LifestreamTravel : IDisposable
{
    private readonly IDalamudPluginInterface _plugin;
    private readonly IFramework _framework;
    private readonly MarkDetector _detector;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private (uint World, AetheryteData Aetheryte, TravelHandoff Handoff)? _pending;
    private DateTime? _teleportAcceptedAt;
    private bool _teleportWasActive;
    private (uint World, uint Territory, uint Instance) _destination;
    private InstanceTravelHandoff? _instanceHandoff;
    public string Status { get; private set; } = "";
    public LifestreamTravel(IDalamudPluginInterface plugin, IFramework framework, MarkDetector detector, IChatGui chat, IPluginLog log)
    { _plugin=plugin; _framework=framework; _detector=detector; _chat=chat; _log=log; framework.Update += Update; }
    public bool Available
    {
        get { try { _plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc(); return true; } catch { return false; } }
    }
    public bool Busy => _pending is not null || _teleportAcceptedAt is not null || _instanceHandoff is not null;
    public void Start(uint world, uint territory, Vector2 position, uint instance = 0)
    {
        try
        {
            if (Busy || _plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc())
            { Status="Lifestream is already travelling."; return; }
            if (_detector.CurrentWorldId() == 0) { Status="Log in before starting travel."; return; }
            if (TeleportHelper.NearestTo(territory, position) is not { } nearest)
            { Status="No eligible aetheryte for this location. Check the aetheryte blacklist."; return; }
            if (_detector.CurrentWorldId() != world && !_plugin.GetIpcSubscriber<uint,bool>("Lifestream.ChangeWorldById").InvokeFunc(world))
            { Status="Lifestream could not start travel to that world."; return; }
            _destination = (world, territory, instance);
            _pending = (world, nearest, new TravelHandoff(world, DateTime.UtcNow));
            Status=$"Changing world, then teleporting to {nearest.Name}.";
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Update(IFramework _)
    {
        if (!Busy) return;
        try
        {
            var now=DateTime.UtcNow;
            var player=HuntTally.Service.Objects.LocalPlayer;
            var ready=player is not null && !player.IsCasting
                && !HuntTally.Service.Condition[ConditionFlag.BetweenAreas]
                && !HuntTally.Service.Condition[ConditionFlag.BetweenAreas51];
            var busy = _plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc();
            if (_instanceHandoff is { } instanceHandoff)
            {
                // Query optional instance IPC only once normal travel has finished.
                var atDestination = ready && !busy && _detector.CurrentWorldId() == _destination.World
                    && _detector.CurrentTerritoryId == _destination.Territory;
                var action = instanceHandoff.Step(now, _detector.CurrentWorldId(), _detector.CurrentTerritoryId,
                    atDestination ? (uint)Math.Max(0, _plugin.GetIpcSubscriber<int>("Lifestream.GetCurrentInstance").InvokeFunc()) : 0,
                    atDestination ? _plugin.GetIpcSubscriber<int>("Lifestream.GetNumberOfInstances").InvokeFunc() : 0,
                    atDestination && _plugin.GetIpcSubscriber<bool>("Lifestream.CanChangeInstance").InvokeFunc(), busy, ready);
                switch (action)
                {
                    case InstanceTravelAction.Change:
                        _plugin.GetIpcSubscriber<int, object>("Lifestream.ChangeInstance").InvokeAction((int)_destination.Instance);
                        Status=$"Changing to instance {_destination.Instance}.";
                        break;
                    case InstanceTravelAction.Complete:
                        _instanceHandoff=null; Status=""; break;
                    case InstanceTravelAction.Unavailable:
                        _instanceHandoff=null; Status="That instance is no longer available."; break;
                    case InstanceTravelAction.TimedOut:
                        _instanceHandoff=null; Status="Instance travel timed out. Check the destination and aetheryte access."; break;
                }
                return;
            }
            if (_teleportAcceptedAt is { } acceptedAt)
            {
                if (!ready || busy) _teleportWasActive = true;
                // Clear the progress label after casting/loading ends. An accepted
                // request that never starts must not leave a permanent progress label.
                if ((ready && !busy && (_teleportWasActive || now - acceptedAt >= TimeSpan.FromSeconds(10)))
                    || now - acceptedAt >= TimeSpan.FromMinutes(2))
                {
                    _teleportAcceptedAt=null; _teleportWasActive=false; Status="";
                    if (_destination.Instance > 0)
                    {
                        _instanceHandoff=new(_destination.World, _destination.Territory, _destination.Instance, now);
                        Status=$"Waiting to enter instance {_destination.Instance}.";
                    }
                }
                return;
            }
            if (_pending is not { } pending) return;
            if (pending.Handoff.Expired(now))
            { _pending=null; Status="Travel timed out; teleport was not accepted. Check attunement or character state."; return; }
            if (!pending.Handoff.ShouldAttempt(now, _detector.CurrentWorldId(), busy, ready)) return;
            if (Teleport(pending.Aetheryte)) _pending=null;
            else { pending.Handoff.Refused(now); Status=$"Waiting for teleport to {pending.Aetheryte.Name} to become available…"; }
        }
        catch (Exception ex) { _pending=null; _teleportAcceptedAt=null; _instanceHandoff=null; Fail(ex); }
    }
    private bool Teleport(AetheryteData nearest)
    {
        var accepted = _plugin.GetIpcSubscriber<uint,byte,bool>("Lifestream.Teleport")
            .InvokeFunc(TeleportHelper.ResolveId(nearest.AetheryteId), nearest.SubIndex);
        Status=accepted ? $"Teleporting to {nearest.Name}." : $"Lifestream refused teleport to {nearest.Name}; check attunement and character state.";
        if (accepted)
        {
            _teleportAcceptedAt=DateTime.UtcNow;
            _teleportWasActive=false;
            _chat.Print("[Hunt Helper Evolved] " + Status);
        }
        return accepted;
    }
    public void Cancel()
    {
        _pending=null;
        _instanceHandoff=null;
        _teleportAcceptedAt=null;
        _teleportWasActive=false;
        try { _plugin.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction(); Status="Travel cancelled."; }
        catch (Exception ex) { Fail(ex); }
    }
    private void Fail(Exception ex) { Status="Lifestream travel is unavailable or failed."; _log.Warning(ex, Status); }
    public void Dispose() { _framework.Update -= Update; _pending=null; _teleportAcceptedAt=null; _instanceHandoff=null; }
}
