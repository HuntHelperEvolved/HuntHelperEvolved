using System.Collections.Generic;
using System.Numerics;

namespace HuntTrainRelay;

/// <summary>
/// The fixed spots an SS event's minions spawn on, per zone.
///
/// Four to a zone, always the same four — killing every one of them is what
/// spawns the mark, so knowing where they are is the whole game during an
/// event.
///
/// EMPTY ON PURPOSE, for now. The coordinates are not in anything reachable
/// from here: Hunt Helper's spawn point data tags each point A, B or S and has
/// no notion of an SS, its mark files carry names and ids but no positions, and
/// the game's own sheets do not hold them either. They are documented on
/// community hunt resources, which is not a source to transcribe from without
/// someone checking the numbers.
///
/// Until it is filled, SsEventWatcher still marks where the minions were
/// actually found, so the feature works — it just cannot tell you where to look
/// before you have looked. Adding a zone here upgrades it from "where they
/// were" to "where they will be".
///
/// Coordinates are map coordinates, the numbers shown on the map and in a chat
/// flag, matching SpawnPointData.
/// </summary>
public static class SsMinionSpawns
{
    public static readonly Dictionary<uint, Vector2[]> ByTerritory = new()
    {
        // [814] = { new(00.0f, 00.0f), ... },   // Kholusia   — Forgiven Gossip
        // [958] = { new(00.0f, 00.0f), ... },   // Garlemald  — Ker Shroud
        // [1191] = { new(00.0f, 00.0f), ... },  // Dawntrail  — crystal incarnation
    };

    public static Vector2[] For(uint territoryId) =>
        ByTerritory.TryGetValue(territoryId, out var points) ? points : [];

    /// <summary>Whether a zone's spots are known, rather than only learnable.</summary>
    public static bool Known(uint territoryId) => For(territoryId).Length > 0;
}
