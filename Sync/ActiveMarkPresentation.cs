using System;
using System.Collections.Generic;

namespace HuntHelperEvolved.Sync;

internal enum ActiveMarkState { Community, Unpulled, Pulled, CombatUnknown, Dead, StaleHealth }

internal static class ActiveMarkPresentation
{
    internal static ActiveMarkState State(ActiveMarkRow row) => row.HealthStale ? ActiveMarkState.StaleHealth : !row.HealthKnown ? ActiveMarkState.Community
        : row.Mark.HpPercent == 0 ? ActiveMarkState.Dead
        : row.Mark.InCombat switch { true => ActiveMarkState.Pulled, false => ActiveMarkState.Unpulled, _ => ActiveMarkState.CombatUnknown };

    internal static string Health(ActiveMarkRow row)
    {
        if (!row.HealthKnown && !row.HealthStale) return "?%";
        var hp = row.Mark.HpPercent;
        var value = hp is > 0 and < .1f ? "<0.1%" : $"{hp:0.#}%";
        if (row.HealthStale) return "~" + value;
        return value + (State(row) switch { ActiveMarkState.Pulled => " !", ActiveMarkState.CombatUnknown => " ?", _ => "" });
    }

    internal static string BearHealthEvidence(ActiveMarkRow row, DateTime now)
    {
        if (row.Bear is not { } bear) return string.Empty;
        var at = bear.Report.HealthReceivedAt ?? bear.Report.HealthObservedAt;
        if (at is null) return "Bear HP not reported.";
        var label = bear.Report.HealthReceivedAt is not null ? "HP report received" : "HP observed";
        var age = now > at.Value ? now-at.Value : TimeSpan.Zero;
        var ageText = $"{(int)age.TotalHours:00}:{age.Minutes:00}:{age.Seconds:00}";
        return $"Bear {label} {ageText} ago. " + (row.HealthStale
            ? "Stale: ~ is the last reported HP; current HP and combat are unknown."
            : bear.HealthFresh(now) ? "Fresh HP report." : "HP is unknown.");
    }

    internal static string Players(ActiveMarkRow row) => row.HealthKnown && row.Mark.NearbyPlayers is { } count && count >= 0
        ? $"[{count}]" : "[?]";

    internal static string FaloopAge(SyncSRankStatus? status,DateTime now)
    {
        var age=ActiveMarkRows.FaloopAge(status,now);
        return age.Length==0 ? "--:--" : age.StartsWith("00:",StringComparison.Ordinal) ? age[3..] : age;
    }

    internal static string Name(ActiveMarkRow row, string tab, string zone)
        => (tab == "All" || row.Mark.Rank != tab ? row.Mark.Rank + " " : string.Empty) + row.Mark.Name
            + (string.IsNullOrEmpty(zone) ? string.Empty : " / " + zone);

    internal static string FilterSummary(VisibleMarkOptions options)
    {
        var filters = new List<string>();
        if (!options.IncludeOwn || !options.IncludeCommunity) filters.Add("sources");
        if (!options.Alive || !options.Dead || !options.Pulled || !options.NotPulled || !options.UnknownCombat)
            filters.Add("status");
        if (options.Rules is { } rules)
        {
            var enabled = 0;
            VisibleMarkRule? only = null;
            foreach (var rule in rules)
                if (rule is { Enabled: true }) { enabled++; only = rule; }
            if (enabled == 0) filters.Add("no rules enabled");
            else if (enabled != 1 || !Unrestricted(only!))
                filters.Add($"{enabled} scope rule{(enabled == 1 ? string.Empty : "s")}");
            return string.Join(", ", filters);
        }
        if (options.Ranks is not { Count: 4 } ranks || !ranks.Contains("B") || !ranks.Contains("A")
            || !ranks.Contains("S") || !ranks.Contains("SS")) filters.Add("ranks");
        if (options.Worlds is { Count: > 0 }) filters.Add("worlds");
        if (options.DataCenters is { Count: > 0 }) filters.Add("data centres");
        if (options.Expansions is { Count: > 0 }) filters.Add("expansions");
        return string.Join(", ", filters);
    }

    private static bool Unrestricted(VisibleMarkRule rule) => rule.Scope == VisibleMarkScope.Any
        && (rule.Worlds is null || rule.Worlds.Count == 0) && (rule.DataCenters is null || rule.DataCenters.Count == 0)
        && rule.AllExpansions && rule.Ranks is { } ranks
        && ranks.Contains("B") && ranks.Contains("A") && ranks.Contains("S") && ranks.Contains("SS");
}

internal readonly record struct ActiveMarkRowLayout(float Width, float SourceWidth, float NameX, float NameWidth,
    float WorldX, float WorldWidth, float HealthX, float HealthWidth, float PlayersX, float PlayersWidth,
    float ActionX, float ActionWidth)
{
    internal static ActiveMarkRowLayout Create(float availableWidth, float sourceWidth, float healthWidth,
        float playersWidth, float preferredWorldWidth, float minimumWorldWidth, float minimumNameWidth,
        float actionWidth, float gap)
    {
        sourceWidth = Math.Max(0, sourceWidth);
        healthWidth = Math.Max(0, healthWidth);
        playersWidth = Math.Max(0, playersWidth);
        minimumWorldWidth = Math.Max(1, minimumWorldWidth);
        minimumNameWidth = Math.Max(1, minimumNameWidth);
        actionWidth = Math.Max(1, actionWidth);
        gap = Math.Max(0, gap);
        var fixedWidth = sourceWidth + healthWidth + playersWidth + actionWidth + gap * 5;
        // At extreme scales, scroll the line instead of hiding health, counts or the clock.
        var width = Math.Max(availableWidth, fixedWidth + minimumWorldWidth + minimumNameWidth);
        var flexibleWidth = width - fixedWidth;
        var worldWidth = Math.Clamp(Math.Min(preferredWorldWidth, flexibleWidth * .38f),
            minimumWorldWidth, Math.Max(minimumWorldWidth, flexibleWidth - minimumNameWidth));
        var nameX = sourceWidth + gap;
        var nameWidth = flexibleWidth - worldWidth;
        var worldX = nameX + nameWidth + gap;
        var healthX = worldX + worldWidth + gap;
        var playersX = healthX + healthWidth + gap;
        return new(width, sourceWidth, nameX, nameWidth, worldX, worldWidth,
            healthX, healthWidth, playersX, playersWidth, playersX + playersWidth + gap, actionWidth);
    }
}
