using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

/// <summary>
/// Mark name, expansion, home zone, respawn window (hours after death), the
/// expansion's display order (ARR=0 ... Dawntrail=5), and that zone's position
/// within its expansion's usual MSQ progression order (used to sort marks the
/// way most hunt trains actually run zones, and to name marks that weren't
/// actually encountered this scout).
/// </summary>
public record MarkInfo(string Name, string Expansion, string Location, double MinHours, double MaxHours, int Order, int ZoneOrder);

/// <summary>
/// A-rank hunt mark ModelID -> full info. ModelIDs come from Hunt Helper's own
/// bundled Data/*-A.json files (img02/HuntHelper); zone names and progression
/// order cross-checked against a current community hunt reference and MSQ
/// zone-unlock guides. Update this if a new expansion's A-ranks are added.
/// </summary>
public static class ExpansionData
{
    // ARR A-ranks: window opens ~3h after death, guaranteed ("capped") by ~4h.
    private const double ArrMin = 3, ArrMax = 4;
    // HW through Dawntrail A-ranks: window opens ~4h after death, capped by ~6h.
    private const double PostArrMin = 4, PostArrMax = 6;

    public static readonly Dictionary<uint, MarkInfo> ModelIdToMark = new()
    {
        // --- ARR --- (La Noscea -> Thanalan -> Shroud -> Coerthas/Mor Dhona)
        [2945] = new("Vogaal Ja", "ARR", "Middle La Noscea", ArrMin, ArrMax, 0, 0),
        [2946] = new("Unktehi", "ARR", "Lower La Noscea", ArrMin, ArrMax, 0, 1),
        [2947] = new("Hellsclaw", "ARR", "Eastern La Noscea", ArrMin, ArrMax, 0, 2),
        [2948] = new("Nahn", "ARR", "Western La Noscea", ArrMin, ArrMax, 0, 3),
        [2949] = new("Marberry", "ARR", "Upper La Noscea", ArrMin, ArrMax, 0, 4),
        [2950] = new("Cornu", "ARR", "Outer La Noscea", ArrMin, ArrMax, 0, 5),
        [2941] = new("Sabotender Bailarina", "ARR", "Central Thanalan", ArrMin, ArrMax, 0, 6),
        [2942] = new("Maahes", "ARR", "Eastern Thanalan", ArrMin, ArrMax, 0, 7),
        [2940] = new("Alectryon", "ARR", "Western Thanalan", ArrMin, ArrMax, 0, 8),
        [2943] = new("Zanig'oh", "ARR", "Southern Thanalan", ArrMin, ArrMax, 0, 9),
        [2944] = new("Dalvag's Final Flame", "ARR", "Northern Thanalan", ArrMin, ArrMax, 0, 10),
        [2936] = new("Forneus", "ARR", "Central Shroud", ArrMin, ArrMax, 0, 11),
        [2937] = new("Melt", "ARR", "East Shroud", ArrMin, ArrMax, 0, 12),
        [2938] = new("Ghede Ti Malice", "ARR", "South Shroud", ArrMin, ArrMax, 0, 13),
        [2939] = new("Girtab", "ARR", "North Shroud", ArrMin, ArrMax, 0, 14),
        [2951] = new("Marraco", "ARR", "Coerthas Central Highlands", ArrMin, ArrMax, 0, 15),
        [2952] = new("Kurrea", "ARR", "Mor Dhona", ArrMin, ArrMax, 0, 16),

        // --- Heavensward --- (Coerthas -> Forelands -> Churning Mists -> Sea of Clouds -> Hinterlands -> Azys Lla)
        [4362] = new("Mirka", "Heavensward", "Coerthas Western Highlands", PostArrMin, PostArrMax, 1, 0),
        [4363] = new("Lyuba", "Heavensward", "Coerthas Western Highlands", PostArrMin, PostArrMax, 1, 0),
        [4364] = new("Pylraster", "Heavensward", "The Dravanian Forelands", PostArrMin, PostArrMax, 1, 1),
        [4365] = new("Lord of the Wyverns", "Heavensward", "The Dravanian Forelands", PostArrMin, PostArrMax, 1, 1),
        [4369] = new("Agathos", "Heavensward", "The Churning Mists", PostArrMin, PostArrMax, 1, 2),
        [4368] = new("Bune", "Heavensward", "The Churning Mists", PostArrMin, PostArrMax, 1, 2),
        [4370] = new("Enkelados", "Heavensward", "The Sea of Clouds", PostArrMin, PostArrMax, 1, 3),
        [4371] = new("Sisiutl", "Heavensward", "The Sea of Clouds", PostArrMin, PostArrMax, 1, 3),
        [4366] = new("Slipkinx Steeljoints", "Heavensward", "The Dravanian Hinterlands", PostArrMin, PostArrMax, 1, 4),
        [4367] = new("Stolas", "Heavensward", "The Dravanian Hinterlands", PostArrMin, PostArrMax, 1, 4),
        [4372] = new("Campacti", "Heavensward", "Azys Lla", PostArrMin, PostArrMax, 1, 5),
        [4373] = new("Stench Blossom", "Heavensward", "Azys Lla", PostArrMin, PostArrMax, 1, 5),

        // --- Stormblood --- (Fringes -> Peaks -> Yanxia -> Ruby Sea -> Azim Steppe -> Lochs)
        [5990] = new("Orcus", "Stormblood", "The Fringes", PostArrMin, PostArrMax, 2, 0),
        [5991] = new("Erle", "Stormblood", "The Fringes", PostArrMin, PostArrMax, 2, 0),
        [5992] = new("Vochstein", "Stormblood", "The Peaks", PostArrMin, PostArrMax, 2, 1),
        [5993] = new("Aqrabuamelu", "Stormblood", "The Peaks", PostArrMin, PostArrMax, 2, 1),
        [5998] = new("Gajasura", "Stormblood", "Yanxia", PostArrMin, PostArrMax, 2, 2),
        [5999] = new("Angada", "Stormblood", "Yanxia", PostArrMin, PostArrMax, 2, 2),
        [5996] = new("Funa Yurei", "Stormblood", "The Ruby Sea", PostArrMin, PostArrMax, 2, 3),
        [5997] = new("Oni Yumemi", "Stormblood", "The Ruby Sea", PostArrMin, PostArrMax, 2, 3),
        [6000] = new("Girimekhala", "Stormblood", "The Azim Steppe", PostArrMin, PostArrMax, 2, 4),
        [6001] = new("Sum", "Stormblood", "The Azim Steppe", PostArrMin, PostArrMax, 2, 4),
        [5994] = new("Mahisha", "Stormblood", "The Lochs", PostArrMin, PostArrMax, 2, 5),
        [5995] = new("Luminare", "Stormblood", "The Lochs", PostArrMin, PostArrMax, 2, 5),

        // --- Shadowbringers --- (Lakeland -> Kholusia -> Amh Araeng -> Il Mheg -> Rak'tika -> Tempest)
        [8906] = new("Nuckelavee", "Shadowbringers", "Lakeland", PostArrMin, PostArrMax, 3, 0),
        [8907] = new("Nariphon", "Shadowbringers", "Lakeland", PostArrMin, PostArrMax, 3, 0),
        [8911] = new("Li'l Murderer", "Shadowbringers", "Kholusia", PostArrMin, PostArrMax, 3, 1),
        [8912] = new("Huracan", "Shadowbringers", "Kholusia", PostArrMin, PostArrMax, 3, 1),
        [8901] = new("Maliktender", "Shadowbringers", "Amh Araeng", PostArrMin, PostArrMax, 3, 2),
        [8902] = new("Sugaar", "Shadowbringers", "Amh Araeng", PostArrMin, PostArrMax, 3, 2),
        [8654] = new("The Mudman", "Shadowbringers", "Il Mheg", PostArrMin, PostArrMax, 3, 3),
        [8655] = new("O Poorest Pauldia", "Shadowbringers", "Il Mheg", PostArrMin, PostArrMax, 3, 3),
        [8891] = new("Supay", "Shadowbringers", "The Rak'tika Greatwood", PostArrMin, PostArrMax, 3, 4),
        [8892] = new("Grassman", "Shadowbringers", "The Rak'tika Greatwood", PostArrMin, PostArrMax, 3, 4),
        [8896] = new("Rusalka", "Shadowbringers", "The Tempest", PostArrMin, PostArrMax, 3, 5),
        [8897] = new("Baal", "Shadowbringers", "The Tempest", PostArrMin, PostArrMax, 3, 5),

        // --- Endwalker --- (Labyrinthos -> Thavnair -> Garlemald -> Mare Lamentorum -> Elpis -> Ultima Thule)
        [10623] = new("Storsie", "Endwalker", "Labyrinthos", PostArrMin, PostArrMax, 4, 0),
        [10624] = new("Hulder", "Endwalker", "Labyrinthos", PostArrMin, PostArrMax, 4, 0),
        [10625] = new("Yilan", "Endwalker", "Thavnair", PostArrMin, PostArrMax, 4, 1),
        [10626] = new("Sugriva", "Endwalker", "Thavnair", PostArrMin, PostArrMax, 4, 1),
        [10627] = new("Minerva", "Endwalker", "Garlemald", PostArrMin, PostArrMax, 4, 2),
        [10628] = new("Aegeiros", "Endwalker", "Garlemald", PostArrMin, PostArrMax, 4, 2),
        [10629] = new("Lunatender Queen", "Endwalker", "Mare Lamentorum", PostArrMin, PostArrMax, 4, 3),
        [10630] = new("Mousse Princess", "Endwalker", "Mare Lamentorum", PostArrMin, PostArrMax, 4, 3),
        [10632] = new("Petalodus", "Endwalker", "Elpis", PostArrMin, PostArrMax, 4, 4),
        [10631] = new("Gurangatch", "Endwalker", "Elpis", PostArrMin, PostArrMax, 4, 4),
        [10634] = new("Arch-Eta", "Endwalker", "Ultima Thule", PostArrMin, PostArrMax, 4, 5),
        [10633] = new("Fan Ail", "Endwalker", "Ultima Thule", PostArrMin, PostArrMax, 4, 5),

        // --- Dawntrail --- (Urqopacha -> Kozama'uka -> Yak T'el -> Shaaloani -> Heritage Found -> Living Memory)
        [13361] = new("queen hawk", "Dawntrail", "Urqopacha", PostArrMin, PostArrMax, 5, 0),
        [13362] = new("Nechuciho", "Dawntrail", "Urqopacha", PostArrMin, PostArrMax, 5, 0),
        [13442] = new("the Raintriller", "Dawntrail", "Kozama'uka", PostArrMin, PostArrMax, 5, 1),
        [13443] = new("Pkuucha", "Dawntrail", "Kozama'uka", PostArrMin, PostArrMax, 5, 1),
        [12753] = new("Rrax Yity'a", "Dawntrail", "Yak T'el", PostArrMin, PostArrMax, 5, 2),
        [12692] = new("Starcrier", "Dawntrail", "Yak T'el", PostArrMin, PostArrMax, 5, 2),
        [13400] = new("Yehehetoaua'pyo", "Dawntrail", "Shaaloani", PostArrMin, PostArrMax, 5, 3),
        [13401] = new("Keheniheyamewi", "Dawntrail", "Shaaloani", PostArrMin, PostArrMax, 5, 3),
        [13157] = new("heshuala", "Dawntrail", "Heritage Found", PostArrMin, PostArrMax, 5, 4),
        [13158] = new("Urna Variabilis", "Dawntrail", "Heritage Found", PostArrMin, PostArrMax, 5, 4),
        [13435] = new("Sally the Sweeper", "Dawntrail", "Living Memory", PostArrMin, PostArrMax, 5, 5),
        [13436] = new("Cat's Eye", "Dawntrail", "Living Memory", PostArrMin, PostArrMax, 5, 5),
    };

    public static MarkInfo? Lookup(uint modelId) =>
        ModelIdToMark.TryGetValue(modelId, out var mark) ? mark : null;

    /// <summary>
    /// The bucket for anything with no expansion of its own — a conductor's
    /// custom flag in a zone that holds no A-rank, or a mark this table has
    /// never heard of. Named rather than left blank so it can be sorted,
    /// dragged and labelled like any other block.
    /// </summary>
    public const string NoExpansion = "Other";

    /// <summary>
    /// Every expansion, oldest first. Built from the table itself rather than
    /// written out again, so adding an expansion's marks is the only edit a new
    /// expansion needs.
    /// </summary>
    public static readonly IReadOnlyList<string> Expansions =
        ModelIdToMark.Values
            .GroupBy(m => m.Expansion)
            .OrderBy(g => g.Min(m => m.Order))
            .Select(g => g.Key)
            .ToList();

    /// <summary>
    /// Zone name to the expansion it belongs to. The one way to place a custom
    /// flag, which has a zone but no mark id to look up.
    /// </summary>
    private static readonly Dictionary<string, string> LocationToExpansion =
        ModelIdToMark.Values
            .GroupBy(m => m.Location)
            .ToDictionary(g => g.Key, g => g.First().Expansion, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which expansion a train row belongs to: by mark id where there is one,
    /// falling back to the zone name for custom flags, and
    /// <see cref="NoExpansion"/> when neither answers.
    ///
    /// The zone fallback is why a rally point dropped in Yak T'el groups with
    /// Dawntrail rather than collecting at the bottom of the list — a flag is
    /// placed in context, and grouping that moved it away from the marks it was
    /// placed among would lose the reason it was there.
    /// </summary>
    public static string ExpansionOf(uint modelId, string zoneName)
    {
        if (Lookup(modelId) is { } info) return info.Expansion;

        return !string.IsNullOrWhiteSpace(zoneName)
               && LocationToExpansion.TryGetValue(zoneName, out var byZone)
            ? byZone
            : NoExpansion;
    }

    /// <summary>
    /// Discord-safe circled-digit instance marker (matches the style Hunt Helper's
    /// own UI uses), e.g. " ①" for instance 1. Empty for instance 0 (no instancing)
    /// or anything outside 1-9.
    /// </summary>
    public static string InstanceGlyph(uint instance)
    {
        const string circled = "①②③④⑤⑥⑦⑧⑨";
        return instance is >= 1 and <= 9 ? $" {circled[(int)instance - 1]}" : string.Empty;
    }
}
