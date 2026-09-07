# Staged 0.5.0.3 / server 0.3.13

Not uploaded or deployed. The public installer feed stays on 0.5.0.2.

- Compare /hha sniped Opens/Window end with Discord for a mark seen alive earlier. Unknown lower bounds must remain unknown; repeat after train reset and reconnect.
- End Train: successful Discord and server acknowledgement reset the unchanged train with Undo. Failed Discord, old/unavailable server, or a concurrent mark/order/watch edit must retain it. A lost acknowledgement may leave an already-reset train; inspect state and use Undo rather than immediately reposting Discord.
- Active Marks: verify zone, visible player count within 50 yalms (including self), expiry, unknown counts from older observers, and Faloop elapsed time. Multiple scouts must not inflate the count.
- Two scouts add live marks; combine and deduplicate their configured sync display names with manual credits. Credits persist across reconnect and clear with the train. Manual credits accumulate until reset.
- Collapse/reopen the train controls and scouts, including after plugin reload. The train list and End Train footer remain usable.
- S-rank name colours: grey before the timer/when unknown, red in an open respawn window outside timed conditions, green when timer and timed conditions align. Manual actions are still required; active rows retain their highlight.

# 0.5.0-beta.2 hotfix

A-rank cooldowns now load server kill history on connection, including after train resets and server restarts. Requires server 0.3.12. Test joining after a completed/reset train with an empty local history and with train sharing disabled; check each world and instance separately. Kill history cleared before the server upgrade cannot be recovered.

# 0.5.0-beta.1

This beta runs independently of Hunt Helper. Existing native trains and
tallies are retained. Back up the plugin configuration before updating.
The installer URL is in [README.md](README.md#install). This beta uses the main
repository feed and remains testing-exclusive. Existing users can update normally.

## What to test with your group

- Split scouting between two users; check train order and expansion grouping.
  Group headings count marks, and the next unfinished expansion opens automatically.
- Reset a shared train and use Undo to recover locally. Undo turns off train sharing
  for the restoring user, so restoring does not overwrite everyone else's train.
- Compare S-rank windows and spawn-condition countdowns in `/hhs`; scout A/B marks
  and check that mapping rules out spots in the current reliable kill cycle.
- Check `/hha` separates instances. Uninstanced cells are blank.
- Open `/hhsa` or `/hhv`: Active Marks has All/S/A/B tabs, with HP after the name.
  Green is unpulled, orange pulled, red dead, blue a Faloop report without live
  feedback. Grey is a live report with unknown combat state; ?% means unknown HP.
  Hover for details, click for the map, Ctrl-click or right-click for Lifestream.
  Configure world/DC, expansion and status filters on the main Sync tab.
- Compare `your count (group count)` while killing trigger mobs separately and
  together. Only personal kill messages contribute; nearby observers do not
  double-count. Local Reset stays local; Reset shared starts a new group attempt.
- Verify FOUND/RELAY chat map links, cross-DC alert filters and one-click world plus
  aetheryte travel. Instance selection after travelling remains manual.

## Limits to keep in mind

A matching private group server is required for sync (0.3.11 or newer). Its address
and password are supplied separately. The server repository is private.
Live observations expire within three seconds without updates; community reports
do not imply live visibility or known HP. First-seen corpses do not establish an
exact kill time. Faloop integration is experimental and may change upstream.
Shared-row edits made offline are replaced by server state on reconnect. Pending
kill-counter contributions are retained after joining a capable server, but a
shared reset discards contributions belonging to the previous attempt.

When reporting a problem, include plugin/server version, mark, world, instance,
expected behaviour and actual behaviour. Remove passwords, webhook URLs, character
names and other private information from logs/screenshots before posting.
