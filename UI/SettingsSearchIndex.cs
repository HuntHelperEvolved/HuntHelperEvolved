using System;
using System.Linq;

namespace HuntHelperEvolved;

internal static class SettingsSearchIndex
{
    // Keep visible phrases alongside the category aliases so a setting can be found by its label.
    public static string Labels(string page) => page switch
    {
        "Appearance" => "Theme Graphite Daylight Dalamud Show release notes after updates",
        "Train" => "Auto-mark dead using Hunt Tally Echo a mark to chat when its row is clicked Tick a mark dead when the battle log says it died Teleport also drops the map flag Show how long ago each mark was last seen Show spicing markers Auto-advance to the next mark when the current one dies Echo and flag the mark it advances to Row padding logical pixels Detection interval seconds Remind me on entering an S-rank zone with bongo sound Show these watches on the train list Hide zone names in the train popout Swap mark and zone names in the train popout",
        "Counters" => "Count only kills I land",
        "Map" => "Show spawn points on the in-game map Hide occupied spawn points Dim points for found A-ranks Show live marks on the in-game map Show a control bar above the map A-rank points B-rank S-rank A-rank marks Alt-click a spawn point on the map to flag it Mark SS event minion locations Write mark names and health on the map Name text size Dark outlines around map dots Dot size Player guides Show these at all Range circle Circle colour Circle radius scale Circle line width Heading line Heading line colour Heading line thickness Position dot Position dot colour Position dot size Projected path Path colour Dot colours Empty point Spawn point in train Live B rank Live A rank Live S rank SS event minions Mark name text Mark name outline Reset map colours Marks other members can see on my map Which spawn points the S can still use S candidate outline width S candidate outline confirmed fill",
        "Notifications" => "Bongo sounds S-rank zone-entry reminders Community S-rank spawn release alerts Announce in chat Chat message templates B message A message S message Show fly text Speak detections Spoken message templates Voice Volume Test Chat alerts for group S sightings and Faloop spawns releases Only my current data centre Test chat format RELAY rules presets Saved presets Save as Load Update saved Rename preset Delete preset Copy /hhv S-rank rules Reset relay rules Current world Current DC World Data centre All expansions Shadowbringers Endwalker Dawntrail ShB+ Add rule Enable rule Duplicate Remove",
        "Travel" => "Aetheryte blacklist Expansion Zone Aetheryte Blacklist Remove",
        "Sharing" => "Enabled Allow unencrypted development connections Server URL Password Show Display name Reconnect Upload saved local marks Upload saved local watches Online now The train What I can see S-rank kills I witness Connection settings",
        "ActiveMarks" => "Active Marks window filters Include community S-rank reports Include marks seen only by me Alive Dead visible corpses Pulled Not pulled Unknown combat status Ranks B A S SS Scope rules Add rule Duplicate rule Remove rule Enable this rule No rules enabled Saved presets Save as Load Update saved Rename preset Delete preset Any world Current world Current DC Current data centre Selected worlds Selected data centres Expansions All expansions Shadowbringers Crystal Mateus Data centres All data centres Worlds All worlds Show data centre beside world Status colours Reset window filters",
        "Discord" => "Enabled Label optional Webhook URL Remove Add webhook Send test message",
        "Tally" => "Only count marks I hit Only count marks I was in combat for Strict credit Detection radius yalms Print a chat message on each kill Send every mark death over IPC Only count A and S ranks the game says it rewarded Use damage detection B ranks A ranks S and SS ranks Check on login Seed now Re-resolve names Counter Achievement Seeded Total Detail log entries kept Reset all characters Confirm reset Cancel",
        "Reset" => "All preferences Train behavior and layout S-rank counters Travel and aetherytes Map display Map colours Player guides Mark detection alerts S-rank reminders and community alerts Sharing preferences Active Marks and A S-rank filters Hunt tally preferences Update notifications Saved sync connection Discord destinations Clear saved connections Hunting Map appearance Windows and tally Restore defaults Clear saved details",
        _ => string.Empty
    };

    public static bool Matches(string query, string page, string aliases = "")
    {
        static string Normalize(string text) => new(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray());
        var searchable = Normalize(page + " " + aliases + " " + Labels(page));
        return Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(searchable.Contains);
    }
}
