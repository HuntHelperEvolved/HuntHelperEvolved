# Testing checklist

Use the testing feed in [README.md](README.md#install). Back up the plugin
configuration before testing. Build and automated test instructions are in the
[development guide](docs/DEVELOPMENT.md).

## Window visibility

- Load the plugin at the title screen: no HHE windows should appear. Log into a
  character: enabled update notes should open once for a newer version.
- Logout with windows open: they should hide until login. Reloading the same
  version should not reopen automatic update notes.

## Settings and help

- Check Scout contains report/credit controls and S Counters contains world selection,
  spawn watches, local resets and shared counts. Verify `/hhc` still opens the popout.
- Visit each settings category at normal and narrow window sizes, with UI scaling.
  Check scrolling, category selection, template editors and saving after reload.
- Verify Active Marks > Filters/settings opens Sharing, and `/hunttally config`
  opens Tally. Connection controls, notification tests and Discord tests should
  remain reachable in their respective categories.
- Search Help for colours, mapping, reset, travel and tally. Clear the search and
  check topic expansion. Mark-specific spawn conditions and live details should
  remain available on hover without the former tutorial paragraphs.

## Maintenance and instances

- With server 0.3.15+, check offline worlds are grey and crossed out and excluded
  from Available only and Active Marks. Restart clocks continue while offline.
- Verify Central Shroud shows I1/I2 without the old uninstanced row. Future count
  changes should follow server metadata without another plugin update.
- Check world reopening clears the offline display after metadata refresh.

## Timer windows

- In `/hhs` and `/hha`, sort each visible data column in both directions, then
  clear sorting to restore automatic order. Check numeric values and dates sort
  correctly, with unknown values last. Check column visibility after reopening.
- Compare A-rank sniped window bounds against Discord, including after train reset
  and reconnect. Unknown lower bounds must remain unknown. Join after a completed
  train with empty local history and train sharing disabled; history should load.
- Check multiple world/expansion filters, progress bars and instance separation.
  Uninstanced cells are blank and an unused instance column disappears. A-rank
  rows keep their normal background when a mark is up.
- S-rank names are grey before the timer or when unknown, red in an open respawn
  window outside timed conditions, and green when both align. Active S ranks
  retain their highlight. Manual spawn actions are still required.

## Standalone commands and IPC

- Check `/hh`, `/hht`, `/hhn`, `/hhna` and `/hhc` without Hunt Helper installed.
  Repeat with it installed but disabled; HHE shortcuts should still work.
- If another plugin holds a shortcut, HHE should leave it alone, including when
  HHE unloads. The `/htr` commands remain available.
- IPC consumers should use `HuntHelperEvolved.*`; verify V2 train reads/imports.
  HHE no longer provides Hunt Helper's `HH.*` endpoints.

## Scout credits

- Add a manual credit from one client and check its first supplier and source
  in train controls and Admin > Scout credits. Remove it from another client.
- Reconnect, restart the server, and reset the train: a removed name must remain
  suppressed. Restore explicitly to allow it again. Older clients must not be
  able to re-add it through their saved manual lists.
- Existing migrated credits should say Legacy / unknown.

## Shared trains and mapping

- Split scouting between two users; check order, expansion grouping and automatic
  opening of the next unfinished expansion. Combine automatic scout names with
  manual credits without duplicates; check reconnect and clearing with the train.
- Collapse and reopen train controls/scouts, including after plugin reload. The
  train list and End Train footer should remain usable.
- End Train should reset an unchanged train after successful Discord submission
  and server acknowledgement. Failed Discord, an old/unavailable server, or a
  concurrent train edit must retain it. A lost acknowledgement can leave an
  already-reset train: inspect state before reposting Discord.
- Use Undo after reset. It restores locally and disables train sharing for that
  user, protecting everyone else's train from the restored copy.
- Scout A/B marks and check mapping excludes their spots in the reliable S-rank
  kill cycle. Check possible-spot outlines and the final confirmed spot.
- Compare personal and group kill counts when killing separately and together.
  Nearby observers must not double-count. Local Reset stays local; Reset shared
  starts a new group attempt.

## Detection debounce

- Cross render range repeatedly around the same mark: chat, speech and fly text
  should announce only once, while map icons still disappear immediately.
- Change zone/world/instance and return, observe a kill then respawn, or find the
  mark at a different spawn: the next detection should announce again.
- A pulled mark moving away from its spawn must not trigger a repeated find.

## Active marks, alerts and travel

- In `/hhsa` or `/hhv`, check All/S/A/B tabs, zone, HP and filters. Green means
  unpulled, orange pulled, red dead, blue a Faloop report without live feedback.
  Grey means unknown combat state; unknown HP is shown as `?%`.
- Check nearby counts (`[number]` or `[?]`) within 50 yalms, including self.
  Counts use cached nearby-player observations, including players no longer
  rendered; departed players can remain estimated until context changes. Multiple
  scouts must not inflate counts. Verify cache resets on zone/world/instance
  changes and logout, while live mark observations still expire within 3 seconds.
- Check Faloop elapsed time, FOUND/RELAY chat map links, duplicate suppression for
  your own detection, and cross-DC alert filters.
- Check map clicks and Lifestream travel from active marks and Ctrl-clicking timer
  names, including a spawn attempt without exact coordinates. One action should
  complete world and aetheryte travel. Instance selection remains manual.

## Compatibility and reporting

Sync uses protocol 4. Use server 0.3.13 or newer for acknowledged train completion
and the current feature set. Server address/password are supplied privately.
Community reports do not establish live visibility or known HP. First-seen
corpses do not establish exact kill times. Faloop integration can change upstream.
Offline shared-row edits are replaced by server state on reconnect. Pending
counter contributions survive reconnect to a capable server, but a shared reset
discards contributions for the previous attempt.

Automated tests use local fixtures and loopback servers; in-game group testing is
separate. Include versions, mark, world, instance and expected/actual behaviour in
reports. Remove passwords, webhook URLs, character names and other private data
from logs and screenshots before posting.
