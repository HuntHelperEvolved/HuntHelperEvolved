using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HuntHelperEvolved.Sync;
public sealed partial class SyncCoordinator
{
    public bool SupportsScoutRemoval { get; private set; }
    public IReadOnlyList<ScoutCreditDto> ScoutCredits => _scoutCredits;
    private List<ScoutCreditDto> _scoutCredits = new();
    public void ChangeScoutCredit(string name, bool restore)
    {
        if (!IsConnected || !SupportsScoutRemoval || !_config.SyncShareTrain) return;
        _client.Send(restore ? new TrainScoutsMessage { Restore=new() { name } } : new TrainScoutsMessage { Remove=new() { name } });
    }
    private void ApplyScoutCredits(List<ScoutCreditDto> credits)
    {
        _scoutCredits=credits;
        var removed=credits.Where(c => c.Removed).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_config.SyncShareTrain && _config.AdditionalScouts.RemoveAll(n => removed.Contains(n.Trim())) > 0) _config.Save();
    }
    public bool SupportsTrainFinish { get; private set; }
    public IReadOnlyList<string> TrainScouts => _trainScouts;
    private List<string> _trainScouts = new();
    private string? _manualScoutsSent;
    private readonly Dictionary<string, TaskCompletionSource<TrainFinishResult>> _finishRequests = new();

    public TrainFinishMessage PrepareFinish(IEnumerable<TrackedMark> history) => new()
    {
        RequestId = Guid.NewGuid().ToString(), ClearShared = _config.SyncShareTrain,
        WatchRevision = _watchRevision,
        ExpectedMarks = _detector.Ordered().Select(m => ToSyncMark(m, _known.GetValueOrDefault(m.Key).Revision)).ToList(),
        History = history.Select(m => new SyncMark { NameId=m.ModelId, WorldId=m.WorldId, Instance=m.Instance,
            TerritoryId=m.TerritoryId, Dead=m.Dead, LastSeen=m.LastSeenUtc, DeathAt=m.DeathObservedAtUtc, SnipedAt=m.SnipedAtUtc }).ToList()
    };
    public Task<TrainFinishResult> SubmitFinish(TrainFinishMessage request, string connectionId)
    {
        if (!IsConnected || !SupportsTrainFinish || ClientId != connectionId)
            return Task.FromResult(new TrainFinishResult { Message="Server unavailable, changed connection, or needs an update; train kept." });
        var completion = new TaskCompletionSource<TrainFinishResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _finishRequests[request.RequestId] = completion;
        _client.Send(request);
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    public void ForgetFinish(string id) => _finishRequests.Remove(id);
    private void ResetCompletionConnection()
    {
        foreach (var request in _finishRequests.Values)
            request.TrySetResult(new TrainFinishResult { Message="Disconnected before acknowledgement; check the shared train before retrying." });
        _finishRequests.Clear(); SupportsScoutRemoval=false; _scoutCredits.Clear(); SupportsTrainFinish=false; _trainScouts.Clear(); _manualScoutsSent=null;
    }
    private void SendManualScouts()
    {
        if (!SupportsTrainFinish || !_config.SyncShareTrain || _detector.Marks.Count==0) return;
        var names=_config.AdditionalScouts.Where(n=>!string.IsNullOrWhiteSpace(n)).Select(n=>n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var serialized=SyncProtocol.Serialize(names);
        if (serialized==_manualScoutsSent) return;
        var previous = _manualScoutsSent is null ? new List<string>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(_manualScoutsSent) ?? new();
        _manualScoutsSent=serialized;
        var removed = SupportsScoutRemoval ? previous.Except(names, StringComparer.OrdinalIgnoreCase).ToList() : new();
        if (names.Count>0 || removed.Count>0) _client.Send(new TrainScoutsMessage { Names=names, Remove=removed });
    }
}
