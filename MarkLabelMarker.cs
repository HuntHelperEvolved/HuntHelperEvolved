using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.MapOverlay;
using KamiToolKit.Nodes;
using System;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>
/// A live mark's name and health, written on the map beside its dot.
///
/// Hunt Helper only ever puts this in a tooltip and in its priority-mob panel,
/// so you have to go looking for it. The point of writing it on the map is that
/// a scout can see at a glance which marks are up and which are already being
/// pulled, without hovering each dot in turn.
///
/// The marker itself draws nothing. It is an anchor at the mark's position with
/// a text node hung off it, and its own size is zero on purpose: MapMarkerNode
/// hands its size to the image node it keeps for an icon, so zero is what keeps
/// that icon — which we never set, and which defaults to id 0 — from drawing
/// anything at all. Position then lands on the mark rather than on the corner
/// of a box around it.
///
/// The offset that puts the text under the dot is in node space, not map
/// coordinates, so it is scaled by exactly what scales the dot: the label stays
/// the same distance from it at every zoom, instead of drifting away as you
/// zoom in.
/// </summary>
public sealed class MarkLabelMarker : MapMarkerNode
{
    private readonly TextNode _text;

    // Native text layout is redone on assignment, so only assign when it would
    // actually say something different. Health changes a few times a second at
    // most; this runs every frame.
    private string _lastText = string.Empty;

    /// <summary>
    /// What the label should say, asked afresh each frame.
    ///
    /// A provider rather than a fixed string for the same reason the player
    /// guides use one for their position: health changes constantly, and
    /// nothing else here would notice. Rebuilding every marker on the map each
    /// time a mark took damage would blink the lot of them several times a
    /// second, which is exactly what the guides stopped doing.
    ///
    /// Returning null or empty hides the label without removing it.
    /// </summary>
    public Func<string?>? TextProvider { get; init; }

    public MarkLabelMarker(Vector4 colour, Vector4 outlineColour, float fontSize, float width, float verticalOffset)
    {
        Size = Vector2.Zero;

        var size = (uint)Math.Clamp(fontSize, 6f, 48f);

        _text = new TextNode
        {
            FontType = FontType.Axis,
            FontSize = size,
            LineSpacing = size + 2,
            TextColor = colour,
            TextOutlineColor = outlineColour,

            // Two lines, the name over the health, anchored at the top centre
            // of the box so the block stays centred on the dot however long
            // the name is. Edge is the outline the game's own map labels carry,
            // and without it white text vanishes over a pale map.
            AlignmentType = AlignmentType.Top,
            TextFlags = TextFlags.Edge | TextFlags.MultiLine,

            Size = new Vector2(width, (size + 4) * 2),
            Position = new Vector2(-width / 2f, verticalOffset),
            IsVisible = true,
        };

        _text.AttachNode(this);
    }

    protected override void OnUpdate()
    {
        var text = TextProvider?.Invoke();

        if (string.IsNullOrEmpty(text))
        {
            _text.IsVisible = false;
            return;
        }

        _text.IsVisible = true;

        if (text == _lastText) return;
        _lastText = text;
        _text.String = text;
    }
}
