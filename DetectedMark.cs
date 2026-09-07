using System;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>
/// One mark detected by our own scanning. Position is stored in in-game map
/// coordinates (the 1-42ish numbers shown on the map), not raw world position,
/// so it can be handed straight to a map link or an aetheryte distance check.
/// </summary>
public class DetectedMark
{
    public string Name = string.Empty;
    public uint NameId;
    public uint TerritoryId;
    public uint MapId;
    public uint Instance;

    /// <summary>
    /// The world it was seen on. Part of a mark's identity, not decoration: the
    /// same mark is up on every world at once, and they are different marks.
    /// </summary>
    public uint WorldId;
    public string WorldName = string.Empty;
    public Vector2 MapPosition;
    public bool Dead;
    public DateTime FirstSeenUtc;
    public DateTime LastSeenUtc;
    public DateTime? DeathObservedAtUtc;

    /// <summary>
    /// Position in the train. Assigned incrementally as marks are first spotted,
    /// so the default order is simply the order they were scouted — and it can
    /// be rewritten freely by drag-and-drop reordering.
    /// </summary>
    public int Order;

    /// <summary>
    /// A conductor-placed flag rather than a detected mark. Behaves like any
    /// other row (teleport, dead, drag, auto-advance) but is left out of the
    /// final train report.
    /// </summary>
    public bool IsCustom;

    /// <summary>Zone label for custom entries, which have no mark data to look up.</summary>
    public string ZoneName = string.Empty;

    /// <summary>A scout intends to prep this mark before the train reaches it.</summary>
    public bool Spiced;

    /// <summary>
    /// When the train arrived to find this mark already gone — killed by
    /// somebody else after it was scouted.
    ///
    /// Not a kill time, and deliberately not stored as one. All this says is
    /// when the mark was found missing, which is the LATEST it can have died;
    /// the earliest is LastSeenUtc, when it was last seen standing there. The
    /// truth is somewhere between the two, and the report says so rather than
    /// picking one and calling it the kill.
    /// </summary>
    public DateTime? SnipedAtUtc;

    /// <summary>
    /// What makes this mark this mark. Compare against it rather than picking
    /// fields off by hand — the same mark is up on every world at once, and a
    /// comparison that forgets to say which one silently matches the wrong row.
    /// </summary>
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Instance, WorldId);
}

