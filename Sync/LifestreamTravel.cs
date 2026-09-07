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
    public string Status { get; private set; } = "";
    public LifestreamTravel(IDalamudPluginInterface plugin, IFramework framework, MarkDetector detector, IChatGui chat, IPluginLog log)
    { _plugin=plugin; _framework=framework; _detector=detector; _chat=chat; _log=log; framework.Update += Update; }
    public bool Available
    {
        get { try { _plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc(); return true; } catch { return false; } }
    }
    public bool Busy => _pending is not null;
    public void Start(uint world, uint territory, Vector2 position)
    {
        try
        {
            if (_pending is not null || _plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc())
            { Status="Lifestream is already travelling."; return; }
            if (_detector.CurrentWorldId() == 0) { Status="Log in before starting travel."; return; }
            if (TeleportHelper.NearestTo(territory, position) is not { } nearest)
            { Status="No eligible aetheryte for this location. Check the aetheryte blacklist."; return; }
            if (_detector.CurrentWorldId() != world && !_plugin.GetIpcSubscriber<uint,bool>("Lifestream.ChangeWorldById").InvokeFunc(world))
            { Status="Lifestream could not start travel to that world."; return; }
            _pending = (world, nearest, new TravelHandoff(world, DateTime.UtcNow));
            Status=$"Changing world, then teleporting to {nearest.Name}.";
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Update(IFramework _)
    {
        if (_pending is not { } pending) return;
        try
        {
            var now=DateTime.UtcNow;
            if (pending.Handoff.Expired(now))
            { _pending=null; Status="Travel timed out; teleport was not accepted. Check attunement or character state."; return; }
            var player=HuntTally.Service.Objects.LocalPlayer;
            var ready=player is not null && !player.IsCasting
                && !HuntTally.Service.Condition[ConditionFlag.BetweenAreas]
                && !HuntTally.Service.Condition[ConditionFlag.BetweenAreas51];
            if (!pending.Handoff.ShouldAttempt(now, _detector.CurrentWorldId(),
                _plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc(), ready)) return;
            if (Teleport(pending.Aetheryte)) _pending=null;
            else { pending.Handoff.Refused(now); Status=$"Waiting for teleport to {pending.Aetheryte.Name} to become available…"; }
        }
        catch (Exception ex) { _pending=null; Fail(ex); }
    }
    private bool Teleport(AetheryteData nearest)
    {
        var accepted = _plugin.GetIpcSubscriber<uint,byte,bool>("Lifestream.Teleport")
            .InvokeFunc(TeleportHelper.ResolveId(nearest.AetheryteId), nearest.SubIndex);
        Status=accepted ? $"Teleporting to {nearest.Name}." : $"Lifestream refused teleport to {nearest.Name}; check attunement and character state.";
        if (accepted) _chat.Print("[Hunt Helper Evolved] " + Status);
        return accepted;
    }
    public void Cancel()
    {
        _pending=null;
        try { _plugin.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction(); Status="Travel cancelled."; }
        catch (Exception ex) { Fail(ex); }
    }
    private void Fail(Exception ex) { Status="Lifestream travel is unavailable or failed."; _log.Warning(ex, Status); }
    public void Dispose() { _framework.Update -= Update; _pending=null; }
}
