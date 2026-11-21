using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using HuntHelperEvolved.TrainPresets;

namespace HuntHelperEvolved;

public static class TurtleExchange
{
    private static readonly Regex Line = new(
        @"^(?<name>.+?)\s*@\s*\uE0BB?(?<zone>.+?)\s*(?<instance>[\uE0B1-\uE0B9①-⑨])?\s*\(\s*(?<x>\d+(?:\.\d+)?)\s*,\s*(?<y>\d+(?:\.\d+)?)\s*\)(?:\s+Instance\s+(?<instanceText>ONE|TWO|THREE|FOUR|FIVE|SIX|SEVEN|EIGHT|NINE|[1-9]))?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static string Normalize(string name) => name.Trim().Replace('’', '\'').ToLowerInvariant();

    public static List<DetectedMark>? Import(string text, DateTime at, out string error,
        Func<uint, string>? localMobName = null, Func<uint, string>? localZoneName = null)
    {
        error = "";
        if (text.Length > TrainExchange.MaxEncodedChars)
        { error = "Turtle export is too large."; return null; }
        var result = new Dictionary<(uint, uint), DetectedMark>();
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is 0 or > TrainExchange.MaxRows)
        { error = "Copy the marks using Turtle Scout's Export button."; return null; }
        for (var i = 0; i < lines.Length; i++)
        {
            var match = lines[i].Length <= 1024 ? Line.Match(lines[i]) : Match.Empty;
            if (!match.Success)
            { error = $"Line {i + 1} is not a Turtle Scout mark."; return null; }
            var zoneName = Normalize(match.Groups["zone"].Value);
            var zone = RouteCatalog.Zones.FirstOrDefault(z => Normalize(z.Name) == zoneName
                || localZoneName is not null && Normalize(localZoneName(z.TerritoryId)) == zoneName);
            var name = Normalize(match.Groups["name"].Value);
            var mob = zone?.Marks.FirstOrDefault(m => Normalize(m.Name) == name
                || localMobName is not null && Normalize(localMobName(m.NameId)) == name);
            if (zone is null || mob is null)
            { error = $"Line {i + 1} has an unknown A-rank or zone. Use Turtle's ordinary Export."; return null; }
            if (!float.TryParse(match.Groups["x"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(match.Groups["y"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var y)
                || x is < 1 or > 100 || y is < 1 or > 100)
            { error = $"Line {i + 1} has invalid coordinates."; return null; }
            var glyph = match.Groups["instance"].Value;
            var instance = glyph.Length == 0 ? 0u
                : (uint)(glyph[0] >= '\uE0B1' ? glyph[0] - '\uE0B1' + 1 : glyph[0] - '①' + 1);
            var suffix = match.Groups["instanceText"].Value.ToUpperInvariant();
            if (suffix.Length > 0)
            {
                var suffixInstance = uint.TryParse(suffix, out var number) ? number
                    : (uint)(Array.IndexOf(new[] { "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE" }, suffix) + 1);
                if (instance != 0 && instance != suffixInstance)
                { error = $"Line {i + 1} has conflicting instance numbers."; return null; }
                instance = suffixInstance;
            }
            var key = (mob.NameId, instance);
            if (result.TryGetValue(key, out var previous) && previous.MapPosition != new Vector2(x, y))
            { error = $"Line {i + 1} repeats a mark with different coordinates."; return null; }
            result[key] = new DetectedMark
            {
                NameId = mob.NameId, Name = mob.Name, TerritoryId = zone.TerritoryId,
                ZoneName = zone.Name, Instance = instance, MapPosition = new(x, y),
                FirstSeenUtc = at, LastSeenUtc = at,
            };
        }
        return result.Values.ToList();
    }

    public static string Export(IEnumerable<DetectedMark> marks, uint world)
    {
        var result = new StringBuilder();
        foreach (var mark in marks.Where(m => m.WorldId == world && !m.IsCustom && !m.Dead))
        {
            if (ExpansionData.Lookup(mark.NameId) is not { } info) continue;
            // Turtle's clipboard parser accepts instance glyphs 1–6.
            if (mark.Instance > 6) throw new InvalidOperationException("Turtle Scout's text import supports instances 1–6.");
            if (!float.IsFinite(mark.MapPosition.X) || !float.IsFinite(mark.MapPosition.Y)
                || mark.MapPosition.X is < 1 or > 100 || mark.MapPosition.Y is < 1 or > 100)
                throw new InvalidOperationException($"{mark.Name} has no valid location to export.");
            result.Append(info.Name).Append(" @ \uE0BB").Append(info.Location);
            if (mark.Instance > 0) result.Append((char)('\uE0B1' + mark.Instance - 1));
            result.Append(CultureInfo.InvariantCulture, $" ( {mark.MapPosition.X:0.0} , {mark.MapPosition.Y:0.0} )").AppendLine();
        }
        return result.ToString();
    }
}
