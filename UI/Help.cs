using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    private string _helpSearch = string.Empty;
    // Keep each topic focused on a task, its controls and essential caveats.
    private static readonly (string Title, string Text)[] HelpTopics =
    [
        ("Getting started and commands",
            "Open /hh for Train, Active Marks and A/S-rank timers. Search Settings to find an option; changes save automatically. Use /htr if /hh is already in use.\n\n/hht: train\n/hha: A-rank timers\n/hhs: S-rank timers\n/hhv: Active Marks\n/hhc: trigger counters\n/hhtally: lifetime tally\n/hhn: flag next mark\n/hhna: next aetheryte\n/hhm: map controls"),
        ("Scouting and following a train",
            "Scouting starts paused after login. Press play in Train or /hht to collect marks. Pausing stops new rows; existing marks still update. Nearby players estimates other players within 50 yalms of you.\n\nFollow controls automatic flags and chat after the current mark dies. Use /hht on or /hht off to toggle it. Adding scouted marks stays quiet; snipes advance silently. Click a mark or use Next mark to flag it yourself."),
        ("Editing marks and planning routes",
            "The check button records a witnessed kill now; undo restores the mark. Crosshairs marks it sniped. Hold Ctrl to remove a row. Hide dead only changes the view; Remove Dead keeps report history.\n\nChoose a route preset in Plan. Dragging rows pauses its ordering; reselect the preset to resume. Calculate rallies updates stops for a paused preset and restores removed stops where living marks remain. Plan also holds scout credits, import/export and Add Flag. Imports merge with the train."),
        ("Discord reports and scout notes",
            "Add a webhook under Settings > Discord. In Train > Reports, choose Scouting report or Train completion, review the preview, then hold Shift to send.\n\nCompletion clears reported dead marks after successful delivery; shared trains also wait for server confirmation. Unfinished marks remain. If delivery partly succeeds or confirmation is lost, check Discord and the train before retrying.\n\nScout notes stay local for this session until sent. Failures keep the draft; Previous note lets you reuse a note from a replaced train. Add scout credits in Plan."),
        ("Reading completion reports",
            "Respawn ranges show the earliest opening and latest cap for each world and expansion; individual marks have their own windows. Bear-confirmed kills inside that range share the summary, with separate counts. Other Bear kills and snipes keep individual details.\n\nMissing / not seen means no train record, not a confirmed kill. Individual kill history shows each mark’s timing and source."),
        ("Resetting or recovering a train",
            "In Plan > Route tools & recovery, hold Shift and click Reset train to clear it without posting. Shared resets affect the group. Undo reset restores your local copy and turns train sharing off."),
        ("Currency messages",
            "Currency estimates rewards from living A-ranks. Its button glows red when your balance plus the remaining rewards would exceed a holding cap; hover for details. Warnings assume full credit on eligible jobs and account for weekly allowance.\n\nChoose Amount gained or Starting balance for the copied message. Copy text, Party and Shout copy without sending. Templates use $c for currencies and $n for living marks, and save automatically."),
        ("A-rank timers and colours",
            "In /hha, UP means seen alive; READY means the timer reached its maximum, not a confirmed spawn. Green is UP in the train, blue is UP outside it or READY, yellow is an open window, and red is cooldown or a snipe with no known opening time. Unknown timers stay neutral.\n\nLast known location flags saved coordinates. Not in train list hides matching rows, including dead ones. Witnessed kills update timers even outside trains; finding a corpse alone gives no exact kill time."),
        ("Recording a snipe or correcting its time",
            "Use Sniped in /hha for the selected world and instance. Enter minutes since a known kill and choose Record known time, or choose Unknown time — found missing now. An unknown earliest bound shows Ready by.\n\nReports save locally. To share, enable The train in Sharing and check for server acceptance. Resubmit offline reports after connecting. Existing train rows stay sniped. For the same spawn, Bear kills replace manual snipes and witnessed plugin kills take priority. A later live sighting restores UP. Bear kills do not count as witnessed train kills."),
        ("S-rank windows and conditions",
            "The percentage measures elapsed respawn window time, not spawn probability. Hover Conditions for requirements; required kills, gathering and player actions still apply. Green means timer and timed conditions align, red means conditions are unmet within the window, and grey means not ready or unknown.\n\nWhile connected, use Record to share a kill now or minutes ago. Shift-click clear removes the group’s recorded kill. Click Points for mapping; right-click a column header to choose columns."),
        ("Map overlays and spawn mapping",
            "Use /hhm or Settings > Map & overlays to choose layers. Points are possible locations; live dots show sightings. Gold outlines mark S-rank candidates; a filled gold point is confirmed. Dim points for found A-ranks helps locate remaining marks while scouting.\n\nAlt-left-click flags a point when enabled. Hold Ctrl to hide the overlay. Shift-click toggles a shared manual exclusion, also available in S-ranks > Mapping. Exclusions apply to that world, instance and kill cycle, resetting on the next S kill. Undoing a manual exclusion preserves automatic evidence. A/B marks first found fighting only eliminate clearly matched nearby points. Moving during combat does not change a matched point."),
        ("S-rank train watches",
            "Open Plan > Train watches, or Train watch in an eligible S-rank’s details. Enable automation on the client preparing the train. Turn it off before removing an automatic watch manually.\n\nRecord Spawned / Didn’t Spawn under the route. Reminders use confirmed mapped points in the train’s world and instance. Adjust them in Settings > Train."),
        ("Active Marks and filters",
            "In /hhv, click a mark for its map or right-click for actions. Green is unpulled, amber pulled, red dead, blue a community report and grey unknown. ?% means unknown HP; ~HP is an older Bear report. Nearby player counts are estimates; local counts drop players unseen for 5 seconds.\n\nFilters match any enabled world/DC, rank and expansion rule; no enabled rules means an empty list. Current World and Current DC follow your travels. Save named presets to reuse filters. Notification filters are separate."),
        ("Notifications and templates",
            "Settings > Notifications controls local chat, speech, fly text and shared S-rank alerts. FOUND is your detection; RELAY is a shared report. Set RELAY world/DC rules independently from Active Marks, or use Copy /hhv S-rank rules.\n\nTemplates support <name>, <rank>, <hpp> and <flag>. Test chat format previews a local example. Speech needs an available system voice. Bongo sounds can be muted separately."),
        ("Travelling to marks",
            "Install Lifestream to travel by Ctrl-clicking an A/S-rank or Active Marks name. It selects the world, an eligible aetheryte and reported instance. Without coordinates, the destination is approximate.\n\nExclude aetherytes in Settings > Travel. A train row’s travel icon teleports; Next Aetheryte only announces and copies the destination."),
        ("Connecting and sharing",
            "Enter your organiser’s server URL, password and display alias in Settings > Sharing, enable the connection and choose what to share. Everyone sharing the train can edit or reset it.\n\nJoining or reconnecting loads the server train and replaces offline shared-row edits. Use Upload saved local marks/watches for local-only records. The Connection control shows feed status and coverage. Keep passwords and webhook URLs private."),
        ("Trigger counters and lifetime tally",
            "Use /hhc for current-zone counters, or S-ranks > Counters for a selected world and instance. Mine counts personal events, Nearby local observations and Group shared contributions. Reset group starts a new shared attempt; counter resets do not affect lifetime totals.\n\nOpen /hhtally for per-character kill credit; Marks > Export filtered CSV exports the list. Settings > Tally has credit and achievement options. Reward confirmation checks the game’s contribution message. Achievement totals lack per-mark detail. If standalone Hunt Tally is installed, remove it and reload HHE to enable the built-in tally."),
        ("Resetting preferences and appearance",
            "Settings > Reset restores a category or all preferences. Trains, presets, counter totals and lifetime tallies are kept. Resetting Sharing or all preferences disconnects sync; saved connections have separate clear controls.\n\nSettings > Appearance controls theme and default opacity. Each window’s title-bar menu offers opacity, pinning and click-through; reset its opacity to use the shared default."),
    ];

    private void DrawHelpPage()
    {
        if (ImGui.Button("Release notes")) _releaseNotesVisible = true;
        ImGui.SameLine();
        ImGui.TextDisabled($"Version {ReleaseNotes.CurrentVersion}");
        ImGui.Spacing();
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
