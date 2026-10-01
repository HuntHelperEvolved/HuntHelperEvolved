using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

/// <summary>The same scope editor with independent state for display and relay preferences.</summary>
internal sealed class MarkScopeRuleEditor(WorldData worlds, Func<uint> currentWorld, Action save,
    Func<string?> loadedPresetId, Action<string?> setLoadedPresetId, bool relayOnly = false)
{
    private int _selectedFilterRule;
    private string _filterTargetSearch = string.Empty;
    private string? _selectedPresetId;
    private string? _renamePresetId;
    private string? _deletePresetId;
    private string _deletePresetName = string.Empty;
    private bool _namingPreset;
    private string _presetName = string.Empty;
    private string _presetError = string.Empty;
    private string _presetMessage = string.Empty;
    private static readonly string[] FilterRanks = { "S", "SS", "A", "B" };
    private static readonly (string Name, string Short)[] FilterExpansions =
    {
        ("ARR", "ARR"), ("Heavensward", "HW"), ("Stormblood", "SB"),
        ("Shadowbringers", "ShB"), ("Endwalker", "EW"), ("Dawntrail", "DT"), ("Unknown", "Other"),
    };

    private IReadOnlyList<string> EditableRanks => relayOnly ? RelayRanks : FilterRanks;
    private static readonly string[] RelayRanks = { "S" };

    public void Draw(List<VisibleMarkRule> rules, List<MarkScopePreset> presets)
    {
        rules.RemoveAll(rule => rule is null);
        foreach (var rule in rules) VisibleMarkFilter.Normalize(rule);
        ImGui.PushID(relayOnly ? "relayScopeRules" : "visibleScopeRules");
        DrawPresets(rules, presets);
        ImGui.Separator();
        DrawRuleEditor(rules);
        ImGui.PopID();
    }

    private void DrawPresets(List<VisibleMarkRule> rules, List<MarkScopePreset> presets)
    {
        var loaded = presets.FirstOrDefault(p => p is not null && p.Id == loadedPresetId());
        ImGui.TextWrapped(loaded is null ? "Current rules: Custom" : "Current rules: " + loaded.Name
            + (MarkScopePresets.SnapshotMatches(loaded, rules, relayOnly) ? "" : " (modified)"));
        if (!ImGui.CollapsingHeader("Saved presets", ImGuiTreeNodeFlags.DefaultOpen)) return;
        var selected = presets.FirstOrDefault(p => p is not null && p.Id == (_selectedPresetId ?? loadedPresetId()));
        _selectedPresetId = selected?.Id;
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##savedPreset", selected?.Name ?? "Select a preset", ImGuiComboFlags.HeightLarge))
        {
            foreach (var preset in presets.Where(p => p is not null))
            {
                ImGui.PushID(preset.Id);
                if (ImGui.Selectable(preset.Name, preset.Id == _selectedPresetId))
                { _selectedPresetId = preset.Id; selected = preset; _presetMessage = _presetError = string.Empty; }
                ImGui.PopID();
            }
            ImGui.EndCombo();
        }
        ImGui.BeginDisabled(selected is null);
        if (ImGui.Button("Load") && selected is not null) LoadPreset(presets, rules, selected.Id);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Replace the current rules with this saved preset.");
        ImGui.EndDisabled();
        SameLineIfFits(HuntUi.ButtonWidth("Save as..."));
        if (ImGui.Button("Save as..."))
        {
            _namingPreset = true; _renamePresetId = null; _presetName = string.Empty;
            _deletePresetId = null; _presetMessage = _presetError = string.Empty;
        }
        SameLineIfFits(HuntUi.ButtonWidth("Update saved"));
        ImGui.BeginDisabled(selected is null);
        if (ImGui.Button("Update saved") && selected is not null) UpdatePreset(presets, rules, selected.Id);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Replace the selected preset's saved rules with your current rules.");
        SameLineIfFits(HuntUi.ButtonWidth("Rename..."));
        if (ImGui.Button("Rename...") && selected is not null)
        {
            _namingPreset = true; _renamePresetId = selected.Id; _presetName = selected.Name;
            _deletePresetId = null; _presetMessage = _presetError = string.Empty;
        }
        SameLineIfFits(HuntUi.ButtonWidth("Delete..."));
        if (ImGui.Button("Delete...") && selected is not null)
        {
            _deletePresetId = selected.Id; _deletePresetName = selected.Name;
            _namingPreset = false; _presetMessage = _presetError = string.Empty;
        }
        ImGui.EndDisabled();
        if (_namingPreset)
        {
            ImGui.TextUnformatted(_renamePresetId is null ? "Save current rules as" : "Rename preset");
            ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("##presetName", "Preset name", ref _presetName, MarkScopePresets.MaxNameLength + 1);
            if (ImGui.Button("Save##presetName") && (_renamePresetId is null
                ? CreatePreset(presets, rules, _presetName) : RenamePreset(presets, _renamePresetId, _presetName)))
                _namingPreset = false;
            SameLineIfFits(HuntUi.ButtonWidth("Cancel"));
            if (ImGui.Button("Cancel##presetName")) { _namingPreset = false; _presetError = string.Empty; }
        }
        if (_deletePresetId is not null)
        {
            ImGui.TextWrapped("Delete \"" + _deletePresetName + "\"? Your current rules will stay in place.");
            if (ImGui.Button("Delete preset"))
            {
                DeletePreset(presets, _deletePresetId, _deletePresetName);
                _deletePresetId = null;
            }
            SameLineIfFits(HuntUi.ButtonWidth("Cancel"));
            if (ImGui.Button("Cancel##deletePreset")) _deletePresetId = null;
        }
        if (_presetError.Length > 0) ImGui.TextWrapped(_presetError);
        else if (_presetMessage.Length > 0) ImGui.TextWrapped(_presetMessage);
        ImGui.TextWrapped("Rule edits apply immediately. Use Update saved to keep them in the selected preset. Presets save world, rank and expansion rules.");
    }

    private void DrawRuleEditor(List<VisibleMarkRule> rules)
    {
        if (HuntUi.Button("addRule", "Add rule", FontAwesomeIcon.Plus))
        {
            rules.Add(new() { Scope = VisibleMarkScope.CurrentWorld, Ranks = relayOnly ? new() { "S" } : new() { "S", "SS" } });
            _selectedFilterRule = rules.Count - 1;
            save();
        }
        var unrestricted = rules.Where(rule => rule.Enabled && rule.Scope == VisibleMarkScope.Any
            && rule.Worlds.Count == 0 && rule.DataCenters.Count == 0 && rule.AllExpansions
            && EditableRanks.All(rule.Ranks.Contains)).ToList();
        if (rules.Count > 1 && unrestricted.Count > 0)
        {
            ImGui.TextWrapped(relayOnly ? "An all-worlds rule still includes every S-rank relay." : "An all-worlds, all-ranks rule still includes every mark.");
            if (ImGui.SmallButton("Disable unrestricted rules"))
            { foreach (var rule in unrestricted) rule.Enabled = false; save(); }
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
                var ranks = string.Join(" / ", EditableRanks.Where(rule.Ranks.Contains));
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
        var enabled = rule.Enabled;
        if (HuntUi.WrappedCheckbox("Enable this rule", ref enabled)) { rule.Enabled = enabled; save(); }
        DrawRuleTarget(rule);
        ImGui.Spacing();
        ImGui.TextUnformatted("Ranks");
        foreach (var rank in EditableRanks)
        {
            if (rank != EditableRanks[0]) SameLineIfFits(HuntUi.ButtonWidth(rank));
            if (HuntUi.Button("rank" + rank, rank, selected: rule.Ranks.Contains(rank),
                tooltip: rank == "SS" ? "SS bosses and their event mobs" : rank + "-rank marks"))
            {
                if (!rule.Ranks.Remove(rank)) rule.Ranks.Add(rank);
                save();
            }
        }
        ImGui.Spacing();
        ImGui.TextUnformatted("Expansions");
        if (HuntUi.Button("allExpansions", "All", selected: rule.AllExpansions, tooltip: "Every expansion, including other territories"))
        { rule.AllExpansions = true; rule.Expansions.Clear(); save(); }
        SameLineIfFits(HuntUi.ButtonWidth("None"));
        if (HuntUi.Button("noExpansions", "None", selected: !rule.AllExpansions && rule.Expansions.Count == 0))
        { rule.AllExpansions = false; rule.Expansions.Clear(); save(); }
        SameLineIfFits(HuntUi.ButtonWidth("ShB+"));
        if (HuntUi.Button("shbExpansions", "ShB+", tooltip: "Shadowbringers, Endwalker and Dawntrail"))
        { rule.AllExpansions = false; rule.Expansions = new() { "Shadowbringers", "Endwalker", "Dawntrail" }; save(); }
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
                save();
            }
        }
        if (!EditableRanks.Any(rule.Ranks.Contains) || !rule.AllExpansions && rule.Expansions.Count == 0)
            ImGui.TextWrapped("This rule matches no marks. Select a rank and an expansion.");
        ImGui.Spacing();
        if (ImGui.Button("Duplicate"))
        {
            rules.Insert(_selectedFilterRule + 1, VisibleMarkFilter.Clone(rule));
            _selectedFilterRule++; save();
        }
        SameLineIfFits(HuntUi.ButtonWidth("Remove"));
        if (ImGui.Button("Remove"))
        { rules.RemoveAt(_selectedFilterRule); _selectedFilterRule = Math.Max(0, _selectedFilterRule - 1); save(); }
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
                save();
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
            var current = currentWorld();
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

    private bool CreatePreset(List<MarkScopePreset> library, List<VisibleMarkRule> rules, string name)
    {
        if (!MarkScopePresets.TryCreate(library, name, rules, relayOnly, out var preset, out _presetError)) return false;
        _selectedPresetId = preset.Id;
        setLoadedPresetId(preset.Id);
        _presetMessage = "Saved \"" + preset.Name + "\".";
        save();
        return true;
    }

    private bool LoadPreset(List<MarkScopePreset> library, List<VisibleMarkRule> rules, string id)
    {
        var preset = library.FirstOrDefault(p => p is not null && p.Id == id);
        if (preset is null) { _presetError = "Preset not found."; return false; }
        var snapshot = MarkScopePresets.Apply(preset, relayOnly);
        rules.Clear(); rules.AddRange(snapshot);
        _selectedFilterRule = 0; _filterTargetSearch = string.Empty;
        _selectedPresetId = preset.Id;
        setLoadedPresetId(preset.Id);
        _presetError = string.Empty; _presetMessage = "Loaded \"" + preset.Name + "\".";
        save();
        return true;
    }

    private bool UpdatePreset(List<MarkScopePreset> library, List<VisibleMarkRule> rules, string id)
    {
        if (!MarkScopePresets.TryUpdate(library, id, rules, relayOnly, out _presetError)) return false;
        _selectedPresetId = id;
        setLoadedPresetId(id);
        _presetMessage = "Updated the saved preset.";
        save();
        return true;
    }

    private bool RenamePreset(List<MarkScopePreset> library, string id, string name)
    {
        if (!MarkScopePresets.TryRename(library, id, name, out _presetError)) return false;
        _presetMessage = "Renamed the saved preset.";
        save();
        return true;
    }

    private bool DeletePreset(List<MarkScopePreset> library, string id, string name)
    {
        var preset = library.FirstOrDefault(p => p is not null && p.Id == id);
        if (preset is null || preset.Name != name)
        { _presetError = "The preset changed. Select it again before deleting."; return false; }
        if (!MarkScopePresets.Delete(library, id)) return false;
        if (loadedPresetId() == id) setLoadedPresetId(null);
        if (_selectedPresetId == id) _selectedPresetId = null;
        _presetError = string.Empty; _presetMessage = "Deleted \"" + name + "\". Current rules are unchanged.";
        save();
        return true;
    }

    public void Reset()
    {
        _selectedFilterRule = 0; _filterTargetSearch = string.Empty;
        _selectedPresetId = _renamePresetId = _deletePresetId = null;
        _namingPreset = false;
        _presetName = _presetError = _presetMessage = _deletePresetName = string.Empty;
    }

    private static void SameLineIfFits(float width) => HuntUi.SameLineIfFits(width);
}
