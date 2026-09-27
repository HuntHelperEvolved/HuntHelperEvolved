using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>The FOUND chat palette and stronger map colours in the same rank colour families.</summary>
public static class HuntRankPalette
{
    public const ushort ChatA = 12;
    public const ushort ChatB = 34;
    public const ushort ChatS = 506;

    // UIForegroundPayload uses UIColor.Dark regardless of the selected UI theme.
    // RGB values verified against the game's UIColor sheet (2026.09.15):
    // 12 = FF9999, 34 = 9FEEF1, 506 = FFFF66. The map uses more saturated
    // coral, cyan and yellow so small markers stand out against the terrain.
    // Map colours remain user-editable; chat retains the game's palette.
    public static readonly Vector4 MapA = new(1f, 0.3f, 0.3f, 1f);
    public static readonly Vector4 MapB = new(0f, 0.85f, 0.95f, 1f);
    public static readonly Vector4 MapS = new(1f, 0.94f, 0f, 1f);
}
