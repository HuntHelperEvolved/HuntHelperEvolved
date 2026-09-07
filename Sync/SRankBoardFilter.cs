namespace HuntHelperEvolved.Sync;
public static class SRankBoardFilter
{
    public static bool Available(SRankPhase phase) => phase is SRankPhase.Window or SRankPhase.Forced;
}
