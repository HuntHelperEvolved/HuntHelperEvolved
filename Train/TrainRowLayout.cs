using System;

namespace HuntHelperEvolved;

internal readonly record struct TrainRowLayout(float ButtonWidth, float ButtonHeight, float Gap, int Columns,
    float ActionLeft, float ActionWidth, float ActionHeight, float NameWidth, bool ActionsBelow)
{
    internal static TrainRowLayout Create(float width, float buttonHeight, float gap, float minimumNameWidth)
    {
        width = Math.Max(1, width);
        buttonHeight = Math.Max(1, buttonHeight);
        gap = Math.Max(0, gap);
        var buttonWidth = Math.Min(width, buttonHeight);
        var columns = Math.Clamp((int)((width + gap) / (buttonWidth + gap)), 1, 4);
        var actionWidth = columns * buttonWidth + (columns - 1) * gap;
        var rows = (4 + columns - 1) / columns;
        var below = columns < 4 || width < actionWidth + gap * 2 + minimumNameWidth;
        return new(buttonWidth, buttonHeight, gap, columns, width - actionWidth, actionWidth,
            rows * buttonHeight + (rows - 1) * gap,
            below ? width : Math.Max(1, width - actionWidth - gap * 2), below);
    }

    internal float X(int action) => ActionLeft + action % Columns * (ButtonWidth + Gap);
    internal float Y(int action) => action / Columns * (ButtonHeight + Gap);
}
