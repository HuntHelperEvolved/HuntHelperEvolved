# Hunt Helper Evolved

A hunting plugin for FFXIV, built from two that came before it. It scouts and
records a train with exact kill times, posts a Discord report sorted by the
order marks actually died, draws spawn points, your detection range and SS
event locations on the **in-game map**, counts S-rank trigger mobs, and keeps a
lifetime per-mark kill tally for every character you play.

> **v0.4 — a testing build.** It is not finished, and it is published as a
> testing-only release on purpose: you will not see it in the plugin installer
> unless you have opted into testing builds. Expect rough edges and expect to
> report them.
>
> See [Where this came from](#where-this-came-from) if you are arriving from
> Hunt Train Relay or Hunt Tally; your settings and your tally carry over.

## Install

1. In-game, type `/xlsettings`, go to the **Experimental** tab.
2. Tick **Get plugin testing builds**. Without this the plugin will not appear
   at all — this release is testing-only.
3. Find **Custom Plugin Repositories** near the bottom of the same tab, paste
   this into the empty box and click the **+**:
   `https://raw.githubusercontent.com/HuntHelperEvolved/HuntHelperEvolved/main/repo.json`
4. Click **Save and Close**.
5. Type `/xlplugins`, search for **Hunt Helper Evolved**, and click **Install**.

Updates show up as a normal **Update** button in `/xlplugins`.

If you cannot find it after adding the repository, step 2 is almost certainly
why.

## Commands

| | |
|---|---|
| `/htr` | the main window — Conductor, Train, Scout, Marks Slain, Settings, Tally |
| `/htrt` | the train list, as a popout |
| `/htrc` | the trigger-mob counter popout — also Narrow-rift's Wee Ea headcount in Ultima Thule and Nunyunuwi's no-FATE-failed clock in Southern Thanalan |
| `/htra` | name the closest aetheryte to the next mark |
| `/htrm` | show or hide the control bar above the map |
| `/hunttally` | the kill tally. `/hunttally config` for its settings |

If **Hunt Helper is not installed**, this plugin also answers to its commands,
so you can carry the muscle memory over: `/hh` opens the main window, `/hht` the
train list, `/hhn` moves to the next live mark and flags it, `/hhna` names the
closest aetheryte to it, and `/hhc` opens the counter. They are only claimed
when Hunt Helper is absent — if you still have it installed, it keeps them.

`/hh1`, `/hh2`, `/hh1save`, `/hh2save` and `/hhr` are left alone: they save and
apply Hunt Helper's map-window presets and open its spawn point recorder, and
there is nothing here that does either.

## What's new

The plugin keeps its own release notes and puts them up once after an update,
so you can see what changed and what's worth testing. Only after an update —
never on a fresh install, and never on an ordinary login. **Settings → About →
What's new** reopens them, and the checkbox on that window turns the automatic
one off.

## The map

Everything here draws on the game's own map, not in a separate window, and only
in zones marks actually occur in. A two-row control bar sits above the map and
appears and disappears with it.

**Spawn points.** Every known spawn point in the zone, filtered by rank.

**Marks.** Every mark that's up, drawn slightly larger than a spawn point and
at the position it is actually standing on — not snapped to the nearest spawn
point. A mark near a point is only *near* it, and an SS event's mobs don't
spawn on those points at all.

The two have **separate switches, and separate B / A / S filters**, because they
answer different questions: the points are where a mark *could* be, the marks
are what *is* there. Showing only A and S points while still being told about
a B rank that's turned up is a perfectly ordinary way to hunt.

**Click to flag.** Clicking a spawn point drops the flag on it, for sending
people to a spot before anything has spawned there. SS event spots and the spot
the SS mark itself will appear on are clickable the same way. Turn it off and
the clickable cursor stops appearing too.

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

The circle is in yalms rather than screen pixels, so it stays honest at every
zoom. There's a scale if you want it bigger than life, and a line-width setting.

**SS events.** When the *"minions of an extraordinarily powerful mark"*
announcement goes out, the four minion spots for that zone are marked, along
with a star on the spot the mark itself will spawn. A minion that is actually
up is drawn over its spot and larger than it, so a spot with something alive on
it reads differently from one still waiting. They stay until the mark appears or
you leave the zone. All 18 ShB, EW and DT hunt zones are covered.

## When a mark turns up

Three ways to be told, each with its own B / A / S switches, and all of them
local — nothing is sent to anyone else.

- **Chat**, as a line you write yourself. `<name>`, `<rank>`, `<hpp>` and a
  clickable `<flag>`, plus a dozen game icons like `<goldstar>`.
- **Fly text** on your character, in the channel a crit lands in, which is the
  one thing here you cannot miss while running.
- **Spoken**, on a voice and volume you pick. Windows only — everywhere else
  the settings screen says so, and the other two carry on regardless.

These are Hunt Helper's own placeholders, defaults and colours, so a message
pasted across from it produces the same line.

## The train

Turn on **Tracking this train** on the Conductor tab and the plugin records the
exact moment each mark dies. Nothing posts on its own — **End Train Now** is the
only thing that sends a report, deliberately, because a multi-expansion train
looks "finished" the moment you leave the first leg.

Marks are recorded per world, so the same mark on Mateus and on Goblin are two
marks and can't overwrite each other — and the export code carries the world,
so two scouts on two worlds sending lists to one conductor stay separate.

**Group by expansion** sorts the list into blocks and keeps scout order inside
each one. It sorts the train itself rather than only redrawing it, so Next Mark,
the export code and the report all follow what's on screen. Blocks start in the
order the expansions already stand in, so ticking the box folds an imported list
into blocks without rearranging it. Drag a block heading to move a whole
expansion, or click it to fold that expansion away.

**Import** reads an export code straight off the clipboard, from the popout as
well as the Train tab. Imports merge — nothing already in the train is
overwritten.

**Sniped marks** have their own button beside the dead tick, and their own
section in the report. Ticking one dead recorded a kill time nobody witnessed,
and so a respawn window that was simply wrong. The window now runs from when the
mark was last seen alive to when the train found it gone — wide, but true. Marks
never seen at all stay in Assumed Sniped, with no window to give.

The S-rank watches from the Conductor tab repeat under the train list, so their
Spawned / Didn't Spawn boxes can be ticked from the popout while running.

A mark is ticked off when it dies, whoever killed it — not only when you were
credited with the kill. Its health reaching zero is the signal, which carries as
far as the game loads the mark itself; the battle log is watched too, but that
only reaches as far as the fight. One the group brought down while you were
still running in used to stay lit as though it were up, and the report is built
from this list.

The **Scout** tab posts a report with a Hunt Helper import code and a per-
expansion count of what's up, including what was found already dead.

## The tally

Every mark you get kill credit for, permanently, per character, broken down by
rank and expansion — so the number survives finishing the achievement, which
stops reporting a running total once it's complete. Credit is read from your own
actions rather than guessed from combat state, and A and S ranks are only
counted once the game confirms it rewarded you.

The **Marks Slain** list filters by name and by B/A/S rank, and is ordered by
kills — so picking a rank puts your most-killed mark of that rank at the top.

`/hunttally` opens it. Its settings live on the **Tally** tab of the main window.

## Talking to other plugins

The train is published over Dalamud IPC, so other plugins can read it and add to
it. When **Hunt Helper is not installed** its own gates are answered here —
`HH.GetVersion`, `HH.GetTrainList` and `HH.ImportTrainList`, with the same
signatures and the same record shape — so anything already written to integrate
with Hunt Helper works unchanged. They are left alone if it is installed, since
a call gate is claimed process-wide and taking one it holds would quietly
redirect every plugin asking it for its train.

`HuntHelperEvolved.ApiVersion`, `.GetTrainList` and `.ImportTrainList` are
published either way. **Settings → About** says which state you're in.

## Where this came from

This is a merger and continuation of two plugins, and the map work is modelled
closely on a third:

- **[Hunt Train Relay](https://github.com/MusicManBowls/HuntTrainRelay)** by
  MusicManBowls — the train recording, Discord reports, scouting, trigger-mob
  counters and the first version of the map overlay. This plugin is that one,
  renamed and carried on.
- **Hunt Tally** by kihtli — the lifetime kill counter, now built in rather than
  a separate install talking over IPC.
- **[Hunt Helper](https://github.com/img02/HuntHelper)** by img02 (MIT) — the
  spawn point data, the territory ids, and the map's design, which the range
  circle, projected path, heading line and position dot follow deliberately.
  Hunt Helper is still a fine plugin and this one reads its train over IPC.

SS minion and mark spawn coordinates are from [Faloop](https://faloop.app/).

### Coming from Hunt Train Relay

Uninstall it. Your settings carry over on first load — Dalamud names a config
file after the plugin, so the old file is read once and written out under the
new name. Nothing to export.

### Coming from Hunt Tally

Uninstall it too. The tally still reads and writes `HuntTally.json` exactly
where the standalone plugin kept it, so every character, mark record, kill and
achievement baseline is simply there. If the standalone plugin is still
installed, this one deliberately counts nothing and leaves the file alone rather
than both writing it — you'll get a warning saying so.

## Building

`dotnet build -c Release`. The release artifact is
`bin/Release/HuntHelperEvolved/latest.zip`.

On macOS or Linux run `./build-macos.sh` instead — the Dalamud SDK only finds
`Dalamud.dll` by itself on Windows, and the script points `DALAMUD_HOME` at the
usual XIV on Mac and XIVLauncher.Core locations.

The tally lives in `Tally/`, still in its original `HuntTally` namespace and
still keeping its own config file. That's deliberate: it keeps the merge to a
wiring change, so the code people's existing totals were built by is the same
code.

## Licence and credits

Hunt Helper Evolved is **MIT licensed** — see [LICENSE](LICENSE). It is a joint
project between MusicManBowls, whose Hunt Train Relay it continues, and kihtli,
whose Hunt Tally is built into it.

Third-party notices, including the full MIT licence text for everything this
plugin borrows or redistributes, are in
**[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**.

In short: Hunt Helper's spawn point data, territory ids, mark names and map
design (MIT, © 2022 imaginary-png), and KamiToolKit (MIT), which ships inside
the release archive. SS event coordinates are from Faloop.
