# Hunt Helper Evolved

A hunting plugin for FFXIV, built from two that came before it. It scouts and
records a train with exact kill times, posts a Discord report sorted by the
order marks actually died, draws spawn points, your detection range and SS
event locations on the **in-game map**, counts S-rank trigger mobs, and keeps a
lifetime per-mark kill tally for every character you play.

> **v0.2 — a testing build.** It is not finished, and it is published as a
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
marks and can't overwrite each other.

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
