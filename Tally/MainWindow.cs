using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace HuntTally.Windows;

public sealed class MainWindow : Window, IDisposable
{
    /// <summary>Kill counts for the four calendar periods, built in one pass.</summary>
    private sealed class StatsSnapshot
    {
        public readonly Dictionary<string, int> Today = new();
        public readonly Dictionary<string, int> Week = new();
        public readonly Dictionary<string, int> Month = new();
        public readonly Dictionary<string, int> Year = new();
        public int Total;
        public DateTime Oldest;
    }

    private static readonly char[] CsvSpecials = { ',', '"', '\n', '\r' };

    private readonly Configuration config;
    private readonly CharacterContext characters;

    private bool accountScope;
    private string filter = string.Empty;
    private string statusMessage = string.Empty;

    /// <summary>
    /// Which rank the marks list is narrowed to, as an index into
    /// <see cref="RankFilterLabels"/>. Zero is every rank.
    ///
    /// A view filter rather than a setting, like the name box beside it — it
    /// answers "what am I looking at right now", not "how should this plugin
    /// behave", so it is not persisted.
    /// </summary>
    private int rankFilter;

    private static readonly string[] RankFilterLabels = { "All ranks", "B", "A", "S" };

    /// <summary>Index-aligned with <see cref="RankFilterLabels"/>; null is every rank.</summary>
    private static readonly MarkRank?[] RankFilterValues =
        { null, MarkRank.B, MarkRank.A, MarkRank.S };

    // No SS. An SS kill lands in the tally as an S rank, so the option matched
    // nothing and only offered an empty table.

    // Derived views are rebuilt when the data revision, the scope or the filter
    // moves - not on every frame. The statistics tab in particular used to copy
    // the whole kill history and walk it five times per frame, which at the
    // default 5000-entry cap across several characters is tens of thousands of
    // iterations per redraw.
    private List<MarkRecord> marksRows = new();
    private int marksRevision = -1;
    private ulong marksScopeKey;
    // A sentinel no real filter can equal, so the first pass always builds.
    // Written as an escape on purpose: as a raw NUL byte it made the whole
    // file read as binary, and grep skips those silently.
    private string marksFilter = "\0";
    private int marksRankFilter = -1;

    private StatsSnapshot? stats;
    private int statsRevision = -1;
    private ulong statsScopeKey;
    private DateTime statsDay;

    public MainWindow(Configuration config, CharacterContext characters)
        : base("Hunt Tally###HuntTallyMain")
    {
        this.config = config;
        this.characters = characters;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 360),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public void Dispose() { }

    /// <summary>Null means account-wide aggregation.</summary>
    private CharacterProfile? Scope => accountScope ? null : characters.Current;

    /// <summary>Character scope is selected but nobody is logged in.</summary>
    private bool ScopeUnavailable => !accountScope && characters.Current is null;

    private ulong ScopeKey(CharacterProfile? scope) =>
        accountScope ? 0UL : scope?.ContentId ?? ulong.MaxValue;

    public override void Draw()
    {
        DrawScopeSelector();
        DrawSummary();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##tabs"))
        {
            if (ImGui.BeginTabItem("Marks"))
            {
                DrawMarksTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("By expansion"))
            {
                DrawExpansionTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Statistics"))
            {
                DrawStatisticsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Characters"))
            {
                DrawCharactersTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawScopeSelector()
    {
        var current = characters.Current;
        var characterLabel = current?.Display ?? "Not logged in";

        if (ImGui.RadioButton(characterLabel, !accountScope))
            accountScope = false;
        ImGui.SameLine();
        if (ImGui.RadioButton($"All characters ({config.Characters.Count})", accountScope))
            accountScope = true;

        if (current is null && !accountScope)
            ImGui.TextDisabled("Log in to see this character's tally.");
    }

    private void DrawSummary()
    {
        if (ScopeUnavailable)
        {
            ImGui.Text("Lifetime marks killed: -");
            return;
        }

        var scope = Scope;
        var total = scope?.GrandTotal() ?? config.AccountGrandTotal();
        var counted = Categories.Overall.Sum(k => config.CountedFor(k, scope));
        var seeded = Categories.Overall.Sum(k => config.BaselineFor(k, scope));

        ImGui.Text($"Lifetime marks killed: {total}");
        if (seeded > 0)
            ImGui.TextDisabled($"({counted} counted here, {seeded} seeded from achievements)");

        ImGui.Text(
            $"S: {config.TotalFor(Categories.S, scope)}    " +
            $"A: {config.TotalFor(Categories.A, scope)}    " +
            $"B: {config.TotalFor(Categories.B, scope)}");

        if (accountScope)
            ImGui.TextDisabled("Covers characters this installation has seen.");
    }

    private void DrawCharactersTab()
    {
        if (config.Characters.Count == 0)
        {
            ImGui.TextDisabled("No characters tracked yet.");
            return;
        }

        var currentId = characters.Current?.ContentId ?? 0;

        if (!ImGui.BeginTable("##chars", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("S", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Last played", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableHeadersRow();

        foreach (var profile in config.Characters.Values.OrderByDescending(p => p.GrandTotal()))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (profile.ContentId == currentId)
                ImGui.TextUnformatted($"{profile.Display} *");
            else
                ImGui.TextUnformatted(profile.Display);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(profile.TotalFor(Categories.S)));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(profile.TotalFor(Categories.A)));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(profile.TotalFor(Categories.B)));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(profile.GrandTotal()));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(profile.LastSeen == default
                ? "-"
                : profile.LastSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        ImGui.EndTable();

        ImGui.Spacing();
        ImGui.TextDisabled("* currently logged in");
    }

    private void DrawExpansionTab()
    {
        if (ScopeUnavailable)
        {
            ImGui.TextDisabled("Log in, or switch to all characters.");
            return;
        }

        var scope = Scope;

        ImGui.TextDisabled("Subsets of the totals above, not extra kills.");
        ImGui.Spacing();

        if (!ImGui.BeginTable("##byexp", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Expansion");
        ImGui.TableSetupColumn("A ranks");
        ImGui.TableSetupColumn("S ranks");
        ImGui.TableSetupColumn("Total");
        ImGui.TableHeadersRow();

        foreach (var expansion in Categories.Expansions)
        {
            var a = config.TotalFor($"{expansion}.A", scope);
            var s = config.TotalFor($"{expansion}.S", scope);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(expansion);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(a));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(s));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(a + s));
        }

        ImGui.EndTable();
    }

    private void DrawMarksTab()
    {
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##filter", "Filter by name...", ref filter, 64);

        // The list is already ordered by kills, so picking a rank here puts
        // the most-killed mark of that rank at the top — which is the whole
        // point of having it.
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.Combo("##rankfilter", ref rankFilter, RankFilterLabels, RankFilterLabels.Length);

        ImGui.SameLine();
        if (ImGui.Button("Export CSV"))
            ExportCsv();

        if (!string.IsNullOrEmpty(statusMessage))
            ImGui.TextDisabled(statusMessage);

        if (ScopeUnavailable)
        {
            ImGui.TextDisabled("Log in, or switch to all characters.");
            return;
        }

        ImGui.TextDisabled("Only marks this plugin has seen. Seeded kills have no per-mark detail.");
        ImGui.TextDisabled("To correct a total, edit the baseline for its rank in settings.");
        ImGui.Spacing();

        EnsureMarksRows();

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders
                                      | ImGuiTableFlags.RowBg
                                      | ImGuiTableFlags.ScrollY
                                      | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##tally", 4, flags))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Mark", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Kills", ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Last killed", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableHeadersRow();

        foreach (var record in marksRows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(record.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(MarkData.RankLabel(record.Rank));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Num(record.Count));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(record.LastKill == default
                ? "-"
                : record.LastKill.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        }

        ImGui.EndTable();
    }

    private void EnsureMarksRows()
    {
        var scope = Scope;
        var scopeKey = ScopeKey(scope);

        if (marksRevision == config.Revision && marksScopeKey == scopeKey
            && marksFilter == filter && marksRankFilter == rankFilter)
            return;

        marksRevision = config.Revision;
        marksScopeKey = scopeKey;
        marksFilter = filter;
        marksRankFilter = rankFilter;

        var source = scope is null ? config.AggregateRecords() : scope.Records.Values;

        var rank = RankFilterValues[Math.Clamp(rankFilter, 0, RankFilterValues.Length - 1)];

        marksRows = source
            .Where(r => r.Count > 0)
            .Where(r => rank is null || r.Rank == rank)
            .Where(r => filter.Length == 0 ||
                        r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name)
            .ToList();
    }

    /// <summary>
    /// Period counts are derived from the timestamped kill history, which only
    /// covers kills this plugin observed. Achievement-seeded totals have no
    /// dates attached and cannot be broken down by period, so these numbers are
    /// always lower than the lifetime totals above.
    /// </summary>
    private void DrawStatisticsTab()
    {
        if (ScopeUnavailable)
        {
            ImGui.TextDisabled("Log in, or switch to all characters.");
            return;
        }

        var snapshot = EnsureStats();

        if (snapshot.Total == 0)
        {
            ImGui.TextDisabled("No kills recorded yet.");
            return;
        }

        ImGui.TextDisabled("Counted kills only. Seeded totals have no dates and are excluded.");
        ImGui.TextDisabled(
            $"History goes back to {snapshot.Oldest:yyyy-MM-dd} ({snapshot.Total} kills, "
            + $"capped at {config.HistoryLimit} per character).");
        ImGui.Spacing();

        if (!ImGui.BeginTable("##stats", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Today", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("This week", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("This month", ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("This year", ImGuiTableColumnFlags.WidthFixed, 65);
        ImGui.TableHeadersRow();

        var periods = new[] { snapshot.Today, snapshot.Week, snapshot.Month, snapshot.Year };

        foreach (var key in StatisticsKeys())
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Categories.Label(key));

            foreach (var period in periods)
            {
                ImGui.TableNextColumn();
                var value = period.GetValueOrDefault(key);
                if (value == 0)
                    ImGui.TextDisabled("0");
                else
                    ImGui.TextUnformatted(Num(value));
            }
        }

        ImGui.EndTable();
    }

    private StatsSnapshot EnsureStats()
    {
        var scope = Scope;
        var scopeKey = ScopeKey(scope);
        var now = DateTime.Now;
        var today = now.Date;

        if (stats is not null
            && statsRevision == config.Revision
            && statsScopeKey == scopeKey
            && statsDay == today)
        {
            return stats;
        }

        statsRevision = config.Revision;
        statsScopeKey = scopeKey;
        statsDay = today;

        var histories = scope is null
            ? config.Characters.Values.SelectMany(p => p.History)
            : scope.History;

        stats = Compute(histories, now);
        return stats;
    }

    /// <summary>
    /// Counts entries into every period in a single pass. Each kill contributes
    /// to its rank and, for A and S ranks in a tracked expansion, to that
    /// expansion's subset as well - matching how the lifetime counters are
    /// built.
    ///
    /// The periods are not nested: a Monday-based week can start in the
    /// previous month or year, so each entry is tested against all four cutoffs
    /// rather than short-circuiting.
    /// </summary>
    private static StatsSnapshot Compute(IEnumerable<KillEntry> entries, DateTime now)
    {
        var snapshot = new StatsSnapshot();

        var day = now.Date;
        var week = StartOfWeek(day);
        var month = new DateTime(now.Year, now.Month, 1);
        var year = new DateTime(now.Year, 1, 1);

        foreach (var entry in entries)
        {
            snapshot.Total++;
            if (snapshot.Oldest == default || entry.Time < snapshot.Oldest)
                snapshot.Oldest = entry.Time;

            var rankKey = Configuration.CategoryKeyFor(entry.Rank);
            if (rankKey is null)
                continue;

            Accumulate(snapshot, rankKey, entry.Time, day, week, month, year);

            if (rankKey == Categories.B || string.IsNullOrEmpty(entry.Expansion))
                continue;
            if (Array.IndexOf(Categories.Expansions, entry.Expansion) < 0)
                continue;

            Accumulate(snapshot, $"{entry.Expansion}.{rankKey}", entry.Time, day, week, month, year);
        }

        return snapshot;
    }

    private static void Accumulate(
        StatsSnapshot snapshot, string key, DateTime time,
        DateTime day, DateTime week, DateTime month, DateTime year)
    {
        if (time >= day)
            snapshot.Today[key] = snapshot.Today.GetValueOrDefault(key) + 1;
        if (time >= week)
            snapshot.Week[key] = snapshot.Week.GetValueOrDefault(key) + 1;
        if (time >= month)
            snapshot.Month[key] = snapshot.Month.GetValueOrDefault(key) + 1;
        if (time >= year)
            snapshot.Year[key] = snapshot.Year.GetValueOrDefault(key) + 1;
    }

    private void ExportCsv()
    {
        try
        {
            var dir = Service.Interface.GetPluginConfigDirectory();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"hunttally-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            var sb = new StringBuilder();

            sb.AppendLine("Character,Category,Counted,Seeded,Total");
            foreach (var profile in config.Characters.Values.OrderBy(p => p.Name))
            {
                foreach (var key in AllKeys())
                {
                    sb.AppendLine(string.Join(',',
                        Escape(profile.Display),
                        Escape(key),
                        Num(profile.CountedFor(key)),
                        Num(profile.BaselineFor(key)),
                        Num(profile.TotalFor(key))));
                }
            }

            sb.AppendLine();
            sb.AppendLine("Character,Name,Rank,Kills,FirstKill,LastKill");
            foreach (var profile in config.Characters.Values.OrderBy(p => p.Name))
            {
                foreach (var r in profile.Records.Values.Where(r => r.Count > 0).OrderBy(r => r.Name))
                {
                    sb.AppendLine(string.Join(',',
                        Escape(profile.Display),
                        Escape(r.Name),
                        MarkData.RankLabel(r.Rank),
                        Num(r.Count),
                        r.FirstKill == default ? "" : r.FirstKill.ToString("s", CultureInfo.InvariantCulture),
                        r.LastKill == default ? "" : r.LastKill.ToString("s", CultureInfo.InvariantCulture)));
                }
            }

            File.WriteAllText(path, sb.ToString());
            statusMessage = $"Saved to {path}";
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "CSV export failed.");
            statusMessage = "Export failed, see the Dalamud log.";
        }
    }

    private static IEnumerable<string> StatisticsKeys()
    {
        yield return Categories.B;
        yield return Categories.A;
        yield return Categories.S;
        foreach (var expansion in Categories.Expansions)
        {
            yield return $"{expansion}.A";
            yield return $"{expansion}.S";
        }
    }

    /// <summary>
    /// Monday-based calendar week. Note this is not the Tuesday server reset:
    /// these are calendar periods, not game-week periods.
    /// </summary>
    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static IEnumerable<string> AllKeys()
    {
        foreach (var key in Categories.Overall)
            yield return key;
        foreach (var expansion in Categories.Expansions)
        {
            yield return $"{expansion}.A";
            yield return $"{expansion}.S";
        }
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// RFC 4180 quoting. The previous version only quoted on a comma, so a
    /// value containing a quote or a newline produced a broken file.
    /// </summary>
    private static string Escape(string value) =>
        value.IndexOfAny(CsvSpecials) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
}
