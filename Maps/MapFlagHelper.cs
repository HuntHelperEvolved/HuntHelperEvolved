using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using System.Linq;

namespace HuntHelperEvolved;

/// <summary>
/// Opens the player's map with a flag on a detected mark, which also sets their
/// real in-game flag (same as Ctrl+Right-Click).
///
/// An earlier version of this always landed in the corner of the map. The cause
/// was the Map ID: it was being hand-entered, but Map ID isn't visible anywhere
/// in the game UI — it's derived from the territory via the game's data sheet.
/// A wrong (or zero) Map ID produces exactly that corner behaviour. Here it's
/// computed by MarkDetector.GetMapId, so it's always right.
/// </summary>
public static class MapFlagHelper
{
    public static bool FlagMark(IGameGui gameGui, DetectedMark mark) =>
        FlagPosition(gameGui, mark.TerritoryId, mark.MapId, mark.Instance,
                     mark.MapPosition.X, mark.MapPosition.Y);

    /// <summary>
    /// Drops the flag on a bare position, for callers holding coordinates
    /// rather than a train row — a sighting on the map, say.
    /// </summary>
    public static bool FlagPosition(
        IGameGui gameGui, uint territoryId, uint mapId, uint instance, float mapX, float mapY)
    {
        if (mapId == 0 || territoryId == 0) return false;

        var seString = SeString.CreateMapLinkWithInstance(
            territoryId,
            mapId,
            instance == 0 ? null : (int)instance,
            mapX,
            mapY);

        var mapLink = seString.Payloads.OfType<MapLinkPayload>().FirstOrDefault();
        if (mapLink == null) return false;

        return gameGui.OpenMapWithMapLink(mapLink);
    }
}
