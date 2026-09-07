using System;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>Version 2 native IPC record. Legacy HH records remain external adapters only.</summary>
public record NativeTrainRecord(string Name, uint NameId, uint TerritoryId, uint MapId,
    uint Instance, uint WorldId, string WorldName, Vector2 Position, bool Dead,
    DateTime LastSeenUtc, DateTime? DeathObservedAtUtc, DateTime? SnipedAtUtc);
