using System;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>Compact train record used by scouting summaries and the original HHE IPC contract.</summary>
public record struct TrainMobRecord(
    string Name,
    uint MobID,
    uint TerritoryID,
    uint MapID,
    uint Instance,
    Vector2 Position,
    bool Dead,
    DateTime LastSeenUTC
);

