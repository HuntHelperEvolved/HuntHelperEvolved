using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using HuntTheme = HuntHelperEvolved.HuntTheme;
using HuntUi = HuntHelperEvolved.HuntUi;

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

    private readonly Configuration config;
    private readonly CharacterContext characters;

    private bool accountScope;
    private string filter = string.Empty;
    private string statusMessage = string.Empty;
    private bool exportFailed;
    private MainWindow? embeddedView;
    private int selectedTab;

    private const ImGuiTableFlags SummaryTableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
        | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable;

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

    public void Dispose() => embeddedView?.Dispose();

    /// <summary>Null means account-wide aggregation.</summary>
    private CharacterProfile? Scope => accountScope ? null : characters.Current;

    /// <summary>Character scope is selected but nobody is logged in.</summary>
    private bool ScopeUnavailable => !accountScope && characters.Current is null;

    private ulong ScopeKey(CharacterProfile? scope) =>
        accountScope ? 0UL : scope?.ContentId ?? ulong.MaxValue;

    public override void Draw() => DrawCore();

    // A second renderer shares records, not transient filters or cached views.
    public void DrawContents() => (embeddedView ??= new MainWindow(config, characters)).DrawCore();

    private void DrawCore()
    {
        DrawScopeSelector();
        DrawSummary();
        ImGui.Separator();

        var tabs = new[] { "Marks", "By expansion", "Statistics", "Characters" };
        for (var index = 0; index < tabs.Length; index++)
        {
            if (index > 0) HuntUi.SameLineIfFits(HuntUi.ButtonWidth(tabs[index]) + 8 * ImGuiHelpers.GlobalScale);
            if (HuntUi.UnderlineTab(tabs[index], selectedTab == index)) selectedTab = index;
        }
        ImGui.Separator();
        switch (selectedTab)
        {
            case 0: DrawMarksTab(); break;
            case 1: DrawExpansionTab(); break;
            case 2: DrawStatisticsTab(); break;
            case 3: DrawCharactersTab(); break;
        }
    }

    private void DrawScopeSelector()
    {
        var current = characters.Current;
        var characterLabel = current?.Display ?? "Not logged in";

        if (ImGui.RadioButton(characterLabel, !accountScope))
            accountScope = false;
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize($"All characters ({config.Characters.Count})").X
            + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) ImGui.NewLine();
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

        if (!ImGui.BeginTable("##chars", 6, SummaryTableFlags))
            return;

        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 4);
        ImGui.TableSetupColumn("S", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Last played", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableHeadersRow();

        foreach (var profile in config.Characters.Values.OrderByDescending(p => p.GrandTotal()))
        {
            ImGui.TableNextRow();

            if (ImGui.TableNextColumn())
                TextWithFullTooltip(profile.Display + (profile.ContentId == currentId ? " *" : ""));

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

        if (!ImGui.BeginTable("##byexp", 4, SummaryTableFlags))
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
            if (ImGui.TableNextColumn()) TextWithFullTooltip(expansion);
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
        ImGui.SetNextItemWidth(Math.Min(200, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##filter", "Filter by name...", ref filter, 64);

        // The list is already ordered by kills, so picking a rank here puts
        // the most-killed mark of that rank at the top — which is the whole
        // point of having it.
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < 110) ImGui.NewLine();
        ImGui.SetNextItemWidth(110);
        ImGui.Combo("##rankfilter", ref rankFilter, RankFilterLabels, RankFilterLabels.Length);

        HuntUi.SameLineIfFits(HuntUi.ButtonWidth("Export filtered CSV", FontAwesomeIcon.Download));
        ImGui.BeginDisabled(ScopeUnavailable);
        if (HuntUi.Button("exportTally", "Export filtered CSV", FontAwesomeIcon.Download,
            tooltip: ScopeUnavailable ? "Log in, or switch to all characters."
                : "Export the current character scope, rank and name filter. Achievement baselines have no per-mark detail and are excluded."))
            ExportCsv();
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(exportFailed ? HuntTheme.Danger : HuntTheme.Success, statusMessage);
            ImGui.PopTextWrapPos();
        }

        if (ScopeUnavailable)
        {
            ImGui.TextDisabled("Log in, or switch to all characters.");
            return;
        }

        ImGui.TextDisabled("Only marks this plugin has seen. Seeded kills have no per-mark detail.");
        ImGui.TextDisabled("To correct a total, edit the baseline for its rank in settings.");
        ImGui.Spacing();

        EnsureMarksRows();

        const ImGuiTableFlags flags = SummaryTableFlags | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##tally", 4, flags))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Mark", ImGuiTableColumnFlags.WidthStretch, 4);
        ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Kills", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Last killed", ImGuiTableColumnFlags.WidthStretch, 3);
        ImGui.TableHeadersRow();

        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(marksRows.Count);
            while (clipper.Step())
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var record = marksRows[i];
                    ImGui.TableNextRow();

                    if (ImGui.TableNextColumn()) TextWithFullTooltip(record.Name);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(MarkData.RankLabel(record.Rank));

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Num(record.Count));

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(record.LastKill == default
                        ? "-"
                        : record.LastKill.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                }
        }
        finally { clipper.Destroy(); }

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

        var rank = RankFilterValues[Math.Clamp(rankFilter, 0, RankFilterValues.Length - 1)];
        marksRows = TallyMarkExport.SelectRows(config, scope, accountScope, rank, filter);
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

        if (!ImGui.BeginTable("##stats", 5, SummaryTableFlags))
            return;

        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch, 3);
        ImGui.TableSetupColumn("Today", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("This week", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("This month", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("This year", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableHeadersRow();

        var periods = new[] { snapshot.Today, snapshot.Week, snapshot.Month, snapshot.Year };

        foreach (var key in StatisticsKeys())
        {
            ImGui.TableNextRow();

            if (ImGui.TableNextColumn()) TextWithFullTooltip(Categories.Label(key));

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
        if (ScopeUnavailable) return;
        try
        {
            var dir = Service.Interface.GetPluginConfigDirectory();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"hunttally-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            EnsureMarksRows();
            var csv = TallyMarkExport.BuildCsv(accountScope ? "All characters" : characters.Current!.Display, marksRows);
            File.WriteAllText(path, csv);
            exportFailed = false;
            statusMessage = $"Exported {marksRows.Count} filtered marks to {path}";
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "CSV export failed.");
            exportFailed = true;
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

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void TextWithFullTooltip(string text)
    {
        ImGui.TextUnformatted(text);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }
}
