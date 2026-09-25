using HuntHelperEvolved.Ipc;
using System;
using System.Linq;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private TrainStatusSnapshot CaptureTrainStatus()
    {
        var now = DateTime.UtcNow;
        if (_disposed || !GameReadiness.CanReadCharacters)
            return TrainStatusBuilder.Build(new() { NowUtc = now, SyncConnected = _sync.IsConnected });

        var world = _detector.CurrentWorldId();
        var evidence = _detector.OtherRanks.Values.Concat(_sync.RemoteSightings.Values)
            .Select(s => new TrainStatusInstanceEvidence(s.NameId, s.TerritoryId, s.WorldId, s.Instance)).ToList();
        evidence.Add(new(0, _detector.CurrentTerritoryId, world, MarkDetector.GetCurrentInstance()));
        return TrainStatusBuilder.Build(new()
        {
            LoggedIn = _clientState.IsLoggedIn,
            SyncConnected = _sync.IsConnected,
            WorldId = world,
            WorldName = _detector.CurrentWorldName(),
            NowUtc = now,
            Marks = _detector.Ordered(),
            Kills = _config.ARankKills,
            Sightings = _config.ARankSightings,
            InstanceEvidence = evidence,
            SRankStatuses = _sync.SRankStatuses.Values.ToArray(),
            Faloop = _sync.Faloop,
        });
    }

    private bool OpenTrainPopoutFromIpc()
    {
        if (_disposed || !GameReadiness.CanReadCharacters) return false;
        SelectTrainPage(TrainWorkspacePage.Route);
        _trainPopoutVisible = true;
        return true;
    }

    private bool ToggleTrainPopoutFromIpc()
    {
        if (_disposed || !GameReadiness.CanReadCharacters) return false;
        var visible = !_trainPopoutVisible;
        if (visible) SelectTrainPage(TrainWorkspacePage.Route);
        _trainPopoutVisible = visible;
        return true;
    }
}
