using Dalamud.Bindings.ImGui;
using System;

namespace HuntHelperEvolved.Sync;

public sealed partial class ActiveMarksWindow
{
    private MarkScopeRuleEditor? _ruleEditor;

    public void DrawSettings(Func<string, string, bool>? matches = null)
    {
        if (matches is not null)
        {
            ImGui.PushID("visibleSettings");
            if (matches("World, rank and expansion rules / saved presets", "VisibleMarkFilters ActiveMarkPresets scope current world DC data centre Shadowbringers ShB Endwalker Dawntrail save load rename delete duplicate"))
            {
                var legacy = config.VisibleMarkFilters.Rules is null;
                var rules = VisibleMarkFilter.MigrateLegacy(config.VisibleMarkFilters);
                if (legacy) config.Save();
                (_ruleEditor ??= new(worlds, detector.CurrentWorldId, config.Save,
                    () => config.ActiveMarkPresetId, id => config.ActiveMarkPresetId = id))
                    .Draw(rules, config.ActiveMarkPresets ??= new());
            }
            DrawFilterDisplayOptions(config.VisibleMarkFilters, matches);
            ImGui.PopID();
            return;
        }
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
                (_ruleEditor ??= new(worlds, detector.CurrentWorldId, config.Save,
                    () => config.ActiveMarkPresetId, id => config.ActiveMarkPresetId = id))
                    .Draw(rules, config.ActiveMarkPresets ??= new());
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

    private void DrawFilterDisplayOptions(VisibleMarkOptions options, Func<string, string, bool>? matches = null)
    {
        void FilterOption(string label, string key, bool value, Action<bool> save)
        {
            if (matches?.Invoke(label, "VisibleMarkFilters " + key) != false) Option(label, value, save);
        }
        if (matches is null) ImGui.TextUnformatted("Reports");
        FilterOption("Include community S-rank reports", "IncludeCommunity", options.IncludeCommunity, value => options.IncludeCommunity = value);
        FilterOption("Include marks seen only by me", "IncludeOwn", options.IncludeOwn, value => options.IncludeOwn = value);
        if (matches is null) { ImGui.Separator(); ImGui.TextUnformatted("Status"); }
        FilterOption("Alive", "Alive", options.Alive, value => options.Alive = value);
        FilterOption("Dead (visible corpses)", "Dead", options.Dead, value => options.Dead = value);
        FilterOption("Pulled", "Pulled combat", options.Pulled, value => options.Pulled = value);
        FilterOption("Not pulled", "NotPulled combat", options.NotPulled, value => options.NotPulled = value);
        FilterOption("Unknown combat status", "UnknownCombat", options.UnknownCombat, value => options.UnknownCombat = value);
        if (matches is null) ImGui.Separator();
        FilterOption("Show data centre beside world", "ShowDataCenter DC", options.ShowDataCenter, value => options.ShowDataCenter = value);
        if (matches is null)
        {
            if (ImGui.TreeNode("Status colours")) { DrawStatusLegend(); ImGui.TreePop(); }
            ImGui.TextWrapped("These settings apply to every rule. The rank tabs and search can narrow the list further.");
        }
        else if (matches("Status colours", "health HP stale legend")) DrawStatusLegend();
        if ((matches?.Invoke("Reset window filters", "defaults VisibleMarkFilters") ?? true) && ImGui.Button("Reset window filters"))
        { config.VisibleMarkFilters = new(); config.ActiveMarkPresetId = null; _ruleEditor?.Reset(); config.Save(); }
    }
}
