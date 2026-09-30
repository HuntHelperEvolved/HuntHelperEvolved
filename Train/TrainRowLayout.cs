using System;

namespace HuntHelperEvolved;

internal readonly record struct TrainRowLayout(float ButtonWidth, float ButtonHeight, float Gap,
    float ActionLeft, float TextLeft, float TextWidth)
{
    internal static TrainRowLayout Create(float width, float buttonHeight, float gap)
    {
        width = Math.Max(1, width);
        buttonHeight = Math.Max(1, buttonHeight);
        // TP precedes the text; the other three actions stay on the right.
        // Only very narrow widths shrink buttons; ordinary windows truncate text.
        var minimumText = Math.Min(width / 4, buttonHeight);
        gap = Math.Clamp(gap, 0, (width - minimumText) / 8);
        var buttonWidth = Math.Min(buttonHeight, (width - minimumText - gap * 5) / 4);
        var actionWidth = buttonWidth * 3 + gap * 2;
        var actionLeft = width - actionWidth;
        var textLeft = buttonWidth + gap;
        return new(buttonWidth, buttonHeight, gap, actionLeft, textLeft, actionLeft - textLeft - gap * 2);
    }

    internal float X(int action) => action == 0 ? 0 : ActionLeft + (action - 1) * (ButtonWidth + Gap);

    internal static float SecondaryTextWidth(float width, float primaryWidth, float secondaryWidth, float gap)
    {
        var available = Math.Max(0, width - Math.Max(0, gap));
        // Share tight rows, but let a short primary label give its unused space
        // to the secondary label before optional age/status detail is added.
        return Math.Min(Math.Max(0, secondaryWidth),
            Math.Max(available * .45f, available - Math.Max(0, primaryWidth)));
    }
}
