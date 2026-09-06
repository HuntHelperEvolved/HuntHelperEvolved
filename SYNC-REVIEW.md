# Sync implementation review — local testing build

The single-server, password-protected WebSocket design and SQLite persistence
are appropriate for a small trusted group. One server is one shared train;
there are no per-user accounts or conductor roles. These choices were retained.
The server and plugin remain separate repositories.

## Findings resolved

- **Concurrent scouts could diverge:** state was locked, but broadcasting and
  registering snapshot clients were not part of that ordering. Hub operations
  now serialize mutation, persistence, snapshot registration and publication.
- **Reconnects could undo deletions:** snapshots were merged with stale local
  lists. They now replace shared state. Local-only rows have a persistent,
  explicit upload recovery option. In-flight edits cannot recreate removed rows.
- **Broken reconnect/settings controls:** settings comparisons prevented a
  manual reconnect and enabling train sharing from taking effect. Forced
  reconnect and relevant settings changes now restart the transport correctly.
- **Connection lifetime races:** an old worker could clear a new worker's queue
  or report a stale welcome. Each connection generation owns its cancellation
  and publication. Failed or full writers cancel their connection, reads have
  timeouts, and incoming frame size/queue length are bounded.
- **Local edits could be forgotten by echoes:** the client now remembers the
  canonical server signature, preserving differences that still need sending.
- **Incorrect S-rank history:** old kills could rewind timers, better duplicate
  reports erased new exclusions, and observed kills blocked all future Faloop
  cycles. Corrections preserve evidence, old reports cannot rewind the clock,
  and plausible later deaths advance it.
- **Pulled marks corrupted spawn mapping:** live/death positions were matched
  repeatedly to spawn points. The client now captures a conservative first
  healthy, out-of-combat match and retains that origin. S-origin tracking does
  not move during a fight. Unknown origins stay unknown.
- **Missed observed deaths:** sync depended on the tally's fight-credit event.
  It now also receives a zero-health transition from the detector, including
  S ranks witnessed outside the player's own fight credit.
- **Misleading S-rank board:** old spawn sightings could show UP forever and
  a completed timer implied a spawned mark. Sightings expire; READY means the
  timer is ready but spawn conditions are still required. Percentages describe
  elapsed window time; uncertain kills have no percentage.
- **Speculative maintenance:** clusters of eight kills were treated as a
  restart. Faloop's explicit global/world restart timeline is used instead.
  Unknown response shapes fail visibly. Heartbeats refresh feed status.
- **Privacy/defaults:** blank aliases no longer disclose character names;
  unauthenticated status endpoints no longer expose group activity. The sample
  password is rejected, and Compose requires an explicit password.
- **SQLite dependency:** the old native SQLite bundle produced a high-severity
  NuGet advisory. The server uses Microsoft.Data.Sqlite.Core with
  SQLitePCLRaw.bundle_e_sqlite3 3.0.2, which restores without that warning.

## Source and compatibility checks

The plugin feature branch was integrated with local main at 02813aa in a
separate worktree. The original plugin worktree remains on its existing testing
branch. The server's pre-existing uncommitted work was carried onto its feature
branch; main remains at 784599d. Both local Git identities are kihtli with its
GitHub noreply address. Nothing was pushed or deployed.

Protocol version 2 rejects the older prototype, whose spawn and merge behaviour
is incompatible. Spawn-table ordering must remain consistent between clients;
future edits to that table must bump the protocol or add table negotiation.

On 6 September 2026, a read-only anonymous check of Faloop returned Chaos timer
rows and the global/world restart timeline. The adapter's endpoints and field
names were also checked against its public frontend. This confirms present
behaviour, not an API support commitment. Faloop is disabled by default.

Sources: [Faloop frontend](https://faloop.app/),
[SQLite dependency advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).
Timer ranges retain the supplied Sonar source table and attribution.

## Initial testing limits

Automated tests cover state/revision changes, two socket clients, wrong
passwords/protocols, malformed messages, SQLite restart persistence, plugin
transport switching, timer boundaries and the actual published server with the
plugin transport. In-game rendering and real game-object/kill callbacks still
need testing in FFXIV; this environment cannot run that session for you.

The server is a portable .NET 10 build. Docker is not installed here, so the
Dockerfile is provided but its image was not built. No cloud host was deployed.
Friend-group clients share equal rights. Passwords are stored locally in plugin
configuration, and server connection logs contain source IPs and chosen aliases.
Use WSS when hosting beyond a trusted local test network.

Spawn points are an empirical table. The conservative 0.5-coordinate match may
miss uncertain origins instead of declaring a wrong exclusion. A kill unseen
by both the group and the optional external source remains unknown. An offline
edit to an existing shared row is replaced by the next server snapshot; new
local-only rows can be recovered using the explicit upload action.
