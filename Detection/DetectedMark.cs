using System;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>
/// A locally detected mark. Position uses in-game map coordinates for map links and aetheryte checks.
/// </summary>
public class DetectedMark
{
    public string Name = string.Empty;
    public uint NameId;
    public uint TerritoryId;
    public uint MapId;
    public uint Instance;

    /// <summary>
    /// Part of the identity: the same mark on another world is a separate entry.
    /// </summary>
    public uint WorldId;
    public string WorldName = string.Empty;
    public Vector2 MapPosition;
    public bool Dead;
    public DateTime FirstSeenUtc;
    public DateTime LastSeenUtc;
    public DateTime? DeathObservedAtUtc;

    /// <summary>
    /// Defaults to scouting order; drag-and-drop can reorder it.
    /// </summary>
    public int Order;

    /// <summary>
    /// A conductor-placed flag with normal row controls, excluded from the final report.
    /// </summary>
    public bool IsCustom;

    /// <summary>Zone label for custom entries, which have no mark data to look up.</summary>
    public string ZoneName = string.Empty;

    /// <summary>A scout intends to prep this mark before the train reaches it.</summary>
    public bool Spiced;

    /// <summary>
    /// When the mark was found missing, giving the latest possible death time.
    /// LastSeenUtc gives the earliest bound; neither is a witnessed kill time.
    /// </summary>
    public DateTime? SnipedAtUtc;

    /// <summary>
    /// Compare the full key to avoid matching a mark on another world or instance.
    /// </summary>
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Instance, WorldId);
}

