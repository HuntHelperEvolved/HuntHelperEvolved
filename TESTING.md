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
