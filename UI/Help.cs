using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private string _helpSearch = string.Empty;
    private static readonly (string Title, string Text)[] HelpTopics =
    [
        ("Getting around",
            "Conductor manages a train and its reports; Train shows the route and import/export controls; Scout prepares a scouting report; S Counters tracks S-rank spawn progress; Marks Slain shows report history. Settings groups preferences by task. Changes save as you edit.\n\n" +
            "Commands: /hh opens the main window; /hht the train; /hhc the trigger counters; /hhs the S-rank timers; /hha the A-rank timers; /hhv or /hhsa Active Marks; /hhtally or /hunttally the lifetime tally. /hhn flags the next live train mark; /hhna names its nearest aetheryte; /hhm or /htrm toggles map controls."),
        ("Train controls, scouting and reports",
            "Scouting starts paused after login. Unpause to join the scout credits and pick up new marks. Pause stops picking up new marks; existing health and death reports still update. Next advances to a live mark without recording a kill. Add flag inserts your map flag as a custom stop. Click a mark to flag it; the chat echo is optional. Spicing marks a scout's plan to prepare a mark before the train arrives. Hide dead affects only the display, not reports.\n\n" +
            "Group by expansion keeps marks together and opens the next unfinished group. Automatic scout names combine with manual credits. Type a manual name, then press Enter or Add scout to share it; drafts stay local. Use Remove to remove a committed name. Completing the last leg clears credits and removed-name suppression and pauses scouting; partial reports keep the remaining train’s credits. Collapse Train controls & scouts to keep the popout compact. Export codes carry the train's worlds, instances and custom stops.\n\n" +
            "End Train submits the report and resets an unchanged train after Discord success and server acknowledgement. Failed submission or concurrent edits retain it. If acknowledgement is lost, inspect state before posting again. Undo reset restores your saved train locally and turns train sharing off; it does not replace your friends' current train."),
        ("Map overlays and S-rank mapping",
            "Spawn points show possible locations; live dots show marks currently seen by you or the group. Rank filters for points and live marks are independent. Click a spawn point to flag it when enabled. Names and HP follow live-mark filters. Remote observations expire after three seconds without updates.\n\n" +
            "Gold outlines indicate possible S-rank spots. A/B sightings rule out their spots for the current reliable S-kill cycle, and the previous S death spot is excluded. A confirmed final point is filled gold. Adjust outline width and colour in Settings > Map.\n\n" +
            "Player guides include the detection circle, projected path, heading line and player dot. The radius scale changes their display. The map control bar toggles these layers quickly. SS overlays show event minion locations and the mark spawn location during an event."),
        ("Timer tables, colours and conditions",
            "Click a data-column header to cycle ascending, descending and automatic order. Right-click headers to choose columns. Unknown values sort last. World and expansion filters accept multiple selections; A-rank instances stay separate. Uninstanced cells are blank.\n\n" +
            "The progress bar measures elapsed respawn-window time, not spawn probability. S-rank names are grey before readiness or when unknown, red when the respawn window is open but timed conditions are unmet, and green when both align. Required player actions still apply. Hover a name for its specific spawn condition. The condition timer counts down to opening or closing.\n\n" +
            "Active S ranks have red rows and appear first in automatic order. Active A ranks retain their usual row background. Available-only filtering retains active S ranks; it does not perform spawn actions for you. An uncertain kill cannot establish a precise window; sniped A-rank bounds use the previous live sighting when known.\n\n" +
            "Record a kill now or a number of minutes ago from the S board. Hold Shift for destructive clear actions. Maintenance records affect timers across the selected world; use them only for a known restart."),
        ("Active Marks",
            "Tabs show All/S/A/B ranks. Green means alive and unpulled, orange pulled, red dead, blue a community report without live feedback, and grey unknown combat state. HP follows the name; ?% means unknown. The line includes zone and a nearby-player count in brackets. Faloop reports also show elapsed active time.\n\n" +
            "Nearby counts estimate players within 50 yalms, including the scout. Cached positions include players no longer rendered; unseen departures can remain counted until the cache resets on zone, world, instance or logout. Counts from multiple scouts are not added together. [?] means no count is available.\n\n" +
            "Click a mark for its map. Ctrl-click travels with Lifestream; right-click opens actions. Filters live in Settings > Sharing."),
        ("Notifications and message templates",
            "Settings > Notifications contains local detection chat, speech and fly-text channels plus group/Faloop S-rank alerts. Rank toggles control local channels. FOUND identifies your detection; RELAY identifies a shared report. Your own detection should not produce a duplicate relay.\n\n" +
            "Community alerts follow the configured data-centre filters and the server's coverage. Historical snapshots do not trigger spawn alerts. The test alert is local and uses example coordinates; it sends no report to the server.\n\n" +
            "Message placeholders: <name>, <rank>, <hpp> (health) and <flag> (map link). Icons: <goldstar>, <silverstar>, <warning>, <nocircle>, <alarm>, <notoriousmonster>, <exclamationrectangle>, <priorityworld>, <elementallevel>, <fanfestival>, <controllerbutton0> and <controllerbutton1>. Speech reads name, rank and health while omitting flags/icons, and requires an available system voice. Brief hover labels remain on icon controls; live status and mark-specific details remain available where useful."),
        ("Travel",
            "Lifestream handles world and aetheryte travel from Active Marks and Ctrl-clicking S-rank names. If no exact position is known, the S board chooses a destination suitable for a spawn attempt. Select the instance yourself on arrival.\n\n" +
            "Settings > Travel excludes aetherytes from routing. Teleport also drops the map flag is configured with train actions. Unavailable Lifestream or a missing allowed destination prevents integrated travel."),
        ("Sharing, connection and privacy",
            "Enter your private group's server URL, password and display alias in Settings > Sharing. Blank aliases use Anonymous. Joining loads the server train; use Upload saved local marks/watches explicitly when needed. Offline shared-row edits are replaced by server state on reconnect.\n\n" +
            "Train sharing includes marks, order, dead state, flags and spicing. Everyone sharing the train can edit or reset it. Sightings share live positions/health and mapping evidence. Witnessed S kills update the group's timer. Community-source credentials are configured on the server.\n\n" +
            "Connection status, online scouts and feed status appear beside connection controls. Do not include passwords, webhook URLs or private character information in public logs or screenshots."),
        ("Discord setup",
            "Create a webhook in Discord through Channel Settings > Integrations > Webhooks, then add its URL in Settings > Discord. Disabled entries remain saved but receive no posts. Send test message posts to enabled entries. Scout reports include a train import code; completed-train reports include kill history and timer bounds."),
        ("Trigger counters and lifetime tally",
            "The S Counters tab contains trigger-mob totals, world selection and current-zone spawn watches, including Narrow-rift and Nunyunuwi. Settings > Counters controls kill counting. Trigger counters measure progress for a spawn attempt. The group total appears in brackets after your total. Shared counts use personal contributions so nearby observers do not double-count. Local Reset affects only you; Reset shared starts a new group attempt and discards pending contributions for the old attempt.\n\n" +
            "The lifetime tally is separate and stored per character. Settings > Tally selects ranks, kill-credit detection and reward confirmation. Damage-based credit follows your actions but does not prove sufficient contribution for rewards. If unavailable, combat-based detection is used; strict credit also checks targeting and the mark’s combat state. Reward confirmation waits for the game’s contribution message; B ranks are exempt. Watching distance controls when tracking starts. IPC reporting options affect what other plugins receive.\n\n" +
            "Achievement seeding imports cumulative totals without per-mark detail. Review baseline and drift before applying corrections; achievement matching uses names and ranks. The detail-log limit removes old timestamped entries, not lifetime totals. Tally reset has its own confirmation. A separate installed Hunt Tally disables the built-in tally until removed and HHE reloaded."),
    ];

    private void DrawHelpPage()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##Help search", "Search UI help…", ref _helpSearch, 100);
        if (ImGui.BeginChild("Help topics", new Vector2(0, 0), false))
        {
            var query = _helpSearch.Trim();
            var found = false;
            foreach (var (title, text) in HelpTopics)
            {
                if (query.Length > 0 && !title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !text.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (query.Length > 0)
                {
                    DrawSettingsHeading(title);
                    ImGui.TextWrapped(text);
                    ImGui.Spacing();
                }
                else if (ImGui.CollapsingHeader(title))
                {
                    ImGui.TextWrapped(text);
                    ImGui.Spacing();
                }
            }
            if (!found) ImGui.TextDisabled("No matching help topics.");
        }
        ImGui.EndChild();
    }
}
