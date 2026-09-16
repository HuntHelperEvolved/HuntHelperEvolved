using HuntHelperEvolved.TrainPresets;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

public sealed class TrainPresetMessage
{
    public string RequestId { get; set; } = "";
    public string Type => "train.preset";
    public long BaseRevision { get; set; }
    public string Action { get; set; } = "";
    public TrainPreset? Preset { get; set; }
    public string? PresetId { get; set; }
    public List<SyncKey>? Order { get; set; }
}

public sealed class TrainPresetsBroadcast
{
    public string RequestId { get; set; } = "";
    public PresetState State { get; set; } = new();
    public List<SyncKey> Order { get; set; } = new();
    public bool Accepted { get; set; } = true;
    public string Message { get; set; } = "";
}
