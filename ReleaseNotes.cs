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
/// </summary>
public static class ReleaseNotes
{
    public const string Kihtli = "kihtli";
    public const string MusicManBowls = "MusicManBowls";

    public static readonly Release[] All =
    {
        new("0.6.0", "2026-09-24", "A tidier train window, scout notes and clearer Discord reports.",
        [
            new("Train window", "The Train tab and /hht now have Route, Reports and Setup pages. Compact rows put TP before the mark name and status buttons on the right. Route keeps navigation and preset details together; Setup holds the train options, including spicing markers.", Kihtli),
            new("Scouting reports", "Add a scout note of up to 256 characters and preview your report before sending. The import code comes first, with the report in the same Discord card when it fits. Larger scouting reports use two messages: code, then report.", Kihtli),
            new("Completed trains", "Shorter reports summarise overall respawn ranges by world and expansion, with sniped and missing marks listed separately. Detailed kill history remains available in game.", Kihtli),
            new("Safer controls", "Hold Alt when clicking a spawn-point or SS-location dot to place a map flag. Ctrl still hides the overlay; hold Ctrl when clicking a route row's X to remove it. With spicing markers enabled, right-click a mark to change its spicing status.", Kihtli),
            new("Active Marks", "Improved removal of stale living entries after known deaths and made shared sightings more reliable when players' clocks differ.", Kihtli),
            new("Reliability", "Improved Discord posting, settings saving and optional Lifestream support. Auto-advance, temporary-flag cleanup and counter resets keep working while plugin windows are closed.", Kihtli),
        ]),
        new("0.5.0.22", "2026-09-21", "Simpler scouting controls and configurable train spawn-point colours.",
        [
            new("Scouting", "Removed Tracking this train. Scanning/Paused now controls whether new marks join the train, and old saved tracking settings no longer block recording or automatic scout credit. Enable Sharing > The train to share contributions. Scouting still starts paused after login or reload.", Kihtli),
            new("History", "Existing train rows continue recording observed deaths and keeping report history while scanning is paused. Auto-mark preferences still apply; End Train remains a manual action.", Kihtli),
            new("Map", "Living, non-sniped train marks highlight their matched spawn points, including after leaving sight. Choose Spawn point in train under Settings > Map > Dot colours; the default is cyan. Dead, sniped or removed rows restore the normal point style. Matching respects world and instance, existing visibility rules and S-rank outlines.", Kihtli),
        ]),
        new("0.5.0.21", "2026-09-21", "Safer FATE tracking and more reliable train recovery and spawn timers.",
        [
            new("Counters", "Fixed an unsafe FATE memory read that could crash the game during teleporting or after a FATE disappeared. The Nunyunuwi watch pauses during loading and starts a fresh observation window afterward. A FATE that disappears before its result is observed restarts the clock conservatively and is labelled unconfirmed.", Kihtli),
            new("Train", "Fixed End Train incorrectly reporting that a sniped mark changed when its last-seen time fell within the same ten-second interval. Genuine changes still prevent the shared train from being cleared.", Kihtli),
            new("Scouting", "Killed or sniped marks return to the live train when an unpaused scout sees them alive again, with their previous death and sniped times cleared.", Kihtli),
            new("A ranks", "Confirmed live A-ranks remain at 100% spawned in /hha after leaving the zone and across reloads for up to 14 days. Newer death, sniped, corpse or maintenance evidence replaces that confirmation; worlds and instances remain separate.", Kihtli),
            new("Rallies", "Preset rallies return when a finished zone or instance is scouted alive again. Reselecting a preset rebuilds rallies for live marks, including previously removed stops. Shared trains require server 0.3.27; local trains work independently.", Kihtli),
        ]),
        new("0.5.0.20", "2026-09-16", "Keep train navigation visible while the controls are collapsed.",
        [
            new("Train", "Next Mark and Next Aetheryte now sit above the controls section in both the /hht popout and the main Train tab, so they remain available while the controls and scouts are collapsed.", Kihtli),
            new("Reset", "Moved Undo reset inside the controls section. Its explanation wraps in narrow windows, and reset messages and Help point to its new location.", Kihtli),
        ]),
        new("0.5.0.19", "2026-09-16", "Reusable conductor presets, shared rally stops and manual route adjustments.",
        [
            new("Presets", "Select a conductor preset in /hht > Train controls & scouts before scouting. Incoming marks follow its expansion and zone order, with instances starting at i1. Presets stay selected for future trains. Strict zones use a saved mark order; other zones use estimated travel distance.", Kihtli),
            new("Rallies", "Add aetheryte rally stops on entry to numbered instances and selected expansion changes. Choose an aetheryte per zone or let the route planner choose. Stops use existing custom flags, with their usual teleport and removal controls.", Kihtli),
            new("Sharing", "Save presets locally or share them through the server. Selecting a shared preset updates the route for everyone as marks are scouted. Shared presets require server 0.3.26 or later; older plugins still receive the ordered train and rally flags.", Kihtli),
            new("Manual order", "Dragging a mark or expansion pauses automatic ordering and preserves the rest of the route. New scouts append while paused. The pause survives resets and future trains until you reselect a preset; the saved preset is unchanged.", Kihtli),
            new("Travel", "Garlemald route estimates and teleport recommendations account for the required flight north from Tertium to (31.5, 12.7) before heading to a mark.", Kihtli),
            new("Counters", "Fixed Reset shared counts requests losing trigger-mob name casing. Resets remain scoped to the selected world, zone and instance, with stale attempts unable to clear newer counts.", Kihtli),
        ]),
        new("0.5.0.18", "2026-09-14", "Lower window draw overhead, clearer train lists and optional visibility filters.",
        [
            new("Performance", "Reduced draw overhead and memory allocations across the A-rank and S-rank boards, Active Marks, train list and tally. Window and display preferences no longer trigger a full configuration save on each change.", Kihtli),
            new("Train", "Marks are grouped by world, then expansion, with folding and ordering within each world. World names no longer crowd mark names, and Teleport and status buttons have consistent sizing and alignment.", Kihtli),
            new("Reports", "End Train Now scopes reports and cleanup to each world and expansion, preserving unrun worlds and their watches. Fixed routine sighting updates preventing train and scout cleanup after a report was sent. Shared completion requires server 0.3.24 or later.", Kihtli),
            new("S ranks", "Added Hide unmet conditions to /hhs. It hides red condition states while keeping green open windows and yellow opening-soon windows visible. Grey states are unaffected. Off by default.", Kihtli),
            new("Map", "Added Hide occupied spawn points to /hhm and Settings > Map. It hides the nearest matching spawn point while a live mark is shown within two map coordinates. Ambiguous matches stay visible; the point returns when the mark moves away, dies or disappears. Off by default.", Kihtli),
            new("Stability", "Improved map marker handling during map updates and loading transitions, cleanup after failed startup or plugin unload, and speech resource cleanup.", Kihtli),
            new("Sharing", "Added limits for incoming data, queued messages and train imports. Unencrypted sharing connections now require explicit opt-in in settings.", Kihtli),
        ]),
        new("0.5.0.17", "2026-09-13", "Hotfix for map overlay cleanup crashes outside hunt zones.",
        [
            new("Map", "Stops repeated native overlay attachment and teardown in cities and duties, including while the map is closed. The overlay now stays hidden outside hunt zones and returns when needed; its controller owns final cleanup. This addresses the reported frame-update crash in KamiToolKit node-focus cleanup.", Kihtli),
        ]),
        new("0.5.0.16", "2026-09-12", "A tidier hunt workspace, reliable minion tracking and smarter train watches.",
        [
            new("Workspace", "The main /hh window now has Train, S Ranks, Settings and Help tabs. Train controls and reports are available together; shared controls and scouts can be collapsed. Use the Windows menu to open separate boards and tools.", Kihtli, 43),
            new("Detection", "Wait for the current world to resolve before announcing marks, preventing duplicate FOUND messages when zoning.", Kihtli, 38),
            new("Active Marks", "Nearby player counts use the largest fresh observer count instead of alternating between scouts; counts are never added together.", Kihtli, 39),
            new("Travel", "Lifestream travel from a mark now continues to its instance after reaching the correct world and zone, where instance switching is available. Normal clicks still place map flags; use Teleport or Ctrl-click to travel.", Kihtli, 40),
            new("Active Marks", "The window stays visible during travel between data centres after the first login. It remains hidden before the first character login.", Kihtli, 41),
            new("Active Marks", "Added an optional data-centre label alongside the world name in Active Marks settings.", Kihtli, 42),
            new("Map", "Only left-click places map flags. Right-click no longer places and immediately clears a flag; Shift-left-click still toggles a manual mapping exclusion.", Kihtli, 44),
            new("Minions", "Identical SS-event minions keep separate health, combat state, observers and map icons using actor IDs, even when moving or sharing coordinates. Requires server 0.3.23 and updated reporting/viewing plugins; older reports cannot reliably distinguish actors.", Kihtli),
            new("Faloop", "Sniped reports advance the bounded spawn window and reset mapping from receipt of the report. Repeated snapshots preserve new mapping evidence. Requires server 0.3.22 or later.", Kihtli),
            new("Train watches", "S-rank watches now live under Train. Optionally follow eligible spawn windows for train worlds and instances; confirmed mapping supplies the location without guessing. Reminders respect the train world and instance. Enable automation on the client preparing the train; requires server 0.3.22 or later.", Kihtli),
            new("Commands", "The installer lists only /hh commands when Hunt Helper is absent, or /htr commands when it is installed. Available aliases still work; commands owned by another plugin are left alone.", Kihtli),
        ]),
        new("0.5.0.15", "2026-09-11", "Shared manual mapping and the latest beta improvements.",
        [
            new("Mapping", "Shift-click an S-capable map point to toggle a shared exclusion, or click the point count in /hhs to compare coordinates and record a Faloop, Bear or other source. Undo removes only manual evidence. Entries persist per world and instance until the next S kill. Requires server 0.3.21.", Kihtli),
            new("S ranks", "The timer board now uses the same bounded visibility feed as Active Marks and ignores pre-kill sightings when deciding UP. This addresses stale UP states after several instances die.", Kihtli),
            new("Conditions", "Eligible S-rank names and condition countdowns turn yellow under five minutes before the condition opens, then green when open.", Kihtli, 35),
            new("Map", "Map controls move below the map when there is no room above, keeping the title bar draggable.", Kihtli, 36),
            new("Commands", "Every /htr command has an /hh equivalent. Added /hhm for map controls and /hhtally, including config and ipc arguments. /hhna remains the /htra equivalent; /hha opens A-rank timers.", Kihtli),
            new("Trains", "Verified empty trains with no history submit nothing, while unsubmitted Marks Slain history remains reportable. Partial completion preserves unfinished legs and full completion resets scouts. The underlying partial-train feature was contributed by musicmanbowls, with kihtli's safeguards.", "musicmanbowls, kihtli", 37),
        ]),
        new("0.5.0.14", "2026-09-10", "Share manual scout names only when added.",
        [
            new("Scouts", "Manual scout text stays local until you press Enter or Add scout. Typing no longer shares and removes partial names. Both scout editors use the same behavior; remove a committed name explicitly, and add it again to restore it if needed.", Kihtli),
        ]),
        new("0.5.0.13", "2026-09-10", "Reliable scout resets and Faloop confirmation after maintenance.",
        [
            new("Scouting", "Scouting starts paused on login and plugin load. Unpause to join automatic scout credits; paused health and death updates do not add your name. Finishing the last train leg clears all scout credits and removals and pauses scouting. Partial reports keep credits for remaining marks and watches. Update scouts to this version and use server 0.3.20.", Kihtli),
            new("Faloop", "Server 0.3.20 accepts reports for worlds whose maintenance timer rows have no kill time. A later Faloop confirmation can then add its timer to a mark already detected locally, keeping its live health.", Kihtli),
        ]),
        new("0.5.0.12", "2026-09-10", "Align S-rank respawn windows with Faloop.",
        [
            new("Timers", "Corrected maintenance ranges to match Faloop exactly, including fractional hours, and updated differing ARR normal ranges. Existing kill and restart times are preserved. Server 0.3.19 applies the same correction to the admin board.", Kihtli),
            new("Credits", "Corrected beta 11 attribution: musicmanbowls contributed partial train completion in plugin PR #34 and server PR #2; kihtli added the review fixes and reconnect safeguards.", "musicmanbowls, kihtli"),
        ]),
        new("0.5.0.11", "2026-09-09", "Submit completed train legs while keeping the rest scouted.",
        [
            new("Trains", "End Train reports expansions with observed kills and clears their dead marks, keeping unrun legs, live marks, watches and scout credits. Completed history is reconciled after reconnect without losing later spawn cycles. Requires server 0.3.18; shared reporting is refused on older servers. Update all conductors before partial reports.", "musicmanbowls, kihtli"),
        ]),
        new("0.5.0.10", "2026-09-09", "Stabilise Active Marks during brief reporting gaps.",
        [
            new("Active Marks", "Holds mark rows and observer names for up to five seconds after the last received report to reduce flicker with latency or brief visibility gaps. Fresh HP/death reports replace the display immediately. Your own observer alias appears once as You. Map visibility is unchanged.", Kihtli),
        ]),
        new("0.5.0.9", "2026-09-09", "Remove unwanted shared scout credits and inspect their source.",
        [
            new("Scouts", "Shared scout credits now show their first supplier and whether they were automatic or manual. Remove or restore credits in train controls; removed names stay suppressed on the server across reconnects and train resets. Requires server 0.3.16. Older credits have an unknown source.", Kihtli),
        ]),
        new("0.5.0.8", "2026-09-08", "Suppress repeated detection alerts at the edge of range.",
        [
            new("Detection", "Finding the same mark at the same spawn no longer repeats chat, speech or fly-text alerts when it drops in and out of range. Notification memory resets on zone/world/instance changes, observed kills or a different spawn location. Live map visibility still expires normally.", Kihtli),
        ]),
        new("0.5.0.7", "2026-09-08", "Show plugin windows only after character login.",
        [
            new("Windows", "All plugin windows stay hidden at the title screen and character selection. Automatic update notes are checked after login so they are not consumed before you can see them.", Kihtli),
        ]),
        new("0.5.0.6", "2026-09-08", "Restore automatic update release notes.",
        [
            new("Updates", "Fixed automatic release notes skipping testing updates because the fourth version number was ignored. Notes now open once when updating, unless disabled in settings.", Kihtli),
        ]),
        new("0.5.0.5", "2026-09-08", "Maintenance-aware instances, standalone commands and consistent hunt timer windows.",
        [
            new("Maintenance", "Timer boards now show offline worlds and follow Faloop zone instance counts, including newly created instances. Restart clocks use the actual restart time; retired instances remain in history and are hidden from current boards.", Kihtli),
            new("Counters", "Moved S-rank trigger counters and spawn watches out of Scout into their own S Counters tab, with counting preferences under Settings > Counters. Scout now focuses on train reports and scout credits.", Kihtli),
            new("Settings", "Grouped preferences into task-based categories with a compact layout for narrow windows. Sync and tally preferences now live under Settings. Added searchable Help and moved lengthy tutorial tooltips there, keeping mark-specific details and short action labels.", Kihtli),
            new("Standalone", "Removed Hunt Helper IPC emulation and installation checks. /hh, /hht, /hhn, /hhna and /hhc are native HHE shortcuts; HHE IPC and train sharing remain available.", Kihtli),
            new("Timers", "Click S- or A-rank table headers to sort ascending, descending, or restore automatic priority. Dates and numbers sort by their values; unknown values stay last. Right-click headers to choose columns.", Kihtli),
            new("A ranks", "The A-rank timer board shares the S-rank layout, progress bars, colours and time formatting, while retaining world/expansion/instance filters and bounded sniped timers. Active A-ranks use normal row backgrounds, without the S-rank red highlight.", Kihtli),
        ]),

        new("0.5.0.4", "2026-09-07", "Nearby-player estimates through character culling.",
        [
            new("Active Marks", "Nearby-player estimates use native character-manager positions and retain them for the zone session, like Sonar. Culled characters remain counted; estimates reset on world/zone/instance changes or logout. Counts appear in brackets; departed players may remain counted until their position updates or the cache resets.", Kihtli),
        ]),

        new("0.5.0.3", "2026-09-07", "Beta: train completion and clearer hunt windows.",
        [
            new("A ranks", "Sniped marks show the same bounded respawn window as Discord when last-seen-alive evidence is available. Bounds survive server resets/reconnects.", Kihtli),
            new("Train", "End Train resets only after Discord succeeds and the server acknowledges storing the report's kill history. A changed shared train is kept. Requires server 0.3.13; Undo remains available.", Kihtli),
            new("Scouts", "Shared scout credits combine plugin scouting contributors and manually added names. Credits and the train controls can be collapsed at the top of the popout.", Kihtli),
            new("Active Marks", "Compact lines include zone, a scout's current visible player count within 50 yalms, and elapsed Faloop activity time when reported. Unknown counts stay unknown.", Kihtli),
            new("S ranks", "Names are green when timer and predictable conditions are open, grey before the window or when unknown, and red within the window when timed conditions are unmet. Player actions still apply.", Kihtli),
        ]),

        new("0.5.0.2", "2026-09-07", "A-rank cooldowns for returning scouts.",
        [
            new("A ranks", "Retrieve recorded kill times by world and instance when connecting, even with train sharing disabled. Server 0.3.12 retains this history through train resets and restarts; unknown/sniped times remain uncertain.", Kihtli),
        ]),

        new("0.5.0.1", "2026-09-07", "Beta: standalone hunts and private-group sync.",
        [
            new("Sync", "Share train scouting/order, live mark observations, S-rank mapping and personal trigger-kill totals through a private password-protected group server.", Kihtli),
            new("Boards", "Use /hhs for S-rank spawn windows and conditions, /hha for A-rank timers by instance, and /hhsa or /hhv for compact Active Marks with All/S/A/B tabs and live HP.", Kihtli),
            new("Travel", "Optional Lifestream world/aetheryte travel, map-linked spawn alerts and suggested destinations for S-rank spawn attempts.", Kihtli),
            new("Train", "Native report history retains exact kill/sniped evidence and reset recovery.", Kihtli),
            new("Train", "Expansion headings count hunt marks only, without counting custom rally flags. Thanks to musicmanbowls for this fix in PR #32.", MusicManBowls, 31),
            new("Train", "Open next automatically unfolds the next unfinished expansion when a leg finishes. Thanks to musicmanbowls for this fix in PR #32.", MusicManBowls, 30),
        ]),

        new("0.4.0.21", "2026-09-07", "Testing: blue for community reports without live feedback.",
        [
            new("Sightings", "Compact Active Marks lines are blue for Faloop-only reports. Fresh scout observations replace blue with the live status colour; grey is reserved for live reports with unknown combat state.", Kihtli),
        ]),

        new("0.4.0.20", "2026-09-07", "Testing: compact Active Marks overlay.",
        [
            new("Sightings", "Active Marks uses compact coloured lines with HP after the name: green unpulled, orange pulled, red dead. Hover for details, click for a map, and Ctrl-click or right-click for travel. Tabs and filters remain available.", Kihtli),
        ]),

        new("0.4.0.19", "2026-09-07", "Testing: Active Marks with rank tabs and live health.",
        [
            new("Sightings", "Active S Ranks becomes Active Marks (/hhsa or /hhv), with All/S/A/B tabs, live HP, alive/dead and combat state. S-rank community reports, travel and active timers remain available. Configure rank, world/DC, expansion and status filters in Sync settings. Requires server 0.3.11.", Kihtli),
        ]),

        new("0.4.0.18", "2026-09-07", "Testing: shared spawn-trigger kill counters.",
        [
            new("Counters", "Kill counters show the group's personal-kill total in brackets. Counts sync by world and instance, survive reconnects, and have a separate shared reset. Requires server 0.3.10.", Kihtli),
        ]),

        new("0.4.0.17", "2026-09-07", "Testing: travel to S-rank spawn attempts.",
        [
            new("Travel", "Ctrl-click an S-rank name even without a reported location. Travel suggests an aetheryte near remaining candidate spots, or the zone's S-rank spots when mapping is uncertain, respecting aetheryte exclusions.", Kihtli),
        ]),

        new("0.4.0.16", "2026-09-07", "Testing: travel status clears when teleporting ends.",
        [
            new("Travel", "The Teleporting message clears after casting/loading finishes. Travel remains busy during the final teleport, preventing overlapping requests.", Kihtli),
        ]),

        new("0.4.0.15", "2026-09-07", "Testing: one-click S-rank travel and clearer active reports.",
        [
            new("Travel", "World travel retains the aetheryte step through loading and temporary refusals, so one click completes both stages. Ctrl-click a name on /hhs to use the same travel when its location is known.", Kihtli),
            new("S ranks", "Active local and fresh community reports move to the top of /hhs and have a red row background. Recently detected local S ranks no longer echo a duplicate relay alert.", Kihtli),
        ]),

        new("0.4.0.14", "2026-09-07", "Testing: undo is visible wherever you manage the train.",
        [
            new("Train", "Undo reset is shown at the top of the Train tab and popout, even for an empty train. Reset and Clear All now save recovery data consistently. Undo restores locally and turns train sharing off.", Kihtli),
        ]),

        new("0.4.0.13", "2026-09-07", "Testing: native train reports and reliable kill evidence.",
        [
            new("Reports", "Reports use HHE's own train history, preserve worlds, and separate live marks and unknown-time deaths from confirmed kills. Removed dead rows remain in report history across reloads.", Kihtli),
            new("Reports", "Overlapping report submissions are blocked. Posting keeps shared or changed trains intact; an unchanged local train can be cleared with undo available.", Kihtli),
            new("Detection", "First-seen corpses no longer create an exact kill time. A witnessed death requires the same object to have been observed alive.", Kihtli),
            new("Sharing", "Native clipboard codes and version 2 IPC preserve worlds and explicit death/sniped times. Legacy codes without a death timestamp remain unknown. Queued tally kills keep their original world.", Kihtli),
        ]),

        new("0.4.0.12", "2026-09-07", "Testing: shared scouting, S-rank tracking and smoother expansion handovers.",
        [
            new("Train", "Expansion headings count hunt marks only; custom rally flags remain visible without inflating the total.", MusicManBowls, 31),
            new("Train", "Open next automatically unfolds the next unfinished expansion when a leg finishes. It respects route order and Hide dead, and leaves completed legs open. Outstanding custom rally stops still need completing.", MusicManBowls, 30),
            new("Sync", "Share scouting, mark order, observed health and location through a password-protected group server. Shared scouting keeps expansion blocks together, and train resets have a local undo option.", Kihtli),
            new("Map", "Observed A/B ranks eliminate S-rank candidates for the current kill cycle. Possible spots have adjustable gold outlines; a confirmed final candidate is filled gold. Live mark overlays expire when nobody can see the mark.", Kihtli),
            new("S ranks", "Use /hhs for respawn windows, world and expansion filters, configurable columns, spawn-condition tooltips and weather/time countdowns. Use /hhsa for active reports, elapsed active time and optional Lifestream travel.", Kihtli),
            new("Alerts", "Shared S-rank alerts use RELAY with coordinates and map links when known. FOUND and RELAY messages include the world, and alerts wait through loading screens.", Kihtli),
            new("A ranks", "Use /hha for A-rank windows, including separate instance rows. Uninstanced cells are blank and the instance column hides when unnecessary.", Kihtli),
        ]),

        new("0.4.0", "2026-09-06", "The train organises itself, and a sniped mark stops inventing its own kill time.",
        [
            new("Train", "The train can group itself into expansion blocks, keeping scout order inside each one. It sorts the train rather than only redrawing it, so Next Mark, the export code and the report all follow what is on screen. Blocks start in the order the expansions already stand in — ticking the box folds a list into blocks without rearranging it.", Kihtli),
            new("Train", "Drag a block heading to move a whole expansion, or click it to fold that expansion away. A folded block still says how many of its marks are up, and stays folded across a reload.", Kihtli),
            new("Train", "A sniped mark gets its own button beside the dead tick, and its own section in the report. Ticking one dead recorded a kill time nobody witnessed, and so a respawn window that was simply wrong; the window now runs from when it was last seen alive to when the train found it gone, which is wide but true.", Kihtli, 1),
            new("Train", "The Spawned / Didn't Spawn boxes for S-rank watches sit under the train list as well as on the Conductor tab, so they can be ticked from the popout without walking back to a settings tab. Neyoozoteel joins the tracked S ranks, in Yak T'el.", Kihtli, 9),
            new("Train", "Import a train straight from the clipboard, from the popout as well as the Train tab — the window actually open when someone's code turns up. Imports merge; nothing already in the train is overwritten.", Kihtli, 12),
            new("Train", "Export codes carry the world they were scouted on. Two scouts on two worlds sending lists to one conductor had their marks collide on a key that said nothing about where they came from.", Kihtli, 13),
            new("Plugin", "Other plugins can read and add to the train over IPC. When Hunt Helper is not installed its own gates are answered here, with the same names and shapes, so anything already written to integrate with it works unchanged. Settings -> About says which state you are in.", Kihtli, 11),
            new("Map", "A live SS minion is drawn as the S rank it is rather than in the event's own orange, which was the same colour as the spot it stands on — so the two were one dot whichever drew on top.", Kihtli),
        ]),

        new("0.3.0", "2026-09-05", "Marks on the map tell the truth about where they are.",
        [
            new("Map", "Marks are drawn where they actually stand, never snapped to the nearest spawn point. A mark near a point was only near it — up to a couple of map coordinates away — so walking to the dot was walking to the wrong place.", Kihtli, 14),
            new("Map", "Spawn points and marks have their own switches and their own B / A / S filters. Showing only A and S points while still being told about a B rank that has turned up now works.", Kihtli),
            new("Map", "Clicking a spawn point drops the flag on it, for sending people somewhere before anything has spawned there. SS minion spots and the spot the SS mark appears on work the same way.", Kihtli, 10),
            new("Map", "An SS minion that is up is drawn over its spot and larger than it, so a spot with something alive on it no longer looks identical to one still waiting.", Kihtli),
            new("Map", "Marks in different instances, and on different worlds, are told apart properly. Killing one in instance 1 was blanking the live one in instance 2.", Kihtli, 8),
            new("Train", "A mark is ticked off when it dies, whoever killed it. Its health reaching zero is the signal, which carries as far as the game loads the mark at all; the battle log is watched too, but that only reaches as far as the fight. Marking dead ran off the tally alone, which only reports kills you were credited with. NOT YET TESTED at long range — please report whether a mark killed across the zone ticks off.", Kihtli),
            new("Detection", "An SS event only starts on the game's own announcement. Anyone typing \"extraordinarily powerful mark\" in party chat was starting one that had not happened.", Kihtli),
            new("Tally", "Marks Slain filters by B / A / S rank as well as by name. The list is ordered by kills, so picking a rank puts your most-killed mark of it at the top.", Kihtli, 7),
            new("Commands", "/hh, /hht, /hhn, /hhna and /hhc answer here when Hunt Helper is not installed, so the muscle memory carries over. They are left alone if it is.", Kihtli, 4),
            new("Commands", "/htr, /htrt and /htrc close their windows again instead of only opening them.", Kihtli),
        ]),

        new("0.2.1", "2026-09-03", "The icon reaches installed copies, not just the installer list.",
        [
            new("Plugin", "The icon showed in the plugin list and then vanished once installed. The list is drawn from the repository manifest; an installed plugin is drawn from the one bundled in its own zip, and that copy had no icon in it.", Kihtli),
        ]),

        new("0.2.0", "2026-09-03", "Mark names on the map, Hunt Helper's notifications, and counters that count.",
        [
            new("Map", "Every mark that is up carries its name and remaining health beside its dot, counting down as it is pulled. Colour, outline and text size are settable.", Kihtli, 5),
            new("Map", "The detection ring and projected path no longer go missing after a teleport with the map left open.", Kihtli, 2),
            new("Detection", "A mark being spotted can announce itself in chat, as fly text, and out loud, with the message templates, placeholders and colours Hunt Helper uses. Each channel has its own B / A / S switches.", Kihtli, 6),
            new("Counters", "The non-kill trigger counters work again. Forgiven Pedantry, Squonk and Salt and Light were all being matched against a kill line when their real trigger is gathering, discarding or an ability firing, and Gandarewa had no counter at all.", MusicManBowls, 3),
            new("Counters", "Narrow-rift's Wee Ea headcount in Ultima Thule, and Nunyunuwi's no-FATE-failed clock in Southern Thanalan, neither of which fits the chat-line model.", MusicManBowls, 3),
        ]),

        new("0.1.0", "2026-09-03", "First release under the Hunt Helper Evolved name.",
        [
            new("Plugin", "Hunt Train Relay and Hunt Tally merged into one plugin and carried on, plus a substantial amount of new map work. Settings and tallies carry over from both.", MusicManBowls),
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
