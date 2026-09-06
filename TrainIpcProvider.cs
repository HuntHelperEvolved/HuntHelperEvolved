using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

/// <summary>
/// Publishes the train over Dalamud IPC, so other plugins can read it and add
/// to it.
///
/// Two sets of gates, for two different readers:
///
///   HuntHelperEvolved.*  always. This plugin's own contract, for anything
///                        written against it deliberately.
///
///   HH.*                 only when Hunt Helper is not installed. Everything
///                        already written to integrate with hunt trains talks
///                        to Hunt Helper, and asking every one of those authors
///                        to add a second code path is not a plan. The gates
///                        answer with the same names, the same signatures and
///                        the same record shape, so a consumer cannot tell the
///                        difference and does not have to.
///
/// The absence check is the same rule the /hh command aliases follow, and for a
/// stronger reason. A Dalamud call gate is keyed by name across the whole
/// process: registering one Hunt Helper already holds does not fail loudly, it
/// quietly takes it over, and every plugin asking Hunt Helper for its train
/// would start getting this one's instead. Claiming a gate is only ever safe
/// when nobody else wants it.
///
/// Checked once, at load, exactly as the commands are. Hunt Helper being
/// installed mid-session is a restart either way.
/// </summary>
public sealed class TrainIpcProvider : IDisposable
{
    /// <summary>
    /// The version of the HH.* contract implemented here. Hunt Helper's own
    /// GetVersion returns 1, and this answers the same because it is the same
    /// contract — the number describes the shape of the gates, not which plugin
    /// happens to be answering them.
    /// </summary>
    private const uint HuntHelperApiVersion = 1;

    /// <summary>
    /// Bumped when one of this plugin's own gates changes signature. Separate
    /// from the number above, which is not ours to move.
    /// </summary>
    public const int ApiVersion = 1;

    private const string OwnApiVersionGate = "HuntHelperEvolved.ApiVersion";
    private const string OwnGetTrainListGate = "HuntHelperEvolved.GetTrainList";
    private const string OwnImportTrainListGate = "HuntHelperEvolved.ImportTrainList";

    // Hunt Helper's own names and signatures, from
    // HuntHelper/Managers/IpcSystem.cs (img02/HuntHelper, MIT).
    private const string HhGetVersionGate = "HH.GetVersion";
    private const string HhGetTrainListGate = "HH.GetTrainList";
    private const string HhImportTrainListGate = "HH.ImportTrainList";

    private readonly MarkDetector _detector;
    private readonly IPluginLog _log;

    private ICallGateProvider<int>? _ownApiVersion;
    private ICallGateProvider<List<HuntHelperMobRecord>>? _ownGetTrainList;
    private ICallGateProvider<List<HuntHelperMobRecord>, bool>? _ownImportTrainList;

    private ICallGateProvider<uint>? _hhGetVersion;
    private ICallGateProvider<List<HuntHelperMobRecord>>? _hhGetTrainList;
    private ICallGateProvider<List<HuntHelperMobRecord>, bool>? _hhImportTrainList;

    /// <summary>Whether the Hunt Helper gate names were claimed.</summary>
    public bool ClaimedHuntHelperGates { get; }

    public TrainIpcProvider(
        IDalamudPluginInterface pluginInterface, MarkDetector detector, IPluginLog log)
    {
        _detector = detector;
        _log = log;

        try
        {
            _ownApiVersion = pluginInterface.GetIpcProvider<int>(OwnApiVersionGate);
            _ownApiVersion.RegisterFunc(() => ApiVersion);

            _ownGetTrainList = pluginInterface.GetIpcProvider<List<HuntHelperMobRecord>>(OwnGetTrainListGate);
            _ownGetTrainList.RegisterFunc(GetTrainList);

            _ownImportTrainList = pluginInterface
                .GetIpcProvider<List<HuntHelperMobRecord>, bool>(OwnImportTrainListGate);
            _ownImportTrainList.RegisterAction(ImportTrainList);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not publish this plugin's own IPC gates.");
        }

        ClaimedHuntHelperGates = !HuntHelperIpc.IsHuntHelperInstalled(pluginInterface);
        if (!ClaimedHuntHelperGates)
        {
            _log.Information("Hunt Helper is installed, so it keeps the HH.* IPC gates.");
            return;
        }

        try
        {
            _hhGetVersion = pluginInterface.GetIpcProvider<uint>(HhGetVersionGate);
            _hhGetVersion.RegisterFunc(() => HuntHelperApiVersion);

            _hhGetTrainList = pluginInterface.GetIpcProvider<List<HuntHelperMobRecord>>(HhGetTrainListGate);
            _hhGetTrainList.RegisterFunc(GetTrainList);

            _hhImportTrainList = pluginInterface
                .GetIpcProvider<List<HuntHelperMobRecord>, bool>(HhImportTrainListGate);
            _hhImportTrainList.RegisterAction(ImportTrainList);

            _log.Information("Hunt Helper is absent, so its IPC gates are answered here.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not answer Hunt Helper's IPC gates.");
        }
    }

    /// <summary>
    /// The train, in the record shape Hunt Helper's consumers already parse.
    ///
    /// Custom flags are left out. They are rally points a conductor dropped for
    /// people to walk to, not marks, and a consumer reading this expects marks
    /// — the scouting report leaves them out for the same reason.
    ///
    /// Never throws: this runs inside somebody else's plugin's call, and an
    /// exception here would surface there as a fault in their code.
    /// </summary>
    private List<HuntHelperMobRecord> GetTrainList()
    {
        try
        {
            return _detector.Ordered()
                .Where(m => !m.IsCustom)
                .Select(m => new HuntHelperMobRecord(
                    m.Name, m.NameId, m.TerritoryId, m.MapId, m.Instance,
                    m.MapPosition, m.Dead, m.LastSeenUtc))
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not build the train list for an IPC caller.");
            return new List<HuntHelperMobRecord>();
        }
    }

    /// <summary>
    /// Folds an incoming list into the train, on the same terms as pasting an
    /// import code: existing marks win, and nothing already here is overwritten.
    ///
    /// The world is not in this shape — it is not in Hunt Helper's either — so
    /// MarkDetector.Merge stamps these with the world the player is on, which
    /// is the only world an IPC caller could sensibly have meant.
    /// </summary>
    private void ImportTrainList(List<HuntHelperMobRecord> incoming)
    {
        try
        {
            if (incoming == null) return;

            var marks = incoming.Select(m => new DetectedMark
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
                DeathObservedAtUtc = m.Dead ? m.LastSeenUTC : null,
            }).ToList();

            var added = _detector.Merge(marks);
            _log.Information($"IPC import: {marks.Count} marks offered, {added} new.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not import a train list offered over IPC.");
        }
    }

    public void Dispose()
    {
        // Unregister rather than leave dangling: a gate still pointing at a
        // disposed plugin is a crash in whoever calls it next.
        try { _ownApiVersion?.UnregisterFunc(); } catch { /* already gone */ }
        try { _ownGetTrainList?.UnregisterFunc(); } catch { /* already gone */ }
        try { _ownImportTrainList?.UnregisterAction(); } catch { /* already gone */ }

        try { _hhGetVersion?.UnregisterFunc(); } catch { /* already gone */ }
        try { _hhGetTrainList?.UnregisterFunc(); } catch { /* already gone */ }
        try { _hhImportTrainList?.UnregisterAction(); } catch { /* already gone */ }

        _ownApiVersion = null;
        _ownGetTrainList = null;
        _ownImportTrainList = null;
        _hhGetVersion = null;
        _hhGetTrainList = null;
        _hhImportTrainList = null;
    }
}
