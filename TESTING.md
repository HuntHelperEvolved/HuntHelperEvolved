# Testing checklist

Use the testing feed in [README.md](README.md#install). Back up the plugin
configuration before testing. Build and automated test instructions are in the
[development guide](docs/DEVELOPMENT.md).

## Window visibility

- Load the plugin at the title screen: no HHE windows should appear. Log into a
  character: enabled update notes should open once for a newer version.
- After the first login, Active Marks should remain open through DC travel and
  character selection. Other windows should hide until login. Closing Active Marks
  during travel must keep it closed. Reloading the same version should not reopen
  automatic update notes.

## Settings and help

- Check /hh has Train, S Ranks, Settings and Help. Train has Route and Report
  preview, one scout editor and one Shift-guarded report/reset footer. Collapse
  controls and verify Undo reset remains accessible when a reset can be undone.
- Check S Ranks has Timers & mapping and Counters; train watches live under Train. Verify /hhs
  and /hhc still open their standalone windows and use the same data/filters.
- Check Windows opens every popout, including A-rank timers, Active Marks and
  lifetime tally. Dalamud's Open Main UI should open /hh, not the tally.
- Check Help opens release notes. Settings > Train contains automatic death
  marking; Settings > S Ranks contains counter preferences; train-watch preferences are in Settings > Train. No setting
  values should change merely by switching tabs.
- Visit each settings category at normal and narrow window sizes, with UI scaling.
  Check scrolling, category selection, template editors and saving after reload.
- Verify Active Marks > Filters/settings opens Settings > Active Marks, and `/hunttally config`
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

- In /xlplugins, HHE should list only /hh commands without Hunt Helper, and
  only /htr commands with Hunt Helper installed, including when disabled.
  /hhsa and /hunttally remain aliases but are hidden from the command list.
- Uninstall Hunt Helper while HHE stays loaded: the list should switch to /hh
  and any newly freed aliases should become available. Existing /htra retains
  its next-aetheryte meaning; /htraw opens the A-rank timer board.

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

## Active Marks report gaps

- Briefly lose reports from one scout: rows and observer names should remain for
  up to five seconds. Continued gaps must expire; repeated UI draws must not
  extend retention. Fresh HP/death reports should update immediately.
- Stand near a shared mark: your observer identity should appear only as You,
  alongside other scouts. Verify Include own, and disconnect/reconnect.
- Confirm map icons still use immediate visibility rather than the window grace.

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
  complete world, aetheryte and supported instance travel.

## Compatibility and reporting

Sync uses protocol 4. Use server 0.3.23 or newer for acknowledged train completion
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

## Partial train completion

- Scout five expansions and complete three. Preview must exclude the two unrun
  legs; submit must preserve their marks/order/watches and scout credits.
- Use Remove Dead before submitting: those kills must still be reported once,
  then forgotten on every connected updated client.
- Disconnect one updated client during completion; reconnect/reload afterwards.
  Completed kills must be forgotten, but later kills of the same mark retained.
- Restart the server after partial completion and repeat reconnect.
- Shared reporting on a disconnected or older server must be refused before
  Discord is posted. Upgrade every client that might submit reports: old builds
  cannot apply completion acknowledgements and may post obsolete local history.

## Automatic train watches

- Train > S-rank watches: enable Automatically follow spawn windows on the client
  preparing the train. Only the four supported train checks in the actual route's
  worlds/zones/instances should appear, and only with open windows. Unknown,
  cooling-down, offline and already-up marks should not be added.
- Confirmed mapping should supply a map position. Losing that confirmation should
  remove the automatic position. No unconfirmed point should be guessed.
- Unchecked automatic watches follow window changes; manual watches and completed
  results survive. Disable automation to remove an automatic watch manually.
- With a Mateus train, enter Yak T'el on Coeurl: no reminder. Enter the matching
  world and instance: remind once after zoning settles. Completed watches should
  not remind again, and automatic watches must not remind from disconnected data.
- Verify scoped watches survive reconnects, partial report submission, undo reset
  and server restart. Shared automation requires SupportsScopedTrainWatches on
  server 0.3.22 or later.

## Individual minion observations

Requires server 0.3.23 and beta 16 plugins on both scouts and the viewer.
Older plugins cannot supply actor identity, so their reports retain legacy grouping.

- Two scouts fight separate identical SS minions in the same world/zone/instance:
  verify separate Active Marks rows, HP, combat status, observers and map icons.
- Both scouts observe one minion: verify one row with combined observer names.
- Move a minion across another: movement must not create a new row or merge them.
- Kill one: only its row becomes a corpse and its map icon disappears; the other
  stays alive with its own HP. Withdraw one scout: only their observations expire.
- Check ordinary A/B/S marks, S-rank clocks and shared train rows still behave as
  before. Verify map clicks/row interactions target the intended minion.

Validation: 182 plugin tests and 144 server tests passed. New regressions cover
separate minions at identical coordinates, shared observers, movement, corpse
isolation, scoped removal messages and actor identity serialization. In-game
validation with multiple scouts remains outstanding.
