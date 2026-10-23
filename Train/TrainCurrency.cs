using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HuntHelperEvolved;

internal enum TrainCurrencyMessageMode { StartingBalance, AmountGained }

internal sealed record TrainCurrencyAmount(string Name, long Gained, int HoldingCap)
{
    public long MaximumStartingBalance => Math.Max(0, HoldingCap - Gained);
    public bool NeedsSpendingDuringTrain => Gained > HoldingCap;
}

internal sealed record TrainCurrencySummary(int MarkCount, IReadOnlyList<string> Expansions,
    IReadOnlyList<TrainCurrencyAmount> Amounts);

internal sealed record TrainCurrencyBalance(long Held, long? WeeklyRemaining = null);

internal sealed record TrainCurrencyOverflow(string Name, long Held, long Gained, int HoldingCap)
{
    public long Lost => Math.Max(0, Gained - (HoldingCap - Held));
}

internal sealed record TrainCurrencyMessage(string Plain, bool HasCurrencyPlaceholder)
{
    public string Party => Plain.Length == 0 ? string.Empty : "/p " + Plain;
    public string Shout => Plain.Length == 0 ? string.Empty : "/sh " + Plain;
    public bool CanCopyPlain => Plain.Length > 0 && HasCurrencyPlaceholder;
    public bool CanCopyParty => CanCopyPlain && Encoding.UTF8.GetByteCount(Party) <= TrainCurrency.ChatByteLimit;
    public bool CanCopyShout => CanCopyPlain && Encoding.UTF8.GetByteCount(Shout) <= TrainCurrency.ChatByteLimit;
}

/// <summary>Full-credit estimates for the A ranks still listed as alive in this train.</summary>
internal static class TrainCurrency
{
    internal const int ChatByteLimit = 500;

    // Patch 7.4 hunt tomestones, unchanged in 7.56:
    // https://na.finalfantasyxiv.com/lodestone/topics/detail/06944d892fd98cc00b2a28ff77edbafa4f7eef54/
    // ARR Allied rewards were raised to 40 in patch 2.5:
    // https://eu.finalfantasyxiv.com/lodestone/topics/detail/82d5a12408d9be6e97336eccb8d0d8aba91d373e
    // ARR Centurio rewards were raised to 20 in patch 3.1:
    // https://jp.finalfantasyxiv.com/lodestone/topics/detail/903444f2b2789767bba60554a1ad32e90623d123
    // Seal/nut rates and holding caps cross-checked with https://xiv.wobbuffet.net/hunt-tome-macro/ (7.56).
    private static readonly string[] CurrencyNames =
        { "Allied Seals", "Centurio Seals", "Nuts", "Poetics", "Mathematics", "Mnemonics" };
    private static readonly int[] HoldingCaps = { 4000, 4000, 4000, 2000, 2000, 2000 };

    internal static TrainCurrencySummary Calculate(IEnumerable<DetectedMark> marks)
    {
        var totals = new long[CurrencyNames.Length];
        var expansions = new SortedDictionary<int, (string Name, int Count)>();
        var seen = new HashSet<(uint NameId, uint Instance, uint WorldId)>();
        var count = 0;
        foreach (var mark in marks)
        {
            if (mark.IsCustom || mark.Dead || mark.SnipedAtUtc.HasValue || mark.DeathObservedAtUtc.HasValue
                || ExpansionData.Lookup(mark.NameId) is not { } info || info.Order is < 0 or > 5
                || !seen.Add(mark.Key)) continue;

            count++;
            expansions.TryGetValue(info.Order, out var expansion);
            expansions[info.Order] = (info.Expansion, expansion.Count + 1);
            if (info.Order == 0) { totals[0] += 40; totals[1] += 20; }
            else if (info.Order <= 2) totals[1] += 40;
            else totals[2] += 40;
            totals[3] += 30;
            totals[4] += info.Order == 5 ? 20 : 10;
            if (info.Order == 5) totals[5] += 10;
        }
        return new(count, expansions.Values.Select(x => $"{x.Name}: {x.Count}").ToArray(),
            CurrencyNames.Select((name, index) => new TrainCurrencyAmount(name, totals[index], HoldingCaps[index]))
                .Where(x => x.Gained > 0).ToArray());
    }

    internal static IReadOnlyList<TrainCurrencyOverflow> FindOverflows(TrainCurrencySummary summary,
        IReadOnlyDictionary<string, TrainCurrencyBalance>? balances)
    {
        if (summary.MarkCount <= 0 || balances is null) return Array.Empty<TrainCurrencyOverflow>();

        var overflows = new List<TrainCurrencyOverflow>();
        foreach (var amount in summary.Amounts)
        {
            if (amount.Gained <= 0 || amount.HoldingCap <= 0
                || !balances.TryGetValue(amount.Name, out var balance) || balance is null
                || balance.Held < 0 || balance.Held > amount.HoldingCap) continue;

            var gained = balance.WeeklyRemaining is { } remaining
                ? Math.Min(amount.Gained, Math.Max(0, remaining)) : amount.Gained;
            if (gained > amount.HoldingCap - balance.Held)
                overflows.Add(new(amount.Name, balance.Held, gained, amount.HoldingCap));
        }
        return overflows;
    }

    internal static string DefaultTemplate(TrainCurrencyMessageMode mode) => mode == TrainCurrencyMessageMode.AmountGained
        ? "$n living A ranks: up to $c at full credit."
        : "Currency check for $n living A ranks: starting balances at most $c to avoid holding caps.";

    internal static TrainCurrencyMessage CreateMessage(TrainCurrencySummary summary, TrainCurrencyMessageMode mode,
        string? template = null)
    {
        if (summary.MarkCount == 0) return new(string.Empty, true);
        template = string.IsNullOrWhiteSpace(template) ? DefaultTemplate(mode) : template;
        var hasCurrencies = template.Contains("$c", StringComparison.Ordinal);
        var amounts = string.Join(", ", summary.Amounts.Select(x =>
            mode == TrainCurrencyMessageMode.AmountGained ? $"{Number(x.Gained)} {x.Name}"
                : x.NeedsSpendingDuringTrain ? $"{x.Name}: spend during train"
                : $"{Number(x.MaximumStartingBalance)} {x.Name}"));
        var message = template.Replace("$c", amounts, StringComparison.Ordinal)
            .Replace("$n", Number(summary.MarkCount), StringComparison.Ordinal);
        return new(SingleLine(message), hasCurrencies);
    }

    internal static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string SingleLine(string value)
    {
        var result = new StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) { space = result.Length > 0; continue; }
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.Format) continue;
            if (space) result.Append(' ');
            result.Append(ch);
            space = false;
        }
        // A pasted template cannot create a second chat command or turn plain text into a command.
        return result.ToString().TrimStart('/', ' ');
    }
}
