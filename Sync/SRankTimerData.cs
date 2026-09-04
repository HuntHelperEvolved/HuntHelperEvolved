using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// One timed S rank: where it lives and how long it takes to come back.
/// Hours are after the kill for the normal range and after the servers
/// return for the maintenance range. A fixed timer has Min == Max.
///
/// Sonar's SonarResources/Readers/HuntReader.cs (MIT) is the source: every
/// S from Heavensward on is 84-132 hours (50-80 after maintenance); ARR's
/// are all different and listed one by one. The sync server carries the
/// same table, and the two must agree.
/// </summary>
public sealed record SRankTimer(
    uint NameId,
    string Name,
    string Expansion,
    int ExpansionOrder,
    uint TerritoryId,
    string Zone,
    double MinHours,
    double MaxHours,
    double MaintMinHours,
    double MaintMaxHours);

public enum SRankPhase
{
    /// <summary>Nobody has recorded a kill, so nothing can be said.</summary>
    Unknown,

    /// <summary>Killed, and the window has not opened yet.</summary>
    Cooldown,

    /// <summary>Inside the window: it may be up at any moment.</summary>
    Window,

    /// <summary>Past the forced time: it must be up, or something is wrong.</summary>
    Forced,

    /// <summary>Somebody has seen it since the kill.</summary>
    Up,
}

/// <summary>What the S-rank table shows for one mark on one world.</summary>
public readonly record struct SRankCycle(
    SRankPhase Phase,
    double Percent,
    DateTime? OpensAtUtc,
    DateTime? ForcedAtUtc,
    TimeSpan? SinceKill);

public static class SRankTimerData
{
    private const double Min = 84, Max = 132, MMin = 50, MMax = 80;

    /// <summary>
    /// Only S ranks with a respawn timer. The SS-event marks and their
    /// minions are S rank in the game's data but come from an event, not a
    /// clock, so they are not here. Ids match OtherRankData; territories
    /// match SpawnPointData.
    /// </summary>
    public static readonly IReadOnlyList<SRankTimer> All = new List<SRankTimer>
    {
        new(2953, "Laideronnette", "ARR", 0, 148, "Central Shroud", 42, 48, 25, 29),
        new(2954, "Wulgaru", "ARR", 0, 152, "East Shroud", 67, 78, 39, 47),
        new(2955, "Mindflayer", "ARR", 0, 153, "South Shroud", 50, 50, 30, 30),
        new(2956, "Thousand-cast Theda", "ARR", 0, 154, "North Shroud", 57, 63, 34, 38),
        new(2957, "Zona Seeker", "ARR", 0, 140, "Western Thanalan", 57, 63, 34, 38),
        new(2958, "Brontes", "ARR", 0, 141, "Central Thanalan", 66, 78, 39, 47),
        new(2959, "Lampalagua", "ARR", 0, 145, "Eastern Thanalan", 66, 78, 39, 47),
        new(2960, "Nunyunuwi", "ARR", 0, 146, "Southern Thanalan", 44, 54, 26, 33),
        new(2961, "Minhocao", "ARR", 0, 147, "Northern Thanalan", 57, 63, 34, 38),
        new(2962, "Croque-mitaine", "ARR", 0, 134, "Middle La Noscea", 65, 75, 39, 45),
        new(2963, "Croakadile", "ARR", 0, 135, "Lower La Noscea", 50, 50, 30, 30),
        new(2964, "The Garlok", "ARR", 0, 137, "Eastern La Noscea", 42, 48, 21, 29),
        new(2965, "Bonnacon", "ARR", 0, 138, "Western La Noscea", 65, 75, 39, 45),
        new(2966, "Nandi", "ARR", 0, 139, "Upper La Noscea", 47, 53, 28, 32),
        new(2967, "Chernobog", "ARR", 0, 180, "Outer La Noscea", 65, 71, 39, 43),
        new(2968, "Safat", "ARR", 0, 155, "Coerthas Central Highlands", 60, 84, 36, 51),
        new(2969, "Agrippa The Mighty", "ARR", 0, 156, "Mor Dhona", 60, 84, 36, 51),

        new(4374, "Kaiser Behemoth", "Heavensward", 1, 397, "Coerthas Western Highlands", Min, Max, MMin, MMax),
        new(4375, "Senmurv", "Heavensward", 1, 398, "The Dravanian Forelands", Min, Max, MMin, MMax),
        new(4376, "The Pale Rider", "Heavensward", 1, 399, "The Dravanian Hinterlands", Min, Max, MMin, MMax),
        new(4377, "Gandarewa", "Heavensward", 1, 400, "The Churning Mists", Min, Max, MMin, MMax),
        new(4378, "Bird of Paradise", "Heavensward", 1, 401, "The Sea of Clouds", Min, Max, MMin, MMax),
        new(4380, "Leucrotta", "Heavensward", 1, 402, "Azys Lla", Min, Max, MMin, MMax),

        new(5987, "Udumbara", "Stormblood", 2, 612, "The Fringes", Min, Max, MMin, MMax),
        new(5984, "Okina", "Stormblood", 2, 613, "The Ruby Sea", Min, Max, MMin, MMax),
        new(5985, "Gamma", "Stormblood", 2, 614, "Yanxia", Min, Max, MMin, MMax),
        new(5988, "Bone Crawler", "Stormblood", 2, 620, "The Peaks", Min, Max, MMin, MMax),
        new(5989, "Salt and Light", "Stormblood", 2, 621, "The Lochs", Min, Max, MMin, MMax),
        new(5986, "Orghana", "Stormblood", 2, 622, "The Azim Steppe", Min, Max, MMin, MMax),

        new(8905, "Tyger", "Shadowbringers", 3, 813, "Lakeland", Min, Max, MMin, MMax),
        new(8910, "Forgiven Pedantry", "Shadowbringers", 3, 814, "Kholusia", Min, Max, MMin, MMax),
        new(8900, "Tarchia", "Shadowbringers", 3, 815, "Amh Araeng", Min, Max, MMin, MMax),
        new(8653, "Aglaope", "Shadowbringers", 3, 816, "Il Mheg", Min, Max, MMin, MMax),
        new(8890, "Ixtab", "Shadowbringers", 3, 817, "The Rak'tika Greatwood", Min, Max, MMin, MMax),
        new(8895, "Gunitt", "Shadowbringers", 3, 818, "The Tempest", Min, Max, MMin, MMax),

        new(10617, "Burfurlur the Canny", "Endwalker", 4, 956, "Labyrinthos", Min, Max, MMin, MMax),
        new(10618, "Sphatika", "Endwalker", 4, 957, "Thavnair", Min, Max, MMin, MMax),
        new(10619, "Armstrong", "Endwalker", 4, 958, "Garlemald", Min, Max, MMin, MMax),
        new(10620, "Ruminator", "Endwalker", 4, 959, "Mare Lamentorum", Min, Max, MMin, MMax),
        new(10622, "Narrow-rift", "Endwalker", 4, 960, "Ultima Thule", Min, Max, MMin, MMax),
        new(10621, "Ophioneus", "Endwalker", 4, 961, "Elpis", Min, Max, MMin, MMax),

        new(13360, "Kirlirger the Abhorrent", "Dawntrail", 5, 1187, "Urqopacha", Min, Max, MMin, MMax),
        new(13444, "Ihnuxokiy", "Dawntrail", 5, 1188, "Kozama'uka", Min, Max, MMin, MMax),
        new(12754, "Neyoozoteel", "Dawntrail", 5, 1189, "Yak T'el", Min, Max, MMin, MMax),
        new(13399, "Sansheya", "Dawntrail", 5, 1190, "Shaaloani", Min, Max, MMin, MMax),
        new(13156, "Atticus the Primogenitor", "Dawntrail", 5, 1191, "Heritage Found", Min, Max, MMin, MMax),
        new(13437, "The Forecaster", "Dawntrail", 5, 1192, "Living Memory", Min, Max, MMin, MMax),
    };

    public static readonly IReadOnlyDictionary<uint, SRankTimer> ByNameId = All.ToDictionary(s => s.NameId);
    public static readonly IReadOnlyDictionary<uint, SRankTimer> ByTerritory = All.ToDictionary(s => s.TerritoryId);

    public static readonly string[] Expansions =
        { "ARR", "Heavensward", "Stormblood", "Shadowbringers", "Endwalker", "Dawntrail" };

    public static bool IsTimerSRank(uint nameId) => ByNameId.ContainsKey(nameId);

    public static SRankTimer? ForTerritory(uint territoryId) =>
        ByTerritory.TryGetValue(territoryId, out var s) ? s : null;

    /// <summary>
    /// Where a mark is in its cycle. The percentage is the share of the
    /// window that has elapsed — the chance it has spawned by now if the
    /// spawn is uniform across the window, which is what the trackers show
    /// and what people mean by "it's at 40%". Nothing here is a probability
    /// in any stricter sense.
    /// </summary>
    public static SRankCycle Compute(SRankTimer timer, SyncSRankStatus? status, DateTime nowUtc, bool seenUpNow)
    {
        if (status?.KilledAt is not { } killed)
        {
            // No kill known. Seen up is still worth saying.
            return new SRankCycle(seenUpNow ? SRankPhase.Up : SRankPhase.Unknown, 0, null, null, null);
        }

        var min = status.Maintenance ? timer.MaintMinHours : timer.MinHours;
        var max = status.Maintenance ? timer.MaintMaxHours : timer.MaxHours;
        var opens = killed.AddHours(min);
        var forced = killed.AddHours(max);
        var since = nowUtc - killed;

        var up = seenUpNow
                 || (status.SpawnedAt is { } spawned && spawned > killed)
                 || (status.LastSeenUpAt is { } seen && seen > killed && nowUtc - seen < TimeSpan.FromMinutes(3));

        double percent;
        if (nowUtc < opens) percent = 0;
        else if (max <= min) percent = 100;
        else percent = Math.Clamp((nowUtc - opens).TotalHours / (max - min) * 100.0, 0, 100);

        var phase = up ? SRankPhase.Up
            : nowUtc < opens ? SRankPhase.Cooldown
            : nowUtc >= forced ? SRankPhase.Forced
            : SRankPhase.Window;

        return new SRankCycle(phase, percent, opens, forced, since);
    }
}
