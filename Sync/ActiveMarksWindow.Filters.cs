using Dalamud.Bindings.ImGui;

namespace HuntHelperEvolved.Sync;

public sealed partial class ActiveMarksWindow
{
    private MarkScopeRuleEditor? _ruleEditor;

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
                (_ruleEditor ??= new(worlds, detector.CurrentWorldId, config.Save)).Draw(rules);
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
        { config.VisibleMarkFilters = new(); _ruleEditor?.Reset(); config.Save(); }
    }
}
