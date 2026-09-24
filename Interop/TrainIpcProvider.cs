using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelperEvolved;

/// <summary>Publishes HHE's train through its own Dalamud IPC endpoints.</summary>
public sealed class TrainIpcProvider : IDisposable
{
    /// <summary>Version of the HHE IPC contract.</summary>
    public const int ApiVersion = 2;

    private const string OwnApiVersionGate = "HuntHelperEvolved.ApiVersion";
    private const string OwnGetTrainListGate = "HuntHelperEvolved.GetTrainList";
    private const string OwnImportTrainListGate = "HuntHelperEvolved.ImportTrainList";

    private volatile bool _disposed;
    private readonly IFramework _framework;
    private sealed record Snapshot(TrainMobRecord[] Legacy, NativeTrainRecord[] Native);
    private Snapshot _snapshot = new(Array.Empty<TrainMobRecord>(), Array.Empty<NativeTrainRecord>());
    private double _secondsSinceSnapshot;
    private readonly MarkDetector _detector;
    private readonly IPluginLog _log;

    private ICallGateProvider<List<NativeTrainRecord>>? _nativeGet;
    private ICallGateProvider<List<NativeTrainRecord>, bool>? _nativeImport;
    private ICallGateProvider<int>? _ownApiVersion;
    private ICallGateProvider<List<TrainMobRecord>>? _ownGetTrainList;
    private ICallGateProvider<List<TrainMobRecord>, bool>? _ownImportTrainList;

    public TrainIpcProvider(
        IDalamudPluginInterface pluginInterface, IFramework framework, MarkDetector detector, IPluginLog log)
    {
        _detector = detector;
        _framework = framework;
        _log = log;
        RefreshSnapshot();
        _framework.Update += UpdateSnapshot;

        try
        {
            _ownApiVersion = pluginInterface.GetIpcProvider<int>(OwnApiVersionGate);
            _ownApiVersion.RegisterFunc(() => ApiVersion);
            _nativeGet = pluginInterface.GetIpcProvider<List<NativeTrainRecord>>("HuntHelperEvolved.GetTrainListV2");
            _nativeGet.RegisterFunc(() => Volatile.Read(ref _snapshot).Native.ToList());
            _nativeImport = pluginInterface.GetIpcProvider<List<NativeTrainRecord>, bool>("HuntHelperEvolved.ImportTrainListV2");
            _nativeImport.RegisterAction(ImportNative);

            _ownGetTrainList = pluginInterface.GetIpcProvider<List<TrainMobRecord>>(OwnGetTrainListGate);
            _ownGetTrainList.RegisterFunc(GetTrainList);

            _ownImportTrainList = pluginInterface
                .GetIpcProvider<List<TrainMobRecord>, bool>(OwnImportTrainListGate);
            _ownImportTrainList.RegisterAction(ImportTrainList);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not publish this plugin's own IPC gates.");
        }
    }

    /// <summary>
    /// The train in the original HHE IPC record shape.
    ///
    /// Custom flags are left out. They are rally points a conductor dropped for
    /// people to walk to, not marks, and a consumer reading this expects marks
    /// — the scouting report leaves them out for the same reason.
    ///
    /// Never throws: this runs inside somebody else's plugin's call, and an
    /// exception here would surface there as a fault in their code.
    /// </summary>
    private List<TrainMobRecord> GetTrainList() => Volatile.Read(ref _snapshot).Legacy.ToList();

    // Records contain copied values. IPC callers never enumerate the live detector,
    // and each caller owns its returned list. Snapshot age is at most one scan tick.
    private void UpdateSnapshot(IFramework framework)
    {
        if (_disposed) return;
        _secondsSinceSnapshot += framework.UpdateDelta.TotalSeconds;
        if (_secondsSinceSnapshot < 0.1) return;
        _secondsSinceSnapshot = 0;
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        var marks = _detector.Ordered().Where(m => !m.IsCustom).ToList();
        var legacy = marks.Select(m => new TrainMobRecord(m.Name, m.NameId, m.TerritoryId,
            m.MapId, m.Instance, m.MapPosition, m.Dead, m.LastSeenUtc)).ToArray();
        var native = marks.Select(m => new NativeTrainRecord(m.Name, m.NameId, m.TerritoryId,
            m.MapId, m.Instance, m.WorldId, m.WorldName, m.MapPosition, m.Dead,
            m.LastSeenUtc, m.DeathObservedAtUtc, m.SnipedAtUtc)).ToArray();
        Volatile.Write(ref _snapshot, new Snapshot(legacy, native));
    }

    private async Task MergeOnFrameworkThread(List<DetectedMark> marks)
    {
        try
        {
            await _framework.RunOnFrameworkThread(() =>
            {
                if (_disposed) return;
                var added = _detector.Merge(marks);
                RefreshSnapshot();
                _log.Information($"IPC import: {marks.Count} marks offered, {added} new.");
            });
        }
        catch (Exception ex) { _log.Error(ex, "Could not import a train list offered over IPC."); }
    }

    /// <summary>
    /// Folds an incoming list into the train, on the same terms as pasting an
    /// import code: existing marks win, and nothing already here is overwritten.
    ///
    /// The original contract has no world field, so
    /// MarkDetector.Merge stamps these with the world the player is on, which
    /// is the only world an IPC caller could sensibly have meant.
    /// </summary>
    private void ImportTrainList(List<TrainMobRecord> incoming)
    {
        try
        {
            if (_disposed || incoming == null) return;

            var marks = incoming.Take(1000).Where(m => m.Instance <= 9
                && float.IsFinite(m.Position.X) && float.IsFinite(m.Position.Y)).Select(m => new DetectedMark
            {
                Name = m.Name,
                NameId = m.MobID,
                TerritoryId = m.TerritoryID,
                MapId = m.MapID,
                Instance = m.Instance,
                MapPosition = m.Position,
                Dead = m.Dead,
                FirstSeenUtc = m.LastSeenUTC,
                LastSeenUtc = m.LastSeenUTC,
                DeathObservedAtUtc = null, // Legacy IPC carries no death timestamp.
            }).ToList();

            _ = MergeOnFrameworkThread(marks);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not import a train list offered over IPC.");
        }
    }

    private void ImportNative(List<NativeTrainRecord> incoming)
    {
        try
        {
            if (_disposed || incoming is null) return;
            var snapshot = incoming.Take(1000).Where(m => m is not null && m.WorldId != 0
                && m.Instance <= 9 && float.IsFinite(m.Position.X) && float.IsFinite(m.Position.Y))
                .Select(m => new DetectedMark { Name=m.Name, NameId=m.NameId,
                    TerritoryId=m.TerritoryId, MapId=m.MapId, Instance=m.Instance,
                    WorldId=m.WorldId, WorldName=m.WorldName, MapPosition=m.Position,
                    Dead=m.Dead, LastSeenUtc=m.LastSeenUtc, FirstSeenUtc=m.LastSeenUtc,
                    DeathObservedAtUtc=m.DeathObservedAtUtc, SnipedAtUtc=m.SnipedAtUtc }).ToList();
            _ = MergeOnFrameworkThread(snapshot);
        }
        catch (Exception ex) { _log.Error(ex, "Could not import a native train list offered over IPC."); }
    }

    public void Dispose()
    {
        _disposed = true;
        _framework.Update -= UpdateSnapshot;
        Volatile.Write(ref _snapshot, new Snapshot(Array.Empty<TrainMobRecord>(), Array.Empty<NativeTrainRecord>()));
        // Unregister rather than leave dangling: a gate still pointing at a
        // disposed plugin is a crash in whoever calls it next.
        try { _nativeGet?.UnregisterFunc(); } catch { }
        try { _nativeImport?.UnregisterAction(); } catch { }
        try { _ownApiVersion?.UnregisterFunc(); } catch { /* already gone */ }
        try { _ownGetTrainList?.UnregisterFunc(); } catch { /* already gone */ }
        try { _ownImportTrainList?.UnregisterAction(); } catch { /* already gone */ }

        _ownApiVersion = null;
        _ownGetTrainList = null;
        _ownImportTrainList = null;
    }
}
