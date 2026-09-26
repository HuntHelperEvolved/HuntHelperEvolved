using System.Collections.Generic;

namespace HuntHelperEvolved;

internal static class MissingMarkSpawnPoints
{
    // Finding all zone marks alive needs no search emphasis. Dead or sniped
    // train rows remain missing for both completeness and point eligibility.
    internal static HashSet<uint> FoundMarks(IEnumerable<DetectedMark> train,
        uint territory, uint world, uint instance, IReadOnlyCollection<uint>? zoneMarkIds,
        bool scanningActive)
    {
        var found = new HashSet<uint>();
        if (!scanningActive || territory == 0 || world == 0 || zoneMarkIds is not { Count: > 0 })
            return found;
        foreach (var nameId in zoneMarkIds)
            if (ExpansionData.Lookup(nameId) is null) return found;

        foreach (var mark in train)
        {
            if (mark.TerritoryId != territory || mark.WorldId != world || mark.Instance != instance
                || mark.IsCustom || mark.Dead || mark.SnipedAtUtc is not null
                || ExpansionData.Lookup(mark.NameId) is null) continue;
            found.Add(mark.NameId);
        }

        foreach (var nameId in zoneMarkIds)
            if (!found.Contains(nameId)) return found;
        found.Clear();
        return found;
    }

    // Unknown point assignments stay bright. A shared point remains useful
    // until every mark that can use it has been found alive in this train scope.
    internal static bool ShouldDim(SpawnPoint point, uint[]? eligibleMarks, IReadOnlySet<uint> foundMarks)
    {
        if ((point.Ranks & SpawnRanks.A) == 0 || eligibleMarks is not { Length: > 0 }) return false;
        foreach (var nameId in eligibleMarks)
            if (!foundMarks.Contains(nameId)) return false;
        return true;
    }
}
