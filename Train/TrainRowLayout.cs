using System;

namespace HuntHelperEvolved;

internal readonly record struct TrainRowLayout(float ButtonWidth, float ButtonHeight, float Gap,
    float ActionLeft, float TextWidth)
{
    internal static TrainRowLayout Create(float width, float buttonHeight, float gap)
    {
        width = Math.Max(1, width);
        buttonHeight = Math.Max(1, buttonHeight);
        // Keep all four actions on the same line. Only widths smaller than the
        // action strip shrink buttons; ordinary narrow windows truncate text.
        var minimumText = Math.Min(width / 4, buttonHeight);
        gap = Math.Clamp(gap, 0, (width - minimumText) / 8);
        var buttonWidth = Math.Min(buttonHeight, (width - minimumText - gap * 5) / 4);
        var actionWidth = buttonWidth * 4 + gap * 3;
        var actionLeft = width - actionWidth;
        return new(buttonWidth, buttonHeight, gap, actionLeft, actionLeft - gap * 2);
    }

    internal float X(int action) => ActionLeft + action * (ButtonWidth + Gap);
}
