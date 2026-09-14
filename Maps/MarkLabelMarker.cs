using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.MapOverlay;
using KamiToolKit.Nodes;
using System;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>
/// Anchors a live name and health label with zero size to suppress the default icon.
/// The text offset uses node space so it scales with the mark's dot at every zoom.
/// </summary>
public sealed class MarkLabelMarker : MapMarkerNode
{
    private readonly TextNode _text;

    // Assign only changed text to avoid rebuilding native text layout every frame.
    private string _lastText = string.Empty;

    /// <summary>
    /// Read each frame to update health without rebuilding markers and causing flicker.
    /// Returning null or empty hides the label without removing it.
    /// </summary>
    public Func<string?>? TextProvider { get; set; }

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

            // Centre name and health over the dot; the outline keeps text legible on pale maps.
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
