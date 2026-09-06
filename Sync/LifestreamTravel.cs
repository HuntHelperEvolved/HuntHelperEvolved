using System;
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
    private (uint World, AetheryteData Aetheryte, DateTime Deadline)? _pending;
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
            if (_detector.CurrentWorldId() == world) { Teleport(nearest); return; }
            if (!_plugin.GetIpcSubscriber<uint,bool>("Lifestream.ChangeWorldById").InvokeFunc(world))
            { Status="Lifestream could not start travel to that world."; return; }
            _pending = (world, nearest, DateTime.UtcNow.AddMinutes(15));
            Status=$"Changing world, then teleporting to {nearest.Name}.";
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Update(IFramework _)
    {
        if (_pending is not { } pending) return;
        try
        {
            if (DateTime.UtcNow > pending.Deadline)
            { _pending=null; Status="Travel timed out; teleport was cancelled."; return; }
            if (_plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc() || _detector.CurrentWorldId() == 0) return;
            if (_detector.CurrentWorldId() != pending.World)
            {
                if (DateTime.UtcNow > pending.Deadline.AddMinutes(-15).AddSeconds(5))
                { _pending=null; Status="World travel stopped before arrival; teleport cancelled."; }
                return;
            }
            _pending=null;
            Teleport(pending.Aetheryte);
        }
        catch (Exception ex) { _pending=null; Fail(ex); }
    }
    private void Teleport(AetheryteData nearest)
    {
        var accepted = _plugin.GetIpcSubscriber<uint,byte,bool>("Lifestream.Teleport")
            .InvokeFunc(TeleportHelper.ResolveId(nearest.AetheryteId), nearest.SubIndex);
        Status=accepted ? $"Teleporting to {nearest.Name}." : $"Lifestream refused teleport to {nearest.Name}; check attunement and character state.";
        _chat.Print("[Hunt Helper Evolved] " + Status);
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
