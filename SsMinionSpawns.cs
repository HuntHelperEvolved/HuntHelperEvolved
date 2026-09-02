using System.Collections.Generic;
using System.Numerics;

namespace HuntTrainRelay;

/// <summary>
/// The fixed spots an SS event's minions spawn on, per zone.
///
/// Four to a zone, always the same four, in every hunt zone of the expansion —
/// the minions appear wherever the S rank that triggered the event was killed.
/// Killing all four is what spawns the mark, so knowing where they are is the
/// whole game during an event.
///
/// Coordinates are map coordinates, the numbers shown on the map and in a chat
/// flag, matching SpawnPointData. Territory ids are from Hunt Helper's Enums.cs,
/// the same source SRankZoneReminder uses.
///
/// These are NOT derivable from the game. NotoriousMonster carries a name, a
/// base and a rank and no position at all, and Hunt Helper ships a
/// SpawnDataGatherer that crowdsources ordinary spawn points from players
/// precisely because the client cannot be asked. So this is transcribed from
/// Faloop, and it is a table to correct rather than a lookup that will keep
/// itself current — a new expansion needs six more rows.
/// </summary>
public static class SsMinionSpawns
{
    public static readonly Dictionary<uint, Vector2[]> ByTerritory = new()
    {
        // Forgiven Gossip
        [813] = [new(11.1f, 24.9f), new(12.4f, 10.5f), new(33.0f, 12.0f), new(29.9f, 36.1f)],  // Lakeland
        [814] = [new(8.8f, 28.9f), new(12.2f, 15.1f), new(24.0f, 15.3f), new(33.8f, 32.4f)],  // Kholusia
        [815] = [new(14.1f, 31.9f), new(13.6f, 11.8f), new(30.4f, 9.8f), new(30.1f, 24.7f)],  // Amh Araeng
        [816] = [new(5.7f, 29.6f), new(32.1f, 11.2f), new(25.0f, 22.1f), new(23.5f, 35.6f)],  // Il Mheg
        [817] = [new(14.5f, 36.6f), new(7.4f, 22.8f), new(18.9f, 22.3f), new(29.8f, 13.0f)],  // The Rak'tika Greatwood
        [818] = [new(8.4f, 7.2f), new(25.8f, 9.6f), new(37.7f, 14.0f), new(33.6f, 29.9f)],  // The Tempest

        // Ker Shroud
        [956] = [new(9.3f, 22.1f), new(25.1f, 8.5f), new(26.3f, 32.8f), new(35.0f, 17.9f)],  // Labyrinthos
        [957] = [new(12.1f, 16.3f), new(16.5f, 29.4f), new(22.9f, 10.4f), new(33.2f, 25.0f)],  // Thavnair
        [958] = [new(17.8f, 10.0f), new(22.5f, 32.6f), new(32.1f, 9.0f), new(33.4f, 28.7f)],  // Garlemald
        [959] = [new(11.9f, 20.6f), new(11.7f, 35.9f), new(29.2f, 35.4f), new(33.0f, 23.4f)],  // Mare Lamentorum
        [960] = [new(10.7f, 31.6f), new(16.1f, 16.8f), new(23.4f, 33.1f), new(32.2f, 10.0f)],  // Ultima Thule
        [961] = [new(7.9f, 35.8f), new(16.8f, 7.0f), new(29.0f, 7.3f), new(37.7f, 13.4f)],  // Elpis

        // crystal incarnation
        [1187] = [new(18.3f, 17.9f), new(25.7f, 13.9f), new(15.6f, 28.5f), new(34.6f, 28.2f)],  // Urqopacha
        [1188] = [new(16.7f, 7.5f), new(33.1f, 8.2f), new(15.8f, 32.7f), new(29.6f, 24.6f)],  // Kozama'uka
        [1189] = [new(17.1f, 14.0f), new(35.4f, 22.4f), new(27.9f, 24.7f), new(12.7f, 35.7f)],  // Yak T'el
        [1190] = [new(11.5f, 8.4f), new(23.3f, 13.3f), new(14.9f, 30.9f), new(34.3f, 31.6f)],  // Shaaloani
        [1191] = [new(14.1f, 17.8f), new(15.0f, 34.7f), new(30.1f, 9.7f), new(32.3f, 22.7f)],  // Heritage Found
        [1192] = [new(11.5f, 18.1f), new(27.3f, 7.1f), new(19.7f, 30.7f), new(28.5f, 36.5f)],  // Living Memory
    };

    public static Vector2[] For(uint territoryId) =>
        ByTerritory.TryGetValue(territoryId, out var points) ? points : [];

    /// <summary>Whether a zone's spots are known, rather than only learnable.</summary>
    public static bool Known(uint territoryId) => For(territoryId).Length > 0;
}
