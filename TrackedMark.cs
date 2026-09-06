using System;

namespace HuntHelperEvolved;

public class TrackedMark
{
    public string Name = string.Empty;
    public uint ModelId;
    public uint Instance;
    public uint WorldId;
    public string WorldName = string.Empty;
    public uint TerritoryId;
    public (uint, uint, uint) Key => (ModelId, Instance, WorldId);
    public TrackedMark Copy() => (TrackedMark)MemberwiseClone();
    public bool Dead;

    /// <summary>
    /// The moment we personally observed this mark flip to dead while polling.
    /// Null when first seen dead; a sighting time is never an exact death time.
    /// </summary>
    public DateTime? DeathObservedAtUtc;
    public DateTime LastSeenUtc;

    /// <summary>
    /// When the train found this mark already gone, if it did. See
    /// DetectedMark.SnipedAtUtc — it is the latest the mark can have died, not
    /// the moment it died.
    /// </summary>
    public DateTime? SnipedAtUtc;
}

