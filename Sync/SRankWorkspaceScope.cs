using System;

namespace HuntHelperEvolved.Sync;

public static class SRankWorkspaceScope
{
    public static bool IsLiveScope(uint world, uint territory, uint instance,
        uint currentWorld, uint currentTerritory, uint currentInstance) =>
        world != 0 && territory != 0 && world == currentWorld && territory == currentTerritory && instance == currentInstance;

    public static bool MatchesMark(FlagEntry watch, string mark, uint territory) =>
        watch.TerritoryId == territory && (string.Equals(watch.Label, mark, StringComparison.OrdinalIgnoreCase)
            || watch.Label.StartsWith(mark + " - ", StringComparison.OrdinalIgnoreCase)
            || watch.Label.StartsWith(mark + " \u2014 ", StringComparison.OrdinalIgnoreCase));

    public static bool MatchesWatch(FlagEntry watch, string mark, uint territory, uint world, uint instance) =>
        world != 0 && watch.WorldId == world && watch.Instance == instance && MatchesMark(watch, mark, territory);
}
