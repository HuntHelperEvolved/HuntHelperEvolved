using System;

namespace HuntHelperEvolved;

public enum SpawnStatus { Unknown, Spawned, NotSpawned }

/// <summary>
/// An S-rank watch for the current train. Label is the display text (mark
/// name, or mark name + which known spawn spot for Narrow-rift specifically).
///
/// Kept out of Configuration.cs so the report code that decides which watches
/// a report carries can be built - and tested - without dragging in Dalamud.
/// </summary>
[Serializable]
public class FlagEntry
{
    public string Label { get; set; } = string.Empty;
    public SpawnStatus SpawnStatus { get; set; } = SpawnStatus.Unknown;

    /// <summary>Zone this watch belongs to, so the zone-entry reminder can match it.</summary>
    public uint TerritoryId { get; set; }

    /// <summary>Map coordinates of the chosen spawn spot, if one was picked.</summary>
    public bool HasLocation { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}
