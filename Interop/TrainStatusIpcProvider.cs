using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using HuntHelperEvolved.Ipc;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelperEvolved;

/// <summary>Publishes immutable JSON snapshots without reading game state in another plugin's call.</summary>
internal sealed class TrainStatusIpcProvider : IDisposable
{
    private sealed record PublishedSnapshot(string Json, bool LoggedIn);
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly Func<TrainStatusSnapshot> _capture;
    private readonly Func<bool> _openPopout;
    private readonly Func<bool> _togglePopout;
    private ICallGateProvider<string>? _snapshotGate;
    private ICallGateProvider<bool>? _openGate;
    private ICallGateProvider<bool>? _toggleGate;
    private PublishedSnapshot _snapshot = UnavailableSnapshot();
    private volatile bool _disposed;
    private double _secondsSinceCapture = 1;
    private int _openPending;
    private bool _captureFailed;

    internal TrainStatusIpcProvider(IDalamudPluginInterface pluginInterface, IFramework framework,
        IPluginLog log, Func<TrainStatusSnapshot> capture, Func<bool> openPopout, Func<bool> togglePopout)
    {
        _framework = framework;
        _log = log;
        _capture = capture;
        _openPopout = openPopout;
        _togglePopout = togglePopout;
        try
        {
            _snapshotGate = pluginInterface.GetIpcProvider<string>(TrainStatusContract.SnapshotGate);
            _snapshotGate.RegisterFunc(() => Volatile.Read(ref _snapshot).Json);
            _openGate = pluginInterface.GetIpcProvider<bool>(TrainStatusContract.OpenGate);
            _openGate.RegisterFunc(RequestOpen);
            _toggleGate = pluginInterface.GetIpcProvider<bool>(TrainStatusContract.ToggleGate);
            _toggleGate.RegisterFunc(RequestToggle);
            // The first capture waits for Update: plugin constructors need not run
            // on the framework thread, and no IPC callback enumerates live state.
            _framework.Update += Update;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static PublishedSnapshot UnavailableSnapshot() => new(JsonSerializer.Serialize(new TrainStatusSnapshot(
        TrainStatusContract.Version, false, false, 0, string.Empty, false, DateTime.UtcNow,
        Array.Empty<TrainExpansionStatus>())), false);

    private void Update(IFramework framework)
    {
        if (_disposed) return;
        _secondsSinceCapture += framework.UpdateDelta.TotalSeconds;
        if (_secondsSinceCapture < 0.5) return;
        _secondsSinceCapture = 0;
        try
        {
            var captured = _capture();
            var snapshot = new PublishedSnapshot(JsonSerializer.Serialize(captured), captured.LoggedIn);
            if (!_disposed) Volatile.Write(ref _snapshot, snapshot);
            _captureFailed = false;
        }
        catch (Exception ex)
        {
            // Retain the real capture timestamp so consumers can reject stale
            // data. One log per failed period avoids flooding the framework log.
            if (!_captureFailed) _log.Error(ex, "Could not capture train status for IPC.");
            _captureFailed = true;
        }
    }

    // True means accepted for framework execution, not that a draw has happened.
    // Preserve the original idempotent open endpoint for existing consumers.
    private bool RequestOpen()
    {
        if (_disposed || !Volatile.Read(ref _snapshot).LoggedIn) return false;
        if (Interlocked.Exchange(ref _openPending, 1) == 0) _ = OpenOnFrameworkThread();
        return true;
    }

    private async Task OpenOnFrameworkThread()
    {
        try
        {
            await _framework.RunOnFrameworkThread(() =>
            {
                if (!_disposed) _openPopout();
            });
        }
        catch (Exception ex) { _log.Error(ex, "Could not open the train popout requested over IPC."); }
        finally { Interlocked.Exchange(ref _openPending, 0); }
    }

    private bool RequestToggle()
    {
        if (_disposed || !Volatile.Read(ref _snapshot).LoggedIn) return false;
        // Each accepted click gets its own framework action: two clicks must
        // toggle twice even when both arrive before the framework runs them.
        _ = ToggleOnFrameworkThread();
        return true;
    }

    private async Task ToggleOnFrameworkThread()
    {
        try
        {
            await _framework.RunOnFrameworkThread(() =>
            {
                // The callback also rechecks live character readiness before
                // changing the window, since the snapshot may lag a logout.
                if (!_disposed && Volatile.Read(ref _snapshot).LoggedIn) _togglePopout();
            });
        }
        catch (Exception ex) { _log.Error(ex, "Could not toggle the train popout requested over IPC."); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _framework.Update -= Update;
        Volatile.Write(ref _snapshot, UnavailableSnapshot());
        try { _snapshotGate?.UnregisterFunc(); } catch { }
        try { _openGate?.UnregisterFunc(); } catch { }
        try { _toggleGate?.UnregisterFunc(); } catch { }
        _snapshotGate = null;
        _openGate = null;
        _toggleGate = null;
    }
}
