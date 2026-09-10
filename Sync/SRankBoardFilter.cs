namespace HuntHelperEvolved.Sync;
public enum SRankNameState { NotReady, Ready, ConditionsUnmet, OpeningSoon }
public static class SRankBoardFilter
{
    public static SRankNameState NameState(SRankPhase phase, bool timed, ConditionWindow? condition, System.DateTime now)
    {
        if (!Available(phase)) return SRankNameState.NotReady;
        if (!timed) return SRankNameState.Ready;
        if (condition is not { } window) return SRankNameState.NotReady;
        return now >= window.Start && now < window.End ? SRankNameState.Ready
            : OpensSoon(window.Start, now) ? SRankNameState.OpeningSoon : SRankNameState.ConditionsUnmet;
    }
    public static bool OpensSoon(System.DateTime start, System.DateTime now) =>
        start > now && start - now < System.TimeSpan.FromMinutes(5);
    public static bool Available(SRankPhase phase) => phase is SRankPhase.Window or SRankPhase.Forced;
}
