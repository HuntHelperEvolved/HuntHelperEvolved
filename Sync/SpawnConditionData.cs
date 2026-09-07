using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HuntHelperEvolved.Sync;

public readonly record struct ConditionWindow(DateTime Start, DateTime End);

/// <summary>Predictable ET time, moon and weather restrictions from Faloop's public condition rules.</summary>
public static class SpawnConditionData
{
    public sealed class Rule
    {
        public string Type { get; set; } = "";
        public int[] Hours { get; set; } = Array.Empty<int>();
        public double Duration { get; set; }
        public string Phase { get; set; } = "";
        public Period[]? Periods { get; set; }
        public string[] Conditions { get; set; } = Array.Empty<string>();
        public Probability[] Probabilities { get; set; } = Array.Empty<Probability>();
        public double Offset { get; set; }
    }
    public sealed class Period { public double From { get; set; } public double To { get; set; } }
    public sealed class Probability { public uint Chance { get; set; } public string Condition { get; set; } = ""; }
    private const double EtScale = 3600d/175;
    private static readonly Dictionary<string,List<List<Rule>>> Rules = Load();
    private static Dictionary<string,List<List<Rule>>> Load()
    {
        using var stream = typeof(SpawnConditionData).Assembly.GetManifestResourceStream("HuntHelperEvolved.Sync.SpawnConditions.json")!;
        return new(JsonSerializer.Deserialize<Dictionary<string,List<List<Rule>>>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!, StringComparer.OrdinalIgnoreCase);
    }
    public static bool HasTimedCondition(string name) => Rules.ContainsKey(name);
    public static ConditionWindow? Next(string name, DateTime at)
    {
        if (!Rules.TryGetValue(name,out var alternatives)) return null;
        ConditionWindow? best=null;
        foreach (var rules in alternatives)
        {
            var cursor=at;
            for(var i=0;i<1024;i++)
            {
                var windows=rules.Select(r=>NextRule(r,cursor)).ToList();
                if(windows.Any(w=>w is null)) break;
                var start=windows.Max(w=>w!.Value.Start);var end=windows.Min(w=>w!.Value.End);
                if(start<end)
                { if(best is null || start<best.Value.Start) best=new(start,end); break; }
                // Step beyond the exclusive boundary to avoid ET/UTC floating-point roundoff revisiting it.
                cursor=end.AddMilliseconds(1);
            }
        }
        return best;
    }
    internal static ConditionWindow? NextRule(Rule rule, DateTime at)
    {
        var real=(at-DateTime.UnixEpoch).TotalSeconds;var et=real*EtScale;
        // Round ET boundaries to milliseconds so touching moon/weather windows do not create sub-tick overlaps.
        DateTime ToUtc(double seconds)=>DateTime.UnixEpoch.AddTicks((long)Math.Round(seconds/EtScale*1000)*TimeSpan.TicksPerMillisecond);
        if(rule.Type=="time")
        {
            var day=Math.Floor(et/86400)*86400-86400;
            for(var i=0;i<4;i++,day+=86400)
                foreach(var hour in rule.Hours.OrderBy(h=>h))
                { var start=day+hour*3600;var end=start+rule.Duration;if(end>et)return new(ToUtc(start),ToUtc(end)); }
        }
        else if(rule.Type=="moon")
        {
            const double cycle=32*86400;
            var origin=Math.Floor((et+43200)/cycle)*cycle-43200+(rule.Phase=="full"?16*86400:0);
            var periods=rule.Periods??new[]{new Period{From=0,To=4*86400}};
            for(var i=0;i<3;i++,origin+=cycle)
                foreach(var period in periods)
                    if(origin+period.To>et)return new(ToUtc(origin+period.From),ToUtc(origin+period.To));
        }
        else if(rule.Type=="weather")
        {
            var block=(long)Math.Floor(real/1400);
            bool Valid(long b)
            {
                var target=WeatherTarget(b*1400);uint total=0;
                foreach(var probability in rule.Probabilities)
                { total+=probability.Chance;if(target<total)return rule.Conditions.Contains(probability.Condition); }
                return false;
            }
            // Include the start of an ongoing weather run, including real-time persistence offsets.
            for(var i=0;i<5000 && Valid(block-1);i++)block--;
            for(var i=0;i<10000;i++)
            {
                if(!Valid(block)){block++;continue;}
                var start=block*1400d+rule.Offset;var endBlock=block+1;
                for(var j=0;j<5000 && Valid(endBlock);j++)endBlock++;
                var end=endBlock*1400d;
                if(start<end && end>real)return new(DateTime.UnixEpoch.AddSeconds(start),DateTime.UnixEpoch.AddSeconds(end));
                block=endBlock;
            }
        }
        return null;
    }
    internal static uint WeatherTarget(long unixSeconds)
    {
        unchecked
        {
            var bell=(uint)(unixSeconds/175);var increment=(bell+8-bell%8)%24;
            var seed=(uint)(unixSeconds/4200)*100+increment;
            var step=(seed<<11)^seed;
            return ((step>>8)^step)%100;
        }
    }
    public static string Description(string name) => Descriptions.TryGetValue(name,out var text) ? text : "Spawn condition not yet documented.";
    private static readonly Dictionary<string,string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Laideronnette"]="Requires 30 real minutes of continuous rain in Central Shroud.",
        ["Wulgaru"]="Start battlecraft or Grand Company leves in East Shroud.",
        ["Mindflayer"]="Cross spawn points at night during the new moon.",
        ["Thousand-cast Theda"]="Catch a Judgeray in North Shroud (17:00–21:00 ET).",
        ["Zona Seeker"]="Catch a Glimmerscale in Western Thanalan during clear or fair weather.",
        ["Brontes"]="Consume food or drink while standing on a spawn point.",
        ["Lampalagua"]="Start battlecraft or Grand Company leves in Eastern Thanalan.",
        ["Nunyunuwi"]="Keep every Southern Thanalan FATE successful for one real hour.",
        ["Minhocao"]="Defeat 100 Earth Sprites in Northern Thanalan.",
        ["Croque-mitaine"]="Mine Grade 3 La Noscean Topsoil (19:00–22:00 ET).",
        ["Croakadile"]="Cross spawn points during the full moon's nighttime windows.",
        ["The Garlok"]="Requires 200 real minutes without rain or showers in Eastern La Noscea.",
        ["Bonnacon"]="Gather La Noscean Leeks (08:00–11:00 ET).",
        ["Nandi"]="Cross spawn points with a minion summoned.",
        ["Chernobog"]="A player must die in the zone.",
        ["Safat"]="Fall far enough to reduce your HP to 1 in Coerthas Central Highlands.",
        ["Agrippa the Mighty"]="Open a treasure-map chest in Mor Dhona.",
        ["Kaiser Behemoth"]="Cross spawn points with the Behemoth Heir minion summoned.",
        ["Senmurv"]="Complete Cerf's Up successfully five consecutive times.",
        ["The Pale Rider"]="Open a treasure-map chest in the Dravanian Hinterlands.",
        ["Gandarewa"]="Gather 50 Aurum Regis Ore and 50 Seventh Heaven from their timed nodes.",
        ["Bird of Paradise"]="Let Squonk, the zone's B rank, use Chirp.",
        ["Leucrotta"]="Defeat 50 each of Allagan Chimera, Lesser Hydra and Meracydian Vouivre.",
        ["Okina"]="Defeat 100 Yumemi and 100 Naked Yumemi during the full moon.",
        ["Gamma"]="Cross spawn points with Toy Alexander summoned at night (17:00–08:00 ET).",
        ["Orghana"]="Complete Not Just a Tribute, then cross spawn points.",
        ["Udumbara"]="Defeat 100 Leshy and 100 Diakka in the Fringes.",
        ["Bone Crawler"]="Take a Chocobo Porter across the middle of the Peaks.",
        ["Salt and Light"]="Discard 50 inventory items in the Lochs.",
        ["Aglaope"]="Cross spawn points with the Scarlet Peacock minion summoned.",
        ["Ixtab"]="Defeat 100 each of Cracked Ronkan Doll, Thorn and Vessel.",
        ["Gunitt"]="Let a fully enlarged Clionid hit a player with Buccal Cones.",
        ["Tarchia"]="Use Blue Mage Self-destruct on a spawn point.",
        ["Tyger"]="Discard a Rail Tenderloin in Lakeland.",
        ["Forgiven Pedantry"]="Gather 50 Dwarven Cotton Bolls in Kholusia.",
        ["Burfurlur the Canny"]="Cross spawn points with Tiny Troll summoned in clear/fair weather, 09:00–17:00 ET.",
        ["Sphatika"]="Defeat 100 each of Asvattha, Pisaca and Vajralangula.",
        ["Armstrong"]="Die on a spawn point wearing Mended Imperial Pot Helm and Mended Imperial Short Robe.",
        ["Ruminator"]="Defeat 100 each of Thinkers, Wanderers and Weepers.",
        ["Ophioneus"]="Discard five Eggs of Elpis together in Elpis.",
        ["Narrow-rift"]="Have ten players cross spawn points with wee Ea minions summoned.",
        ["Kirlirger the Abhorrent"]="Cross spawn points during foggy new-moon nights.",
        ["Ihnuxokiy"]="Cross spawn points with the Morpho minion summoned.",
        ["Neyoozoteel"]="Discard 50 Fish Meal together in Yak T'el.",
        ["Sansheya"]="Complete You Are What You Drink successfully three consecutive times.",
        ["Atticus the Primogenitor"]="Craft a high-quality Rroneek Steak in Heritage Found.",
        ["The Forecaster"]="Cast Blue Mage Northerlies on a spawn point.",
    };
}
