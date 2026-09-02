using KamiToolKit.MapOverlay;
using System;
using System.Numerics;

namespace HuntTrainRelay;

/// <summary>
/// A map marker measured in world units instead of screen pixels.
///
/// Ordinary markers keep a constant size however far the map is zoomed — which
/// is right for an icon, and wrong for anything that stands for a distance. The
/// detection ring has to stay the same number of yalms across at every zoom or
/// it is not telling the truth about range.
///
/// The arithmetic, from MapOverlayController: the overlay node is scaled by the
/// map's zoom, and then every marker is scaled by 1/MarkerPositionScaling to
/// undo it. So a marker finally renders at
///
///     Size * MarkerScale * MapScale / MarkerPositionScaling
///
/// and setting MarkerScale to MarkerPositionScaling leaves Size * MapScale —
/// a size in the map's own units, which is what positions already use.
///
/// MarkerPositionScaling changes as the player zooms, so it is re-read on every
/// frame through OnUpdate, which MapMarkerNode calls before it lays the marker
/// out. Rotation is untouched by that method, which is what lets the projected
/// path be a single rotated quad rather than a line of sprites.
/// </summary>
public sealed class WorldSizedMarker : MapMarkerNode
{
    private readonly Func<float> markerPositionScaling;

    public WorldSizedMarker(Func<float> markerPositionScaling)
    {
        this.markerPositionScaling = markerPositionScaling;
    }

    /// <summary>Size in map units, the same space <see cref="MapMarkerNode.Position"/> uses.</summary>
    public Vector2 WorldSize { get; set; } = Vector2.One;

    /// <summary>
    /// Where the marker should sit, asked afresh each frame.
    ///
    /// This is what stops the guides flickering. Anything that follows the
    /// player used to be redrawn by tearing down every marker on the map and
    /// building them again, which blinks — and blinks the spawn points along
    /// with it, since they go at the same time. A marker that moves itself
    /// never has to be replaced at all: OnUpdate runs every frame anyway, so
    /// the position is simply current, and it tracks at the frame rate rather
    /// than in ten-per-second steps.
    /// </summary>
    public Func<Vector2>? PositionProvider { get; init; }

    /// <summary>Facing, asked afresh each frame. Same reasoning as the position.</summary>
    public Func<float>? RotationProvider { get; init; }

    protected override void OnUpdate()
    {
        var scaling = markerPositionScaling();

        // Zero would collapse the marker to nothing; fall back to behaving like
        // an ordinary fixed-size marker rather than vanishing.
        if (scaling <= 0f)
            scaling = 1f;

        Size = WorldSize;
        MarkerScale = scaling;

        // Set before the base class lays the marker out — Update calls this
        // first and then reads both.
        if (PositionProvider is { } position)
            Position = position();

        if (RotationProvider is { } rotation)
            Rotation = rotation();
    }
}
