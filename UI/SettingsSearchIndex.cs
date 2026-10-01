using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HuntHelperEvolved;

internal static class SettingsSearchIndex
{
    // A query is evaluated against one setting, never the combined labels of its category.
    public static bool MatchesSetting(string query, string page, string label, string aliases = "")
    {
        static string[] Words(string text)
        {
            text = Regex.Replace(text, "([A-Z]+)([A-Z][a-z])", "$1 $2");
            text = Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2");
            var normalized = new string(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray())
                .Replace("colour", "color").Replace("centre", "center");
            return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        var terms = Words(query);
        var words = Words(page + " " + label + " " + aliases);
        return terms.All(term => words.Any(word => term.Length == 1 ? word == term : word.StartsWith(term, StringComparison.Ordinal)));
    }

}
