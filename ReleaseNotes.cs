using System;
using System.Linq;
using System.Reflection;

namespace HuntHelperEvolved;

/// <summary>
/// One line of a release's notes.
///
/// Credit is per line rather than per release because a release is usually
/// several people's work, and "who do I thank for this particular thing" is the
/// question the notes are there to answer.
/// </summary>
public sealed record ReleaseChange(string Area, string Text, string Credit, int Issue = 0);

/// <summary>One published version.</summary>
public sealed record Release(string Version, string Date, string Summary, ReleaseChange[] Changes);

/// <summary>
/// What changed in each version, shown in game on the What's new tab.
///
/// Kept in code rather than shipped as a data file, for the same reason the
/// spawn point tables are: a release stays two files, a dll and a manifest,
/// with nothing alongside them to go missing or fall out of step.
///
/// Newest first. Add to the top when cutting a release, and make sure the
/// version matches the one in the csproj — MissingCurrentVersion below is what
/// catches it when they drift apart.
/// Keep in-game and public release notes focused on user-visible changes.
/// Prefer a short sentence per change; put usage instructions in Help.
/// Include update guidance only when users must take a specific action; omit
/// routine installation, feed and "no server update required" boilerplate.
/// </summary>
public static class ReleaseNotes
{
    public const string Kihtli = "kihtli";
    public const string MusicManBowls = "MusicManBowls";

    public static readonly Release[] All =
    {
        new("0.7.0.7", "2026-10-23", "Restored rallies, nearby player counts and currency warnings.",
        [
            new("Rally flags", "Calculate Rally Flags restores removed rallies when living marks remain in that zone and instance.", Kihtli),
            new("Nearby players", "Train and /hht show nearby player counts beside marks remaining; players no longer seen drop out after five seconds.", Kihtli),
            new("Currency", "Currency glows red when remaining train rewards would exceed your holding caps. Hover for details; the weekly Mnemonics cap alone does not trigger a warning.", Kihtli),
        ]),
        new("0.7.0.6", "2026-10-19", "Reliable shared rally locations.",
        [
            new("Rally flags", "Recalculating shared rallies now updates their locations together with their names.", Kihtli),
        ]),
        new("0.7.0.5", "2026-10-15", "Clearer reports and help, with reliable plugin kill attribution.",
        [
            new("Train reports", "Bear kills within their world and expansion's observed respawn range join that summary; kills outside it keep individual details.", Kihtli),
            new("Kill attribution", "Plugin-witnessed kills now take priority when Bear reports arrive first, including kills witnessed without battle credit.", Kihtli),
            new("Help and notes", "Shortened in-game Help and release notes to focus on useful controls and changes.", Kihtli),
        ]),
        new("0.7.0.4", "2026-10-15", "Reliable A-rank timers, combat mapping and saved currency templates.",
        [
            new("A-rank kills", "Witnessed A-rank deaths save exact /hha timers outside trains, while paused or offline; sighting sharing updates the group without adding rows. Duplicates retain the original time; corpses alone remain unknown.", Kihtli),
            new("Combat mapping", "A/B marks first found fighting or damaged now use normal spawn-point matching, fixing cases such as Sally; matched origins stay fixed during movement.", Kihtli),
            new("Currency templates", "Currency templates survive reloads, restarts and new trains; Reset template or resetting Train preferences restores the default.", Kihtli),
            new("Currency wording", "Copied currency messages follow your template without adding a weekly-allowance note; add one yourself if wanted.", Kihtli),
        ]),
        new("0.7.0.3", "2026-10-14", "Bear A-rank reports, currency messages and more reliable map markers.",
        [
            new("Snipe corrections", "Exact snipe corrections now reach train rows and reports despite clock differences, while marks remain classified as sniped.", Kihtli),
            new("Train currency", "Currency in Train and /hht previews rewards or safe starting balances for living marks, with custom templates and plain, party or shout copies.", Kihtli),
            new("Bear A-ranks", "Bear kills update timers and train rows; sightings show UP without adding rows. For the same spawn, witnessed kills take priority and Bear kills replace snipes. Reports label Bear kills separately.", Kihtli),
            new("A-rank board", "The A-rank board adds sighting times, clickable locations and Not in train list; green means UP in train, blue means UP outside it or READY.", Kihtli),
            new("Map markers", "Map markers recover from failed image loads and stay visible during texture changes, reducing the need to reopen the map.", Kihtli),
            new("Updating", "Bear A-ranks require server 0.3.33-bear-preview.7+ and receiving plugins 0.7.0.3+; older plugins may miss UP reports or retain uncertain snipe timers.", Kihtli),
        ]),
        new("0.7.0.2", "2026-10-08", "Share exact A-rank corrections after unknown snipes.",
        [
            new("Shared A-rank timers", "Sniped in /hha shares exact kill times and unknown-time bounds by world and instance, even without a train row.", Kihtli),
            new("Correction history", "Accepted corrections survive reconnects and server restarts; older train reports cannot undo them, while newer sightings start the next cycle.", Kihtli),
            new("Sharing status", "The board shows sharing confirmation. Enable train sharing with compatible servers and updated receivers; re-submit earlier local corrections after connecting or updating.", Kihtli),
        ]),
        new("0.7.0.1", "2026-10-06", "Manual A-rank snipe timers and quieter scouting.",
        [
            new("A-rank snipes", "Sniped in /hha and the main A-rank table resets a local timer using a known or unknown kill time, even offline.", Kihtli),
            new("Unknown snipe times", "Unknown snipes use available sighting or previous-window bounds; without an earliest bound, timers and reports show only Ready by.", Kihtli),
            new("Quiet scouting", "Scouting, removing and reordering marks no longer trigger follow callouts or flags; deaths still advance normally and snipes advance silently.", Kihtli),
            new("Train colours", "Spiced marks use consistent red and rally stops consistent green across themes.", Kihtli),
        ]),
        new("0.7.0.0", "2026-10-01", "A redesigned hunt workspace, Bear S-ranks and filter presets.",
        [
            new("Hunt workspace", "The /hh workspace unites trains and hunt boards, shows feed status, and adds searchable settings with Graphite, Daylight and Dalamud themes.", Kihtli),
            new("Window controls", "Standalone windows gain opacity, pinning and click-through controls, with a shared opacity setting and individual overrides.", Kihtli),
            new("Train controls", "Main and compact Train views share labelled controls, responsive layouts and zone-first labels; preset editing protects unsaved drafts and confirms deletion.", Kihtli),
            new("S-rank details", "S-rank details combine mapping, counters and watches; right-click board headers to add Last kill, Points, Opens or Ready by.", Kihtli),
            new("Bear Toolkit", "Bear S-rank HP defaults on unless previously disabled; toggle it under Sharing. Priority is plugin, Bear, then Faloop; pull clocks use Faloop and older HP appears grey.", Kihtli),
            new("Shared S-rank kills", "Confirmed Bear kills update shared S-rank timers and mapping without adding train entries or lifetime tally credit.", Kihtli),
            new("World and expansion rules", "Filter /hhv by world or DC, rank and expansion; Current world and Current DC follow your travels.", Kihtli),
            new("Named presets", "Save separate Active Marks and RELAY presets, copy rules between them, and retain them through resets. Active edits save automatically; saved presets change only with Update.", Kihtli),
            new("Relay notifications", "RELAY includes available HP and combines plugin, Bear and Faloop alerts into one notification per spawn; Test chat format previews locally.", Kihtli),
            new("Tally export", "Export the filtered per-mark lifetime kill list as CSV.", Kihtli),
        ]),
        new("0.6.0.3", "2026-09-28", "Shared A-rank locations, travel shortcuts and compact timer boards.",
        [
            new("A-rank locations", "Last known location in /hha flags shared coordinates by world and instance, surviving resets and reloads; server 0.3.32+ supplies them to later arrivals.", Kihtli),
            new("A-rank travel", "Ctrl-click an A-rank name to travel with Lifestream to its world, nearest eligible aetheryte and supported instance.", Kihtli),
            new("Timer windows", "Timer boards are more compact; feed status moves to /hh > Server, maintenance follows Faloop, and Conditions tooltips explain spawn requirements.", Kihtli),
        ]),
        new("0.6.0.2", "2026-09-27", "Choose when to follow trains and recalculate rallies.",
        [
            new("Following the train", "Follow train or /hht on/off saves whether navigation flags and echoes advance automatically; scouting and manual travel remain available.", Kihtli),
            new("Rally flags", "Setup > Calculate Rally Flags recalculates pending rallies from a paused preset without changing mark order or completed stops; sharing requires server 0.3.31+.", Kihtli),
        ]),
        new("0.6.0.1", "2026-09-27", "Clearer maps, settings resets and configurable Umbra widgets.",
        [
            new("Settings resets", "Reset individual settings categories or all preferences while keeping hunt data and saved connections; Reset all turns sharing off.", Kihtli),
            new("Map colours", "New map colours distinguish empty points, train locations and hunt ranks; use Reset settings > Map colours to apply them to existing settings.", Kihtli),
            new("Map outlines", "Optional dark outlines improve map-dot contrast and keep S-rank rings outside dots; candidate width ranges from 1–12.", Kihtli),
            new("Default settings", "New defaults use 1-second polling, hidden dead marks, 14px rows/labels, visible spawn points, 10px dots and detection-ring width 4; saved settings stay unchanged.", Kihtli),
            new("Missing-mark scouting", "Dim points used only by A-ranks already alive in an incomplete train zone; shared or unknown points stay visible. Toggle this under Map.", Kihtli),
            new("Train popout", "Optionally show zone names before mark names in the train popout using Swap mark and zone names.", Kihtli),
            new("Bongo sounds", "Notification settings group zone-entry and community-alert sounds, allowing either sound to be muted without hiding its notification.", Kihtli),
            new("Umbra companion", "Umbra companion 0.2.0.0 adds independent world and expansion choices per widget, with optional fixed-world labels.", Kihtli),
        ]),
        new("0.6.0", "2026-09-25", "A clearer train workspace, shorter reports and an Umbra widget.",
        [
            new("Train window", "Route, Reports and Setup organise the Train views, with compact travel/status rows and preset selection while scouting.", Kihtli),
            new("Scouting reports", "Scouting reports gain previews and 256-character notes, count full rosters and known instances, and place the Discord import code first.", Kihtli),
            new("Completed trains", "Completed-train reports summarise respawn ranges by world and expansion; sniped and missing marks stay separate, with detailed history available in game.", Kihtli),
            new("Controls", "Hold Shift when sending reports or ending trains; Alt-click flags map points, Ctrl-click X removes rows, and right-click toggles spicing when enabled.", Kihtli),
            new("Umbra widget", "The Umbra widget shows remaining DT/EW/ShB marks and spawn progress; left-click opens the train, right-click cycles expansions, and 100% requires the full roster.", Kihtli),
            new("Install the widget", "In Umbra > Settings > Plugins > Install from repository, use owner HuntHelperEvolved and repository Umbra.HuntHelperEvolved; restart if prompted, then choose Add Widget > Hunt Helper Evolved.", Kihtli),
            new("Fixes", "Fixed stale Active Marks, clock differences, Discord delivery and settings saving; train automation and counter resets now work with windows closed.", Kihtli),
        ]),
        new("0.5.0.22", "2026-09-21", "Simpler scouting and configurable train-point colours.",
        [
            new("Scouting", "Scanning/Paused replaces Tracking this train for recording marks and scout credits; enable Sharing > The train to share, and unpause after login.", Kihtli),
            new("History", "Existing train rows keep observed deaths and report history while scanning is paused; auto-mark preferences still apply.", Kihtli),
            new("Map", "Living train marks highlight their spawn points after leaving sight; customise Spawn point in train under Map colours.", Kihtli),
        ]),
        new("0.5.0.21", "2026-09-21", "Safer FATE tracking and more reliable train recovery.",
        [
            new("Counters", "Fixed FATE-related crashes during teleporting; Nunyunuwi tracking restarts after loading or an unconfirmed FATE result.", Kihtli),
            new("Train", "Fixed false changed-mark warnings when ending a train with sniped marks; genuine changes still protect the shared train.", Kihtli),
            new("Scouting", "Unpaused scouting restores killed or sniped marks when seen alive, clearing their previous death times.", Kihtli),
            new("A ranks", "Confirmed A-ranks remain UP across zones and reloads for up to 14 days, until newer death, snipe or maintenance evidence replaces them.", Kihtli),
            new("Rallies", "Rallies return when completed areas are scouted alive again; reselecting presets rebuilds missing live-mark rallies. Sharing requires server 0.3.27.", Kihtli),
        ]),
        new("0.5.0.20", "2026-09-16", "Train navigation stays visible when controls are collapsed.",
        [
            new("Train", "Next Mark and Next Aetheryte remain visible in main and compact Train views when controls and scouts are collapsed.", Kihtli),
            new("Reset", "Undo reset moves inside Train controls, with explanations that fit narrow windows.", Kihtli),
        ]),
        new("0.5.0.19", "2026-09-16", "Conductor presets, shared rallies and manual route ordering.",
        [
            new("Presets", "Conductor presets persist between trains and order new scouts by expansion, zone and instance, using saved mark order or estimated travel distance.", Kihtli),
            new("Rallies", "Add aetheryte rally stops at numbered instances and selected expansion changes, choosing destinations yourself or using route suggestions.", Kihtli),
            new("Sharing", "Save presets locally or share route ordering with the group; shared presets require server 0.3.26+, while older plugins still receive ordered marks and rallies.", Kihtli),
            new("Manual order", "Dragging marks or expansions pauses automatic ordering across trains and resets; new scouts append until you reselect a preset, leaving the saved preset unchanged.", Kihtli),
            new("Travel", "Garlemald routes account for the required flight north from Tertium to (31.5, 12.7).", Kihtli),
            new("Counters", "Fixed shared counter resets failing on trigger-name casing; resets remain scoped to the correct world, zone and instance.", Kihtli),
        ]),
        new("0.5.0.18", "2026-09-14", "Faster windows, clearer train lists and visibility filters.",
        [
            new("Performance", "Hunt boards, Active Marks, trains and tallies use less processing and memory; changing display preferences is lighter.", Kihtli),
            new("Train", "Train rows group by world then expansion, with folding, ordering and consistent travel/status controls.", Kihtli),
            new("Reports", "Reports and cleanup preserve unrun worlds and watches; routine sightings no longer block cleanup. Shared completion requires server 0.3.24+.", Kihtli),
            new("S ranks", "Hide unmet conditions in /hhs hides red condition states while keeping open, opening-soon and unknown rows visible.", Kihtli),
            new("Map", "Optional Hide occupied spawn points hides clearly matched points near live marks, restoring them when the mark moves away or disappears.", Kihtli),
            new("Stability", "Improved map stability during loading, plugin startup/unload cleanup and speech cleanup.", Kihtli),
            new("Sharing", "Sharing gains data limits; unencrypted connections require explicit opt-in in settings.", Kihtli),
        ]),
        new("0.5.0.17", "2026-09-13", "Fixed map-overlay crashes outside hunt zones.",
        [
            new("Map", "Fixed map-overlay cleanup crashes in cities and duties; overlays stay hidden outside hunt zones and return when needed.", Kihtli),
        ]),
        new("0.5.0.16", "2026-09-12", "A tidier workspace, reliable minion tracking and train watches.",
        [
            new("Workspace", "The /hh workspace gains Train, S Ranks, Settings and Help tabs, collapsible shared controls, and a Windows menu for separate tools.", Kihtli, 43),
            new("Detection", "Fixed duplicate FOUND messages while zoning by waiting for the current world to be known.", Kihtli, 38),
            new("Active Marks", "Nearby-player estimates use the largest fresh scout count, avoiding alternating or added-together counts.", Kihtli, 39),
            new("Travel", "Lifestream travel continues to the mark’s instance after reaching its world and zone, where supported; use Teleport or Ctrl-click.", Kihtli, 40),
            new("Active Marks", "Active Marks stays visible during travel between data centres after login.", Kihtli, 41),
            new("Active Marks", "Active Marks can show a data-centre label beside world names.", Kihtli, 42),
            new("Map", "Right-click no longer changes map flags; left-click flags and Shift-left-click manual exclusions remain available.", Kihtli, 44),
            new("Minions", "Identical SS minions retain separate HP, combat state and icons; requires server 0.3.23 and updated reporting and viewing plugins.", Kihtli),
            new("Faloop", "Faloop snipes advance bounded spawn windows and reset mapping without repeated reports erasing new evidence; requires server 0.3.22+.", Kihtli),
            new("Train watches", "Train watches follow eligible world/instance windows and confirmed locations; enable automation on the conductor’s client and use server 0.3.22+.", Kihtli),
            new("Commands", "The installer shows /hh or /htr commands according to Hunt Helper availability, while leaving other plugins’ commands alone.", Kihtli),
        ]),
        new("0.5.0.15", "2026-09-11", "Shared manual mapping and more reliable hunt tracking.",
        [
            new("Mapping", "Shift-click S-capable points or use /hhs point counts to share manual exclusions until the next kill; undo preserves automatic evidence. Requires server 0.3.21.", Kihtli),
            new("S ranks", "Fixed stale S-rank UP states after kills across multiple instances.", Kihtli),
            new("Conditions", "Eligible S-rank names and countdowns turn yellow within five minutes of conditions opening, then green.", Kihtli, 35),
            new("Map", "Map controls move below the map when space above is limited, keeping the title bar draggable.", Kihtli, 36),
            new("Commands", "Added /hh equivalents for /htr commands, /hhm for map controls and /hhtally with config/ipc arguments; /hha opens A-rank timers.", Kihtli),
            new("Trains", "Empty trains submit nothing unless unreported history remains; partial reports preserve unfinished legs, and full completion resets scouts.", "musicmanbowls, kihtli", 37),
        ]),
        new("0.5.0.14", "2026-09-10", "Scout names share only when added.",
        [
            new("Scouts", "Manual scout names share only after Enter or Add scout; remove committed names explicitly and add them again to restore them.", Kihtli),
        ]),
        new("0.5.0.13", "2026-09-10", "Reliable scout resets and Faloop reports after maintenance.",
        [
            new("Scouting", "Scouting starts paused; unpause for automatic credits. Full completion clears credits and pauses again; partial reports retain them. Update scouts to 0.5.0.13 and use server 0.3.20.", Kihtli),
            new("Faloop", "Faloop can confirm timers after maintenance without replacing live health already seen by scouts; requires server 0.3.20.", Kihtli),
        ]),
        new("0.5.0.12", "2026-09-10", "S-rank respawn windows match Faloop.",
        [
            new("Timers", "Maintenance and ARR respawn ranges now match Faloop; existing timestamps stay unchanged. Server 0.3.19 applies the correction to its admin board.", Kihtli),
            new("Credits", "Corrected partial-train credits: musicmanbowls contributed plugin PR #34 and server PR #2; kihtli added review fixes and reconnect safeguards.", "musicmanbowls, kihtli"),
        ]),
        new("0.5.0.11", "2026-09-09", "Report completed train legs while keeping the rest scouted.",
        [
            new("Trains", "End Train clears completed legs’ dead marks while retaining unrun legs, live marks, watches and credits, including after reconnect. Update all conductors and use server 0.3.18 before partial reports.", "musicmanbowls, kihtli"),
        ]),
        new("0.5.0.10", "2026-09-09", "Less flicker in Active Marks.",
        [
            new("Active Marks", "Active Marks holds rows for five seconds through brief reporting gaps, updates new HP immediately and labels your scout alias You.", Kihtli),
        ]),
        new("0.5.0.9", "2026-09-09", "Remove shared scout credits and see their source.",
        [
            new("Scouts", "Scout credits show who supplied them and whether they were automatic; removal persists across reconnects and resets. Requires server 0.3.16.", Kihtli),
        ]),
        new("0.5.0.8", "2026-09-08", "Fewer repeated detection alerts.",
        [
            new("Detection", "Marks moving in and out of range no longer repeat chat, speech or fly-text alerts for the same spawn.", Kihtli),
        ]),
        new("0.5.0.7", "2026-09-08", "Plugin windows appear only after login.",
        [
            new("Windows", "Plugin windows and automatic release notes wait until character login, staying hidden at title and character-selection screens.", Kihtli),
        ]),
        new("0.5.0.6", "2026-09-08", "Automatic release notes work for testing updates.",
        [
            new("Updates", "Automatic release notes now recognise testing-version updates and open once unless disabled in settings.", Kihtli),
        ]),
        new("0.5.0.5", "2026-09-08", "Maintenance-aware timers, standalone commands and clearer settings.",
        [
            new("Maintenance", "Timer boards show offline worlds and current Faloop instances, use actual restart times and retain retired instances only in history.", Kihtli),
            new("Counters", "S-rank counters and watches move to S Counters, with preferences under Settings > Counters; Scout focuses on reports and credits.", Kihtli),
            new("Settings", "Settings gain compact task-based categories, sync/tally preferences and searchable Help, replacing lengthy tutorial tooltips.", Kihtli),
            new("Standalone", "Removed Hunt Helper compatibility emulation; /hh shortcuts work natively, while HHE IPC and sharing remain available.", Kihtli),
            new("Timers", "Click timer headers to cycle ascending, descending and automatic order; unknown values stay last, and right-click selects columns.", Kihtli),
            new("A ranks", "A-rank timers adopt the S-rank board’s layout and progress bars while retaining instance filters, bounded snipes and normal row backgrounds.", Kihtli),
        ]),

        new("0.5.0.4", "2026-09-07", "Nearby-player estimates include culled characters.",
        [
            new("Active Marks", "Nearby-player estimates include culled characters and reset on travel or logout; departed players may remain counted until their positions refresh.", Kihtli),
        ]),

        new("0.5.0.3", "2026-09-07", "Safer train completion and clearer hunt windows.",
        [
            new("A ranks", "A-rank snipes show bounded windows matching Discord when live evidence exists, with bounds retained across server resets and reconnects.", Kihtli),
            new("Train", "End Train clears only after successful Discord delivery and server confirmation, preserving changed trains and Undo; requires server 0.3.13.", Kihtli),
            new("Scouts", "Scout credits combine automatic contributors and manual names, with collapsible credits and controls in the popout.", Kihtli),
            new("Active Marks", "Active Marks adds zones, nearby-player counts within 50 yalms and elapsed Faloop activity time when known.", Kihtli),
            new("S ranks", "S-rank names show green for open timers/conditions, red for unmet timed conditions, and grey before opening or when unknown.", Kihtli),
        ]),

        new("0.5.0.2", "2026-09-07", "Saved A-rank timers for returning scouts.",
        [
            new("A ranks", "A-rank kill history loads by world and instance even without train sharing; server 0.3.12 retains it across resets and restarts.", Kihtli),
        ]),

        new("0.5.0.1", "2026-09-07", "Standalone hunts and private-group sharing.",
        [
            new("Sync", "Share train order, sightings, S-rank mapping and personal trigger-kill totals through a private password-protected group server.", Kihtli),
            new("Boards", "Use /hhs for S-rank windows, /hha for A-rank instances, and /hhsa or /hhv for Active Marks with rank tabs and HP.", Kihtli),
            new("Travel", "Optional Lifestream travel supports worlds, aetherytes and suggested S-rank spawn destinations; spawn alerts include map links.", Kihtli),
            new("Train", "Native train history preserves exact kills, sniped evidence and reset recovery.", Kihtli),
            new("Train", "Expansion totals count hunt marks without including custom rally flags.", MusicManBowls, 31),
            new("Train", "Open next automatically unfolds the next unfinished expansion when a leg finishes.", MusicManBowls, 30),
        ]),

        new("0.4.0.21", "2026-09-07", "Community reports without live feedback appear blue.",
        [
            new("Sightings", "Faloop-only sightings appear blue; fresh scout reports restore live status colours, with grey reserved for unknown combat state.", Kihtli),
        ]),

        new("0.4.0.20", "2026-09-07", "A compact Active Marks overlay.",
        [
            new("Sightings", "Active Marks uses compact HP lines: green unpulled, orange fighting, red dead. Hover for details, click for maps, Ctrl-click or right-click for travel.", Kihtli),
        ]),

        new("0.4.0.19", "2026-09-07", "Active Marks gains rank tabs and live health.",
        [
            new("Sightings", "Active Marks adds All/S/A/B tabs, live HP and status filters by rank, world/DC and expansion; requires server 0.3.11.", Kihtli),
        ]),

        new("0.4.0.18", "2026-09-07", "Shared spawn-trigger kill counters.",
        [
            new("Counters", "Trigger counters show shared personal-kill totals by world and instance, with persistent counts and a separate reset; requires server 0.3.10.", Kihtli),
        ]),

        new("0.4.0.17", "2026-09-07", "Travel to S-rank spawn attempts.",
        [
            new("Travel", "Ctrl-click S-rank names to travel toward candidate spawn points without a reported location, respecting excluded aetherytes.", Kihtli),
        ]),

        new("0.4.0.16", "2026-09-07", "Travel status clears when teleporting ends.",
        [
            new("Travel", "Teleporting status clears after casting and loading finish, while preventing overlapping travel requests.", Kihtli),
        ]),

        new("0.4.0.15", "2026-09-07", "One-click S-rank travel and clearer active reports.",
        [
            new("Travel", "One-click world travel continues to the aetheryte through loading and temporary refusals; Ctrl-click /hhs names to travel to known locations.", Kihtli),
            new("S ranks", "Active S-rank reports move to the top of /hhs with red backgrounds; local detections no longer produce duplicate relay alerts.", Kihtli),
        ]),

        new("0.4.0.14", "2026-09-07", "Undo is easier to find.",
        [
            new("Train", "Undo reset is visible in both Train views, including empty trains; reset recovery restores locally and turns train sharing off.", Kihtli),
        ]),

        new("0.4.0.13", "2026-09-07", "Native train reports and reliable kill times.",
        [
            new("Reports", "Native reports separate confirmed kills, unknown deaths and live marks by world; removed dead marks remain in history across reloads.", Kihtli),
            new("Reports", "Reports block overlapping submissions and preserve shared or changed trains; unchanged local trains can be cleared with Undo.", Kihtli),
            new("Detection", "Finding a corpse no longer invents a kill time; exact deaths require witnessing the same mark alive first.", Kihtli),
            new("Sharing", "Clipboard codes and IPC preserve worlds and explicit kill/snipe times; legacy times stay unknown and queued tally kills retain their world.", Kihtli),
        ]),

        new("0.4.0.12", "2026-09-07", "Shared scouting, S-rank tracking and smoother expansion changes.",
        [
            new("Train", "Expansion totals count hunt marks only, keeping rally flags visible without inflating the count.", MusicManBowls, 31),
            new("Train", "Open next unfolds the next unfinished expansion in route order while respecting Hide dead; outstanding rally stops still need completion.", MusicManBowls, 30),
            new("Sync", "Private-group sharing includes scouting, order, HP and locations, keeps expansion blocks together and allows local undo after train resets.", Kihtli),
            new("Map", "A/B sightings rule out S-rank candidates; gold outlines show possibilities and a filled final point confirms the remainder. Live dots disappear when no longer seen.", Kihtli),
            new("S ranks", "The /hhs board adds windows, filters, columns and condition countdowns; /hhsa shows active reports and optional Lifestream travel.", Kihtli),
            new("Alerts", "FOUND and RELAY alerts include worlds and known map links, and wait until loading ends.", Kihtli),
            new("A ranks", "The /hha board separates instanced A-rank windows and hides unnecessary instance labels.", Kihtli),
        ]),

        new("0.4.0", "2026-09-06", "Expansion grouping and honest sniped-mark timers.",
        [
            new("Train", "Group trains into expansion blocks while retaining scout order; navigation, exports and reports follow the displayed route.", Kihtli),
            new("Train", "Drag expansion headings to reorder or click to fold them; folded groups retain live counts and survive reloads.", Kihtli),
            new("Train", "Sniped gains its own button and report section, with respawn bounds measured from last seen alive to found missing.", Kihtli, 1),
            new("Train", "S-rank watch results are editable below the train and in the popout; added Neyoozoteel in Yak T’el.", Kihtli, 9),
            new("Train", "Import clipboard trains from either Train view, merging marks without overwriting existing entries.", Kihtli, 12),
            new("Train", "Train export codes retain worlds, preventing marks from different worlds colliding during import.", Kihtli, 13),
            new("Plugin", "Other plugins can read and add train marks; Hunt Helper integrations work when it is absent, with status shown under About.", Kihtli, 11),
            new("Map", "Live SS minions use the S-rank colour, distinguishing them from their orange event spawn points.", Kihtli),
        ]),

        new("0.3.0", "2026-09-05", "Accurate map positions and better mark detection.",
        [
            new("Map", "Live map dots follow each mark’s actual position instead of snapping to spawn points.", Kihtli, 14),
            new("Map", "Spawn points and live marks gain independent visibility switches and B/A/S rank filters.", Kihtli),
            new("Map", "Click spawn points, SS-minion spots or SS-mark locations to place map flags before anything spawns.", Kihtli, 10),
            new("Map", "Live SS minions appear larger and above their spawn points, making occupied spots easier to distinguish.", Kihtli),
            new("Map", "Marks on different worlds and instances stay separate, so a death cannot hide another live mark.", Kihtli, 8),
            new("Train", "Visible marks are marked dead at zero HP regardless of kill credit, alongside battle-log detection; long-range detection was not yet verified.", Kihtli),
            new("Detection", "SS events start only from the game’s announcement; player chat can no longer trigger false events.", Kihtli),
            new("Tally", "Marks Slain adds B/A/S filters alongside name search, with results ordered by kill count.", Kihtli, 7),
            new("Commands", "The /hh, /hht, /hhn, /hhna and /hhc shortcuts work when Hunt Helper is absent and leave its commands alone when installed.", Kihtli, 4),
            new("Commands", "The /htr, /htrt and /htrc commands now toggle windows closed as well as open.", Kihtli),
        ]),

        new("0.2.1", "2026-09-03", "The plugin icon remains visible after installation.",
        [
            new("Plugin", "Fixed the plugin icon disappearing after installation.", Kihtli),
        ]),

        new("0.2.0", "2026-09-03", "Map labels, detection notifications and working trigger counters.",
        [
            new("Map", "Live map marks show names and remaining HP, with customisable colour, outlines and text size.", Kihtli, 5),
            new("Map", "Detection rings and projected paths remain visible after teleporting with the map open.", Kihtli, 2),
            new("Detection", "Detection alerts support chat, fly text and speech, with custom templates, colours and independent B/A/S switches for each channel.", Kihtli, 6),
            new("Counters", "Fixed non-kill counters for Forgiven Pedantry, Squonk and Salt and Light, and added Gandarewa tracking.", MusicManBowls, 3),
            new("Counters", "Added Narrow-rift’s Wee Ea headcount and Nunyunuwi’s no-failed-FATE clock.", MusicManBowls, 3),
        ]),

        new("0.1.0", "2026-09-03", "Hunt Helper Evolved combines train and tally tools.",
        [
            new("Plugin", "Hunt Train Relay and Hunt Tally combine with new map tools; existing settings and tallies carry over.", MusicManBowls),
        ]),
    };

    /// <summary>
    /// The running version, including a nonzero testing-build revision.
    /// </summary>
    public static string CurrentVersion { get; } = ReadCurrentVersion();

    private static string ReadCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "unknown"
            : version.Revision > 0 ? version.ToString(4) : version.ToString(3);
    }

    /// <summary>The notes for the running version, if they have been written.</summary>
    public static Release? Current =>
        All.FirstOrDefault(r => r.Version == CurrentVersion);

    /// <summary>
    /// True when the running build has no notes. Says out loud on the tab that
    /// someone bumped the version and forgot this file, rather than quietly
    /// showing the previous release as though it were current.
    /// </summary>
    public static bool MissingCurrentVersion => Current is null;

    /// <summary>
    /// Whether a version string is one this build considers newer than what was
    /// last seen. String comparison would call 0.10.0 older than 0.9.0, so the
    /// parts are compared as numbers.
    /// </summary>
    public static bool IsNewerThan(string version, string previous)
    {
        if (string.IsNullOrWhiteSpace(previous)) return true;
        return Compare(version, previous) > 0;
    }

    private static int Compare(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);

        for (var i = 0; i < 4; i++)
        {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }

        return 0;
    }

    private static int[] Parts(string version)
    {
        var parts = new int[4];
        var split = version.Split('.');

        for (var i = 0; i < 4 && i < split.Length; i++)
            _ = int.TryParse(split[i], out parts[i]);

        return parts;
    }
}
