using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HuntTally;

internal static class TallyMarkExport
{
    private static readonly char[] CsvSpecials = { ',', '"', '\n', '\r' };

    public static List<MarkRecord> SelectRows(Configuration config, CharacterProfile? current,
        bool allCharacters, MarkRank? rank, string nameFilter)
    {
        if (!allCharacters && current is null) return new List<MarkRecord>();
        var source = allCharacters ? config.AggregateRecords() : current!.Records.Values;
        return source.Where(record => record.Count > 0)
            .Where(record => rank is null || record.Rank == rank)
            .Where(record => nameFilter.Length == 0 || record.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.Count)
            .ThenBy(record => record.Name)
            .ToList();
    }

    public static string BuildCsv(string scope, IEnumerable<MarkRecord> rows)
    {
        var csv = new StringBuilder("Scope,Name,Rank,Kills,FirstKill,LastKill\r\n");
        foreach (var record in rows)
        {
            csv.Append(Escape(scope)).Append(',')
                .Append(Escape(record.Name)).Append(',')
                .Append(record.Rank is MarkRank.B or MarkRank.A or MarkRank.S or MarkRank.SS ? record.Rank.ToString() : "?").Append(',')
                .Append(record.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Timestamp(record.FirstKill)).Append(',')
                .Append(Timestamp(record.LastKill)).Append("\r\n");
        }
        return csv.ToString();
    }

    private static string Timestamp(DateTime value) => value == default ? "" : value.ToString("s", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value.IndexOfAny(CsvSpecials) < 0
        ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
