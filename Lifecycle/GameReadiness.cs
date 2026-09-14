using Dalamud.Game.ClientState.Conditions;

namespace HuntHelperEvolved;

internal static class GameReadiness
{
    // Test before reading game objects or native singletons. IsLoggedIn alone
    // remains true during zone changes and world/instance transfers.
    public static bool CanReadCharacters => HuntTally.Service.Framework.IsInFrameworkUpdateThread
        && HuntTally.Service.ClientState.IsLoggedIn
        && !HuntTally.Service.Condition[ConditionFlag.BetweenAreas]
        && !HuntTally.Service.Condition[ConditionFlag.BetweenAreas51];
}
