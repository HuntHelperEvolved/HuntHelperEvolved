using HuntHelperEvolved.TrainPresets;
using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public sealed partial class SyncCoordinator
{
    private DateTime? _trainSnapshotConnectionAt;
    public bool HasCurrentTrainSnapshot => IsConnected && _trainSnapshotConnectionAt is not null
        && _trainSnapshotConnectionAt == _client.ConnectedAtUtc;
    public bool SupportsTrainPresets { get; private set; }
    public PresetState TrainPresets { get; private set; } = new();
    public string PresetStatus { get; private set; } = "";
    public string? PendingPresetRequest { get; private set; }
    public long? AcceptedPresetRevision { get; private set; }
    public string? CompletedPresetRequest { get; private set; }
    private DateTime _presetRequestAt;

    public bool SendPreset(string action, TrainPreset? preset = null, string? presetId = null, long? baseRevision = null,
        List<SyncKey>? order = null)
    {
        if (PendingPresetRequest is not null && DateTime.UtcNow - _presetRequestAt < TimeSpan.FromSeconds(10))
            return false;
        if (!IsConnected || !SupportsTrainPresets)
        {
            PresetStatus = "Connect to a server that supports train presets.";
            return false;
        }
        if ((action == "select" || action == "reorder") && (!_config.SyncShareTrain || !HasCurrentTrainSnapshot))
        {
            PresetStatus = "Wait for the shared train before changing its preset or order.";
            return false;
        }
        PresetStatus = "Waiting for the server...";
        PendingPresetRequest = Guid.NewGuid().ToString("N");
        CompletedPresetRequest = null;
        AcceptedPresetRevision = null;
        _presetRequestAt = DateTime.UtcNow;
        if (action == "reorder") DiffTrain();
        _client.Send(new TrainPresetMessage
        {
            RequestId = PendingPresetRequest, Action = action, Preset = preset?.Copy(), PresetId = presetId,
            BaseRevision = baseRevision ?? TrainPresets.Revision,
            Order = order,
        });
        return true;
    }

    private void ApplyPresets(TrainPresetsBroadcast message)
    {
        if (message.State.Revision < TrainPresets.Revision) return;
        TrainPresets = message.State;
        if (message.RequestId == PendingPresetRequest)
        {
            CompletedPresetRequest = message.RequestId;
            PendingPresetRequest = null;
            PresetStatus = message.Accepted ? "Preset change saved on the server." : message.Message;
            AcceptedPresetRevision = message.Accepted ? message.State.Revision : null;
        }
        if (_config.SyncShareTrain) ApplyOrder(message.Order);
    }

    private void CheckPresetRequest()
    {
        if (PendingPresetRequest is null || IsConnected && DateTime.UtcNow - _presetRequestAt < TimeSpan.FromSeconds(10)) return;
        PresetStatus = "The server has not confirmed the preset change. Reconnect to check the shared library before retrying.";
        PendingPresetRequest = null;
    }
}
