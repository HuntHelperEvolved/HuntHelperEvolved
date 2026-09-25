using System;
using System.Text.Json.Serialization;

namespace HuntHelperEvolved.Ipc;

/// <summary>Standalone JSON IPC contract; consumers do not reference the HHE assembly.</summary>
public static class TrainStatusContract
{
    public const int Version = 1;
    public const string SnapshotGate = "HuntHelperEvolved.GetTrainStatusV1";
    public const string OpenGate = "HuntHelperEvolved.OpenTrainPopout";
    public const string ToggleGate = "HuntHelperEvolved.ToggleTrainPopout";
}

/// <summary>
/// Current-world snapshot, refreshed on the framework thread. UpdatedAtUtc is
/// the capture time, not the caller's read time. SyncConnected describes shared
/// data availability; local train and timer data remain usable while disconnected.
/// LoggedIn is false, with no world or expansion rows, until character/world
/// data can be read safely (including logout and area/world transfers).
/// WorldOffline means maintenance/offline status is known and all progress
/// values are unknown. Retained obsolete instance rows may make Recorded
/// exceed the roster Total for the currently configured instances.
/// </summary>
public sealed record TrainStatusSnapshot(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("loggedIn")] bool LoggedIn,
    [property: JsonPropertyName("syncConnected")] bool SyncConnected,
    [property: JsonPropertyName("worldId")] uint WorldId,
    [property: JsonPropertyName("worldName")] string WorldName,
    [property: JsonPropertyName("worldOffline")] bool WorldOffline,
    [property: JsonPropertyName("updatedAtUtc")] DateTime UpdatedAtUtc,
    [property: JsonPropertyName("expansions")] TrainExpansionStatus[] Expansions);

/// <summary>
/// Recorded counts distinct A-rank identities retained in the train, excluding
/// dead/sniped marks and custom flags. Total covers the full expansion roster
/// and known zone instances regardless of train status. Progress covers that
/// full roster and is the mean of known spawn-window fractions (0..1), including
/// confirmed alive marks at 1, or null if none are known. Unknown rows are
/// excluded from that mean; KnownProgressCount states its
/// coverage. Combined progress must be weighted by KnownProgressCount.
/// CompletedProgressCount counts known fractions exactly at 1; null denotes a
/// provider predating this optional field. Subtract that count from both the
/// progress sum (Progress * KnownProgressCount) and count to average remaining
/// known marks. The full roster is complete only when this count equals Total.
/// </summary>
public sealed record TrainExpansionStatus(
    [property: JsonPropertyName("expansion")] string Expansion,
    [property: JsonPropertyName("recorded")] int Recorded,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("knownProgressCount")] int KnownProgressCount,
    [property: JsonPropertyName("progress")] double? Progress,
    [property: JsonPropertyName("completedProgressCount")] int? CompletedProgressCount = null);
