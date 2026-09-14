using System.Collections.Generic;

namespace HuntHelperEvolved;

internal static class TrainNavigation
{
    internal static DetectedMark? Next(IEnumerable<DetectedMark> marks, DetectedMark? current)
    {
        DetectedMark? next = null;
        var passedCurrent = false;
        foreach (var mark in marks)
        {
            if (ReferenceEquals(mark, current)) { passedCurrent = true; continue; }
            if (mark.Dead) continue;
            if (current is not null && (mark.Order < current.Order || (mark.Order == current.Order && !passedCurrent))) continue;
            if (next is null || mark.Order < next.Order) next = mark;
        }
        return next;
    }
}
