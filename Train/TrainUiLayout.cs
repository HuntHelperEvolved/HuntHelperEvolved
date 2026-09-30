using System;

namespace HuntHelperEvolved;

internal readonly record struct TrainContextLayout(float PresetWidth, float NameWidth,
    bool WrapFlagControls, bool StackFlagButton)
{
    internal int Rows => 1 + (WrapFlagControls ? 1 : 0) + (StackFlagButton ? 1 : 0);

    internal static TrainContextLayout Compact(float width, float scale, float gap, float flagWidth)
    {
        width = Math.Max(1, width);
        var presetMinimum = 75 * scale;
        var nameMinimum = 60 * scale;
        var fieldsWidth = width - flagWidth - gap * 2;
        if (fieldsWidth >= presetMinimum + nameMinimum)
        {
            var presetWidth = Math.Clamp(fieldsWidth * .63f, presetMinimum, fieldsWidth - nameMinimum);
            return new(presetWidth, fieldsWidth - presetWidth, false, false);
        }
        if (width >= nameMinimum + flagWidth + gap)
            return new(width, width - flagWidth - gap, true, false);
        return new(width, width, true, true);
    }
}

internal static class TrainUiLayout
{
    internal const int MinimumStoredRowHeight = 14;
    internal const int MaximumStoredRowHeight = 48;

    internal static float RowHeight(int configured, float scale, float buttonHeight, bool satellite)
    {
        var padding = Math.Clamp(configured, MinimumStoredRowHeight, MaximumStoredRowHeight) - MinimumStoredRowHeight;
        return Math.Max(buttonHeight, (satellite ? 26 : 31) * scale) + padding * scale;
    }
}
