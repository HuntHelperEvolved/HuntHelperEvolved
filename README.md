# Hunt Helper Evolved

A hunting plugin for FFXIV, built from two that came before it. It scouts and
records a train with exact kill times, posts a Discord report with respawn
summaries and sniped/missing-mark details, draws spawn points, your detection range and SS
event locations on the **in-game map**, counts S-rank trigger mobs, and keeps a
lifetime per-mark kill tally for every character you play.

> **v0.5 beta.** This release appears in the plugin installer only when testing
> builds are enabled.
>
> See [Where this came from](#where-this-came-from) if you are arriving from
> Hunt Train Relay or Hunt Tally; your settings and your tally carry over.

## Install

1. In-game, type `/xlsettings`, go to the **Experimental** tab.
2. Tick **Get plugin testing builds**. Without this the plugin will not appear
   at all. This release is testing-only.
3. Find **Custom Plugin Repositories** near the bottom of the same tab, paste
   this into the empty box and click the **+**:
   `https://raw.githubusercontent.com/HuntHelperEvolved/HuntHelperEvolved/main/repo.json`
4. Click **Save and Close**.
5. Type `/xlplugins`, search for **Hunt Helper Evolved**, and click **Install**.

This beta uses the existing main repository feed and remains testing-exclusive.
If you already use that feed, update normally in `/xlplugins`; no repository change
is needed. Keep a backup of your plugin configuration before updating.
Hunt Helper is not required.

If it does not appear, first check that testing builds are enabled in step 2.

## Commands

| Command | Action |
|---|---|
| `/hh` or `/htr` | the main window: Train, S Ranks, Settings and Help |
| `/hht` or `/htrt` | the train workspace as a popout: Route, Reports and Setup |
| `/hhc` or `/htrc` | the trigger-mob counter popout, including Narrow-rift's Wee Ea headcount and Nunyunuwi's no-FATE-failed clock |
| `/hhn` or `/htrn` | move to the next live mark and flag it |
| `/hhna` or `/htra` | name the closest aetheryte to the next mark |
| `/hhm` or `/htrm` | show or hide the control bar above the map |
| `/hhs` or `/htrs` | the S-rank board: windows, kill times and spawn points, shared through sync |
| `/hhtally` or `/hunttally` | the kill tally. `/hhtally config` (or `/hunttally config`) for its settings |

A shortcut already held by another plugin is left alone.

Manual S-rank mapping: **Shift-click** an S-capable map point to toggle a shared
exclusion, or click the point count in `/hhs` to compare coordinates and select a
source (Manual, Faloop, Bear or Other). Requires a server with manual-mapping
support. Entries reset on the next S kill; undo leaves automatic evidence intact.

## Settings and help

Settings uses a category sidebar, or a dropdown in narrow windows: Train, S Ranks, Map,
Notifications, Travel, Sharing, Active Marks, Discord and Tally. The **Help** tab has
searchable explanations of the controls, timer colours, mapping, sharing and
kill credit. Hover details focus on live information and short action labels.

## Release notes

See [GitHub releases](https://github.com/HuntHelperEvolved/HuntHelperEvolved/releases)
or **Settings → About → What's new** in game for patch notes.

## The map

Everything here draws on the game's own map, not in a separate window, and only
in zones marks actually occur in. A two-row control bar sits above the map and
appears and disappears with it.

**Spawn points.** Every known spawn point in the zone, filtered by rank.

**Marks.** Every mark that's up, drawn slightly larger than a spawn point and
at its observed position, without snapping to the nearest spawn
point. A mark near a point is only *near* it, and an SS event's mobs don't
spawn on those points at all.

The two have **separate switches, and separate B / A / S filters**, because they
answer different questions: the points are where a mark *could* be, the marks
are what *is* there. Showing only A and S points while still being told about
a B rank that's turned up is a perfectly ordinary way to hunt.

**Ctrl-click to flag.** Hold Ctrl and left-click a spawn point to drop the flag
on it, for sending people to a spot before anything has spawned there. SS event
spots and the spot the SS mark itself will appear on work the same way. Ordinary
clicks do not place a flag. Shift-click still toggles manual S-rank exclusions
when the server supports them.

**Mark names.** Every mark that is actually up gets its name and remaining
health written beside its dot, so a glance at the map says which are still
untouched and which somebody is already pulling. The health keeps counting down
while you watch. Colour, outline and text size are all settable, and the
**Names** toggle on the control bar turns the lot off.

**Around you.** Four pieces, each with its own toggle and colour, which
together reproduce Hunt Helper's map:

- a **range circle** at the real detection radius, two map coordinates
- a **projected path**, the swathe ahead that your range will sweep
- a **heading line** out to the edge of the circle
- a **position dot** on exactly where you are

The circle uses yalms, so it scales with the map zoom. You can adjust its scale
and line width.

**SS events.** When the *"minions of an extraordinarily powerful mark"*
announcement goes out, the four minion spots for that zone are marked, along
with a star on the spot the mark itself will spawn. A minion that is actually
up is drawn over its spot and larger than it, so a spot with something alive on
it reads differently from one still waiting. They stay until the mark appears or
you leave the zone. All 18 ShB, EW and DT hunt zones are covered.

## When a mark turns up

Three ways to be told, each with its own B / A / S switches, and all of them
local. Nothing is sent to anyone else.

- **Chat**, as a line you write yourself. `<name>`, `<rank>`, `<hpp>` and a
  clickable `<flag>`, plus a dozen game icons like `<goldstar>`.
- **Fly text** on your character, in the same channel as critical-hit text.
- **Spoken**, on a voice and volume you pick. Windows only; everywhere else
  the settings screen says so, and the other two carry on regardless.

These are Hunt Helper's own placeholders, defaults and colours, so a message
pasted across from it produces the same line.

## The train

The main **Train** tab and `/hht` popout share three pages: **Route**, **Reports**
and **Setup**, with a pause/resume icon at the right of the page tabs. **Next
Mark** and **Next Aetheryte** appear below the tabs only on **Route**, alongside
local/shared and preset context above compact, single-line mark rows.
Last-seen ages use concise brackets, such as `(5m)` or `(1h 12m)`.
Switching pages does not change the train or recording state.

**Route presets** save a conductor's expansion and zone order, optional strict
mark order, and rally preferences for future trains. Choose a preset in
**/hht > Setup** before scouting to organise incoming marks.
Dragging a mark or expansion pauses automatic ordering until a preset is
reselected. Shared presets require server **0.3.26 or later**; older plugins
receive the same train order and ordinary rally flags. See the
[train preset guide](docs/train-presets.md) for setup and routing details.

Use the **pause/resume icon** at the top right to control whether new marks
join the train. Existing train rows continue recording observed deaths
while paused. Enable **Settings > Sharing > The train** to share additions and
automatic scout credit with the group. Scouting starts paused after login.
Leaving a zone does not post a report, so a multi-expansion train can continue
across legs. Reports are sent explicitly from **Reports** with Shift-click.

**Next Mark** selects, flags and announces the next live mark without recording
a kill. **Next Aetheryte** announces and copies the next destination without
teleporting or moving the flag. A row's **TP** button teleports to its nearest
eligible aetheryte; travelling to a custom rally stop completes that stop after
a short delay.

Marks are recorded per world, so the same mark on Mateus and on Goblin are two
marks and can't overwrite each other. The export code carries the world,
so two scouts on two worlds sending lists to one conductor stay separate.

**Group expansions within worlds**, in Setup, sorts the list into blocks and keeps scout order inside
each one. It sorts the train itself rather than only redrawing it, so Next Mark
and the export code follow what's on screen. Blocks start in the
order the expansions already stand in, so ticking the box folds an imported list
into blocks without rearranging it. Drag a block heading to move a whole
expansion, or click it to fold that expansion away.

**Import from Clipboard**, **Copy Export Code** and **Add Flag** are in Setup,
in both train views. Imports merge; nothing already in the train is overwritten.
Hide dead changes only the display. Setup's **Remove Dead** removes dead route
rows while keeping their report history. Expansion totals exclude rally stops
and retain recorded counts when dead rows are hidden.

**Scouting reports** live under **Reports > Scouting report**. They summarize
last recorded state by world and expansion using compact remaining/recorded
counts such as **10/12**, distinguishing marked snipes,
witnessed kills and unknown death times. **Not recorded** means a roster name
is missing from the retained list; it does not prove the mark is absent in an
instance or that every instance was searched.

Add an optional note of up to **256 Unicode code points** beside the preview.
Notes stay local until explicitly sent, are shared between both train views,
and last for the current plugin session. Closing a window or changing page
keeps the draft. A successful send clears only the submitted draft; failures,
partial delivery and edits made during sending preserve unsent work. Resetting
or replacing the train detaches its note for explicit reuse or discard. This
includes loading a shared snapshot when joining or reconnecting; an old note
is not silently sent with a new train. Undo can restore a reset draft without
replacing newer edits.

The intact import code comes first, followed by the scouting summary,
exceptions, notes and credits. These share one card in one Discord message when they fit;
otherwise the code is the first message and the full report is the second.
The preview shows the outgoing message count per destination. If either part
exceeds its own Discord limit, sending is blocked before anything is posted;
Copy Export Code remains available in Setup. Sending refreshes the latest
train snapshot. While posting, the
captured report remains visible and subsequent note edits belong to the next
report. The Reports page in both train views shows webhook progress and results;
Route and Setup reserve no space for those messages.

**Completed-train reports** summarize observed kills by world and expansion.
Each overall respawn range runs from the earliest individual window opening to
the latest individual cap; it does not mean every mark respawns together.
Choose **Reports > Train completion**, review the content, then hold Shift and
click **Send & finish completed legs**. Reported dead marks clear only after
Discord success and, for shared trains, server acknowledgement; unfinished
marks remain. Individual kill records are available under **Individual kill
history**. Failed submission or concurrent changes keep the train. Check the
shared state before retrying after an acknowledgement timeout.

**Sniped marks** use the crosshairs button beside the witnessed-kill check button.
The check button records a kill now or restores a dead mark. Crosshairs marks a
mark found gone; clearing that evidence leaves it dead with an unknown kill
time. These remain separate report states.
Their individual respawn window uses the interval between the last live sighting
and when the train found them missing. Roster marks not recorded on this train
appear under **Missing / not seen this train**, without an asserted respawn time.
Unfinished marks, unknown kill times and S-rank checks retain their own details.

Manage S-rank watches in **Setup > S-rank watch setup**. Their Spawned / Didn't
Spawn boxes remain under the route, including in the popout. Hold **Ctrl** and
click a row's **X** to remove it. With **Show spicing markers** enabled, right-click
the mark row and choose **Being spiced**; spiced marks remain visibly marked.

A mark is ticked off when it dies, whoever killed it, even if you were not
credited with the kill. Its health reaching zero is the signal, which carries as
far as the game loads the mark itself; the battle log is watched too, but that
only reaches as far as the fight. One the group brought down while you were
still running in used to stay lit as though it were up, and the report is built
from this list.

**Setup > Recovery** contains Shift-click **Reset train**, which posts nothing,
and **Undo reset**. Undo restores the saved train locally and switches train
sharing off, leaving the group's train unchanged. A recovery notice also
appears after a reset. Conflicting edits and reset actions are disabled during
report posting; Next Mark and Next Aetheryte remain usable on Route.

## The tally

Every mark you get kill credit for, permanently, per character, broken down by
rank and expansion. The number survives finishing the achievement, which
stops reporting a running total once it's complete. Credit is read from your own
actions rather than guessed from combat state, and A and S ranks are only
counted once the game confirms it rewarded you.

The **Marks Slain** list filters by name and by B/A/S rank, and is ordered by
kills, so picking a rank puts your most-killed mark of that rank at the top.

`/hunttally` opens it. Its settings live under **Settings → Tally**.

## Sharing with a group

The features above work locally. To share hunt data, one group member runs a
**sync server** and everyone enters its URL and password in **Settings → Sharing**.
Anyone with those details can join and edit the group's shared hunts.

The server is its own project:
[HuntHelperEvolved/HuntHelperEvolvedServer](https://github.com/HuntHelperEvolved/HuntHelperEvolvedServer).
It is private and requires maintainer access. Testers receive a server URL and
group password separately; neither is bundled in this plugin or its installer feed.

What sync does, each with its own switch:

- **One train.** Every mark any member scouts lands in one list, in one
  order. Ticking a mark dead, dragging it, spicing it, adding a custom flag
  or removing a row is shared with connected members. Two scouts can
  split an expansion and the conductor watches the whole thing assemble.
  Reset and Clear All empty it for everyone, and say so.
- **Live marks.** While a member can see a mark, everyone sees where it is
  and how much health it has, on the in-game map, drawn like their own with
  who saw it in the tooltip. Removed when the last observer loses sight of it; missing heartbeats expire after three seconds.
- **S-rank clocks.** When an S dies in front of any member, the plugin reports
  the exact moment and everyone's window opens on time. Kills can also be
  entered by hand on the S-rank board, and the server can read Faloop's
  timers for your data centre through a live feed with six-hour reconciliation, so the board is full even
  when nobody in the group was at the kill. Observed reports win for the same cycle; a plausible later Faloop death
  advances the timer. Conflicts remain visible. Faloop is experimental and off by default.
- **Spawn point elimination.** An S cannot spawn where an A or B has spawned
  since it last died, nor twice running on its previous spawn point. Members'
  sightings rule points out as they scout; the map colours what is left,
  and the board counts it.

The **S-rank board** (`/htrs`) is the in-game version of what the trackers
show: every timed S on a world, whether it is up, in cooldown, in its window
(and how far through, as a percentage) or ready for its spawn conditions, when the
window opens and closes in your local time, who last saw it, and how many
spawn points are still possible. ARR S ranks have their own timers; everything
since Heavensward is 84 to 132 hours, or 50 to 80 after maintenance, which the
board handles from an explicit maintenance report or Faloop restart timeline.
The percentage measures elapsed window time, not a confirmed spawn probability.

Joining or reconnecting uses the server train. Local-only rows are saved in
configuration for explicit upload from Settings → Sharing. Shared-row edits made while
offline are replaced by server state. Blank display aliases send Anonymous.
All password holders can edit, reorder and clear the shared train.

Sync uses protocol 4; shared train presets require server 0.3.26 or newer.

## Talking to other plugins

HHE publishes its own Dalamud IPC endpoints: `HuntHelperEvolved.ApiVersion`,
`.GetTrainListV2` and `.ImportTrainListV2`. The V2 records include world identity
and observed death/sniped timestamps. The original HHE `.GetTrainList` and
`.ImportTrainList` endpoints remain available for existing HHE consumers.
Hunt Helper's `HH.*` endpoints are no longer registered.

## Where this came from

This is a merger and continuation of two plugins, and the map work is modelled
closely on a third:

- **[Hunt Train Relay](https://github.com/MusicManBowls/HuntTrainRelay)** by
  MusicManBowls: the train recording, Discord reports, scouting, trigger-mob
  counters and the first version of the map overlay. This plugin is that one,
  renamed and carried on.
- **Hunt Tally** by kihtli: the lifetime kill counter, now built in rather than
  a separate install talking over IPC.
- **[Hunt Helper](https://github.com/img02/HuntHelper)** by img02 (MIT): the
  spawn point data, the territory ids, and the map's design, which the range
  circle, projected path, heading line and position dot follow deliberately.
  Its data and design contributions are credited in the third-party notices.

SS minion and mark spawn coordinates are from [Faloop](https://faloop.app/).

### Coming from Hunt Train Relay

Uninstall it. Your settings carry over on first load. Dalamud names a config
file after the plugin, so the old file is read once and written out under the
new name. Nothing to export.

### Coming from Hunt Tally

Uninstall it too. The tally still reads and writes `HuntTally.json` exactly
where the standalone plugin kept it, so every character, mark record, kill and
achievement baseline is simply there. If the standalone plugin is still
installed, HHE shows a warning and leaves counting and file writes to Hunt Tally
to prevent competing saves from losing counts.

## Building

`dotnet build -c Release`. The release artifact is
`bin/Release/HuntHelperEvolved/latest.zip`.

On macOS or Linux run `./scripts/build-macos.sh` instead. The Dalamud SDK only finds
`Dalamud.dll` by itself on Windows, and the script points `DALAMUD_HOME` at the
usual XIV on Mac and XIVLauncher.Core locations.

The tally lives in `Tally/`, retains its `HuntTally` namespace and uses the
existing `HuntTally.json` config file.

## Licence and credits

Hunt Helper Evolved is **MIT licensed**. See [LICENSE](LICENSE). It is a joint
project between MusicManBowls, whose Hunt Train Relay it continues, and kihtli,
whose Hunt Tally is built into it.

Third-party notices, including the full MIT licence text for everything this
plugin borrows or redistributes, are in
**[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**.
