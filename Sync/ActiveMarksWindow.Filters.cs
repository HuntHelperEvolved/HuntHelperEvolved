using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

public sealed partial class ActiveMarksWindow
{
    private int _selectedFilterRule;
    private string _filterTargetSearch = string.Empty;
    private static readonly string[] FilterRanks = { "S", "SS", "A", "B" };
    private static readonly (string Name, string Short)[] FilterExpansions =
    {
        ("ARR", "ARR"), ("Heavensward", "HW"), ("Stormblood", "SB"),
        ("Shadowbringers", "ShB"), ("Endwalker", "EW"), ("Dawntrail", "DT"), ("Unknown", "Other"),
    };

    public void DrawSettings()
    {
        if (!ImGui.CollapsingHeader("Active Marks window filters", ImGuiTreeNodeFlags.DefaultOpen)) return;
        DrawFilterOptions();
    }

    private void DrawFilterOptions()
    {
        ImGui.PushID("visibleSettings");
        var options = config.VisibleMarkFilters;
        var legacy = options.Rules is null;
        var rules = VisibleMarkFilter.MigrateLegacy(options);
        if (legacy) config.Save();
        if (ImGui.BeginTabBar("filterSections"))
        {
            if (ImGui.BeginTabItem("Worlds & ranks"))
            {
                ImGui.TextWrapped("Show marks matching any enabled rule.");
                ImGui.PushStyleColor(ImGuiCol.Text, HuntTheme.Muted);
                ImGui.TextWrapped("The rank tabs and search narrow the list further.");
                ImGui.PopStyleColor();
                DrawRuleEditor(rules);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Status & display"))
            {
                DrawFilterDisplayOptions(options);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.PopID();
    }

    private void DrawRuleEditor(List<VisibleMarkRule> rules)
    {
        if (HuntUi.Button("addRule", "Add rule", FontAwesomeIcon.Plus))
        {
            rules.Add(new() { Scope = VisibleMarkScope.CurrentWorld, Ranks = new() { "S", "SS" } });
            _selectedFilterRule = rules.Count - 1;
            config.Save();
        }
        SameLineIfFits(HuntUi.ButtonWidth("NA hunt mix"));
        var preset = NorthAmericanHuntRules();
        ImGui.BeginDisabled(preset is null);
        if (HuntUi.Button("naHuntMix", "NA hunt mix", tooltip:
            "Replace rules: S/SS on Crystal, A on Mateus, and ShB/EW/DT S/SS on Aether, Primal and Dynamis."))
        {
            rules.Clear(); rules.AddRange(preset!);
            _selectedFilterRule = 0;
            config.Save();
        }
        ImGui.EndDisabled();
        if (preset is null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("The North American world list is not available yet.");
        var unrestricted = rules.Where(rule => rule.Enabled && rule.Scope == VisibleMarkScope.Any
            && rule.Worlds.Count == 0 && rule.DataCenters.Count == 0 && rule.AllExpansions
            && FilterRanks.All(rule.Ranks.Contains)).ToList();
        if (rules.Count > 1 && unrestricted.Count > 0)
        {
            ImGui.TextWrapped("An all-worlds, all-ranks rule still includes every mark.");
            if (ImGui.SmallButton("Disable unrestricted rules"))
            { foreach (var rule in unrestricted) rule.Enabled = false; config.Save(); }
        }
        ImGui.Spacing();
        if (rules.Count == 0)
        {
            ImGui.TextWrapped("No rules. Add a rule to choose the marks you want to see.");
            return;
        }
        _selectedFilterRule = Math.Clamp(_selectedFilterRule, 0, rules.Count - 1);
        var wide = ImGui.GetContentRegionAvail().X >= 590 * ImGuiHelpers.GlobalScale;
        if (wide && ImGui.BeginTable("ruleColumns", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Rules", ImGuiTableColumnFlags.WidthFixed, 215 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Edit rule", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextColumn();
            DrawRuleList(rules, 325 * ImGuiHelpers.GlobalScale);
            ImGui.TableNextColumn();
            DrawSelectedRule(rules);
            ImGui.EndTable();
        }
        else
        {
            DrawRuleList(rules, Math.Min(150 * ImGuiHelpers.GlobalScale,
                (ImGui.GetTextLineHeight() * 3 + ImGui.GetStyle().ItemSpacing.Y) * rules.Count + 12 * ImGuiHelpers.GlobalScale));
            ImGui.Spacing();
            DrawSelectedRule(rules);
        }
    }

    private void DrawRuleList(List<VisibleMarkRule> rules, float height)
    {
        if (ImGui.BeginChild("scopeRules", new Vector2(0, height), true))
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                ImGui.PushID(i);
                var start = ImGui.GetCursorScreenPos();
                var line = ImGui.GetTextLineHeight();
                var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
                var title = (rule.Enabled ? "" : "Off · ") + RuleTargetName(rule);
                var ranks = string.Join(" / ", FilterRanks.Where(rule.Ranks.Contains));
                if (ranks.Length == 0) ranks = "No ranks";
                var expansions = rule.AllExpansions ? "All expansions" : string.Join(" / ",
                    FilterExpansions.Where(e => rule.Expansions.Contains(e.Name)).Select(e => e.Short));
                if (expansions.Length == 0) expansions = "No expansions";
                if (ImGui.Selectable("##rule", i == _selectedFilterRule, ImGuiSelectableFlags.None, new Vector2(width, line * 3)))
                { _selectedFilterRule = i; _filterTargetSearch = string.Empty; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(title + "\n" + ranks + "\n" + expansions);
                var draw = ImGui.GetWindowDrawList();
                var colour = rule.Enabled ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : HuntTheme.Muted;
                draw.AddText(start, ImGui.GetColorU32(colour), FitFilterLabel(title, width));
                draw.AddText(start + new Vector2(0, line), ImGui.GetColorU32(HuntTheme.Accent), FitFilterLabel(ranks, width));
                draw.AddText(start + new Vector2(0, line * 2), ImGui.GetColorU32(HuntTheme.Muted), FitFilterLabel(expansions, width));
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private static string FitFilterLabel(string text, float width)
        => TrainRowPresentation.FitText(text, width, static value => ImGui.CalcTextSize(value).X);

    private void DrawSelectedRule(List<VisibleMarkRule> rules)
    {
        var rule = rules[_selectedFilterRule];
        ImGui.PushID("selectedRule");
        Option("Enable this rule", rule.Enabled, value => rule.Enabled = value);
        DrawRuleTarget(rule);
        ImGui.Spacing();
        ImGui.TextUnformatted("Ranks");
        foreach (var rank in FilterRanks)
        {
            if (rank != FilterRanks[0]) SameLineIfFits(HuntUi.ButtonWidth(rank));
            if (HuntUi.Button("rank" + rank, rank, selected: rule.Ranks.Contains(rank),
                tooltip: rank == "SS" ? "SS bosses and their event mobs" : rank + "-rank marks"))
            {
                if (!rule.Ranks.Remove(rank)) rule.Ranks.Add(rank);
                config.Save();
            }
        }
        ImGui.Spacing();
        ImGui.TextUnformatted("Expansions");
        if (HuntUi.Button("allExpansions", "All", selected: rule.AllExpansions, tooltip: "Every expansion, including other territories"))
        { rule.AllExpansions = true; rule.Expansions.Clear(); config.Save(); }
        SameLineIfFits(HuntUi.ButtonWidth("None"));
        if (HuntUi.Button("noExpansions", "None", selected: !rule.AllExpansions && rule.Expansions.Count == 0))
        { rule.AllExpansions = false; rule.Expansions.Clear(); config.Save(); }
        SameLineIfFits(HuntUi.ButtonWidth("ShB+"));
        if (HuntUi.Button("shbExpansions", "ShB+", tooltip: "Shadowbringers, Endwalker and Dawntrail"))
        { rule.AllExpansions = false; rule.Expansions = new() { "Shadowbringers", "Endwalker", "Dawntrail" }; config.Save(); }
        foreach (var expansion in FilterExpansions)
        {
            if (expansion != FilterExpansions[0]) SameLineIfFits(HuntUi.ButtonWidth(expansion.Short));
            var selected = rule.AllExpansions || rule.Expansions.Contains(expansion.Name);
            if (HuntUi.Button("expansion" + expansion.Short, expansion.Short, selected: selected,
                tooltip: expansion.Name == "Unknown" ? "Territories without a known expansion" : expansion.Name))
            {
                if (rule.AllExpansions)
                { rule.AllExpansions = false; rule.Expansions = FilterExpansions.Select(e => e.Name).ToList(); }
                if (!rule.Expansions.Remove(expansion.Name)) rule.Expansions.Add(expansion.Name);
                config.Save();
            }
        }
        if (rule.Ranks.Count == 0 || !rule.AllExpansions && rule.Expansions.Count == 0)
            ImGui.TextWrapped("This rule matches no marks. Select a rank and an expansion.");
        ImGui.Spacing();
        if (ImGui.Button("Duplicate"))
        {
            rules.Insert(_selectedFilterRule + 1, VisibleMarkFilter.Clone(rule));
            _selectedFilterRule++; config.Save();
        }
        SameLineIfFits(HuntUi.ButtonWidth("Remove"));
        if (ImGui.Button("Remove"))
        { rules.RemoveAt(_selectedFilterRule); _selectedFilterRule = Math.Max(0, _selectedFilterRule - 1); config.Save(); }
        ImGui.PopID();
    }

    private void DrawRuleTarget(VisibleMarkRule rule)
    {
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
        ImGui.TextUnformatted("Where");
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##ruleTarget", RuleTargetName(rule), ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("##targetSearch", "Find world or DC", ref _filterTargetSearch, 100);
            void Choice(string label, VisibleMarkScope scope, uint world = 0, uint dc = 0)
            {
                if (_filterTargetSearch.Length > 0 && !label.Contains(_filterTargetSearch, StringComparison.OrdinalIgnoreCase)) return;
                if (!ImGui.Selectable(label)) return;
                rule.Scope = scope;
                rule.Worlds = world == 0 ? new() : new() { world };
                rule.DataCenters = dc == 0 ? new() : new() { dc };
                _filterTargetSearch = string.Empty;
                config.Save();
            }
            Choice("All worlds", VisibleMarkScope.Any);
            Choice("Current world", VisibleMarkScope.CurrentWorld);
            Choice("Current DC", VisibleMarkScope.CurrentDataCenter);
            ImGui.Separator();
            ImGui.TextDisabled("DATA CENTRES");
            foreach (var dc in worlds.DataCenters) Choice(dc.Name, VisibleMarkScope.Any, dc: dc.Id);
            ImGui.Separator();
            ImGui.TextDisabled("WORLDS");
            foreach (var dc in worlds.DataCenters)
                foreach (var world in worlds.WorldsIn(dc.Id)) Choice(world.Name + " · " + dc.Name, VisibleMarkScope.Any, world.RowId);
            ImGui.EndCombo();
        }
        if (rule.Scope != VisibleMarkScope.Any)
        {
            var current = detector.CurrentWorldId();
            var location = worlds.LocateWorld(current);
            ImGui.TextWrapped(current == 0 ? "Waiting for your current world."
                : "Now: " + (rule.Scope == VisibleMarkScope.CurrentWorld ? worlds.NameOf(current)
                    : location is { } at ? worlds.DataCenters[at.DcIndex].Name : "DC unavailable"));
        }
        if (rule.Worlds.Count > 1 || rule.DataCenters.Count > 1 || rule.Worlds.Count > 0 && rule.DataCenters.Count > 0)
        {
            if (rule.Worlds.Count > 0) ImGui.TextWrapped("Worlds: " + string.Join(", ", rule.Worlds.Select(worlds.NameOf)));
            if (rule.DataCenters.Count > 0) ImGui.TextWrapped("DCs: " + string.Join(", ", rule.DataCenters.Select(DataCenterName)));
            ImGui.TextWrapped("Choose a target above to replace this saved selection.");
        }
    }

    private string DataCenterName(uint id) => worlds.DataCenters.FirstOrDefault(dc => dc.Id == id).Name ?? $"DC {id}";

    private string RuleTargetName(VisibleMarkRule rule)
    {
        if (rule.Scope == VisibleMarkScope.CurrentWorld) return "Current world";
        if (rule.Scope == VisibleMarkScope.CurrentDataCenter) return "Current DC";
        if (rule.Scope != VisibleMarkScope.Any) return "Unknown target";
        if (rule.Worlds.Count == 1 && rule.DataCenters.Count == 0) return worlds.NameOf(rule.Worlds[0]);
        if (rule.DataCenters.Count == 1 && rule.Worlds.Count == 0) return DataCenterName(rule.DataCenters[0]);
        if (rule.Worlds.Count == 0 && rule.DataCenters.Count == 0) return "All worlds";
        return "Custom saved selection";
    }

    private List<VisibleMarkRule>? NorthAmericanHuntRules()
    {
        var mateus = worlds.IdOf("Mateus");
        var names = new[] { "Crystal", "Aether", "Primal", "Dynamis" };
        var dcs = names.Select(name => worlds.DataCenters.FirstOrDefault(dc => dc.Name == name).Id).ToArray();
        if (mateus == 0 || dcs.Any(id => id == 0)) return null;
        var result = new List<VisibleMarkRule>
        {
            new() { DataCenters = new() { dcs[0] }, Ranks = new() { "S", "SS" } },
            new() { Worlds = new() { mateus }, Ranks = new() { "A" } },
        };
        foreach (var dc in dcs.Skip(1)) result.Add(new()
        {
            DataCenters = new() { dc }, Ranks = new() { "S", "SS" }, AllExpansions = false,
            Expansions = new() { "Shadowbringers", "Endwalker", "Dawntrail" },
        });
        return result;
    }

    private void DrawFilterDisplayOptions(VisibleMarkOptions options)
    {
        ImGui.TextUnformatted("Reports");
        Option("Include community S-rank reports", options.IncludeCommunity, value => options.IncludeCommunity = value);
        Option("Include marks seen only by me", options.IncludeOwn, value => options.IncludeOwn = value);
        ImGui.Separator();
        ImGui.TextUnformatted("Status");
        Option("Alive", options.Alive, value => options.Alive = value);
        Option("Dead (visible corpses)", options.Dead, value => options.Dead = value);
        Option("Pulled", options.Pulled, value => options.Pulled = value);
        Option("Not pulled", options.NotPulled, value => options.NotPulled = value);
        Option("Unknown combat status", options.UnknownCombat, value => options.UnknownCombat = value);
        ImGui.Separator();
        Option("Show data centre beside world", options.ShowDataCenter, value => options.ShowDataCenter = value);
        if (ImGui.TreeNode("Status colours"))
        { DrawStatusLegend(); ImGui.TreePop(); }
        ImGui.TextWrapped("These settings apply to every rule. The rank tabs and search can narrow the list further.");
        if (ImGui.Button("Reset window filters"))
        { config.VisibleMarkFilters = new(); _selectedFilterRule = 0; config.Save(); }
    }
}
