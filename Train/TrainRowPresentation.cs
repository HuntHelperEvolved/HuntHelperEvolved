using System;
using System.Collections.Generic;

namespace HuntHelperEvolved;

internal static class TrainRowPresentation
{
    internal static (int Recorded, int Remaining) Count(IEnumerable<DetectedMark> marks)
    {
        var recorded = 0;
        var remaining = 0;
        foreach (var mark in marks)
        {
            if (mark.IsCustom) continue;
            recorded++;
            if (!mark.Dead) remaining++;
        }
        return (recorded, remaining);
    }

    internal static string Describe(DetectedMark mark, DateTime now, string? zone, bool showAge, bool showSpicing)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(zone)) parts.Add(zone);
        if (mark.IsCustom) parts.Add(mark.Dead ? "Stop complete" : "Rally stop");
        else if (mark.Dead)
        {
            if (mark.SnipedAtUtc is { } gone) parts.Add($"Found gone {gone.ToLocalTime():HH:mm}");
            else if (mark.DeathObservedAtUtc is { } killed) parts.Add($"Killed {killed.ToLocalTime():HH:mm}");
            else parts.Add("Dead · kill time unknown");
        }
        if (!mark.Dead && mark.Spiced && showSpicing) parts.Add("Spiced");
        if (showAge && !mark.IsCustom)
        {
            if (mark.LastSeenUtc == default) parts.Add("Seen time unknown");
            else
            {
                var age = now - mark.LastSeenUtc;
                parts.Add(age.TotalMinutes < 1 ? "Seen just now"
                    : age.TotalHours < 1 ? $"Seen {(int)age.TotalMinutes}m ago"
                    : $"Seen {(int)age.TotalHours}h {age.Minutes}m ago");
            }
        }
        return string.Join(" · ", parts);
    }
}
