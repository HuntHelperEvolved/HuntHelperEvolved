# Local Bear Toolkit preview

Branch `codex/bear-integration-ui` combines the current reimagined UI with Bear
Toolkit S-rank integration. Plugin version `0.6.0.6` is a local preview; `repo.json`
continues to point to the existing public build.

Build with the .NET 10 SDK and Dalamud development references installed:

```sh
dotnet build HuntHelperEvolved.csproj -c Release
python3 scripts/package-testing.py bin/Release/HuntHelperEvolved/latest.zip HuntHelperEvolved-testing.zip
```

Extract the testing ZIP into a dedicated local directory. Disable the installed
HHE copy before loading this build through Dalamud's developer plugin loading
controls. Keep the internal name `HuntHelperEvolved` so the existing settings
can be used. Do not load both copies simultaneously.

Enable sync to the preview server and enable the Bear preview option in
Sharing settings. The option defaults off. The connection details show whether
the server supports Bear and whether its feed is connected. An older server
continues to provide the normal group and Faloop data.

The server must also have Bear enabled, selected data centres and a private
Bear credential file. Bear credentials stay on the server. Each plugin opts
in independently; the server never sends Bear snapshots to existing clients
that omit the opt-in field.

The Bear feed contains S-rank reports only; A-rank scouting and existing sources remain unchanged.
Bear reports are a separate read model. They do not become group scout
observations, train edits, shared kill reports, spawn-point exclusions or tally
credit. Live evidence uses **plugin > Bear > Faloop** priority. HP is fresh for
15 seconds. While the Bear sighting remains active, an older positive HP report
stays visible in grey as `~80%` with its age in the tooltip. This is the last reported
value, not current health or combat evidence. The sighting still expires after
five minutes without a new qualifying report. Zero HP lasts only 15 seconds
unless a separate death report confirms the kill.
Disconnecting clears live Bear evidence; it does not create a kill.

The pull-time clock always uses the Faloop report time, even when a plugin or
Bear supplies the displayed health. If Faloop has not supplied a timer, the
clock is unknown. Bear updates never restart it. The existing S/A timer-board
values remain unchanged; Bear kill reports are shown separately in Active Marks.

For in-game verification, compare the same S-rank, world and instance in Bear and HHE:

1. Observe a spawn, its coordinates and subsequent HP changes.
2. Observe a kill and check that the corresponding live community row clears.
3. Wait 15 seconds without HP changes: Bear should remain the source and HP should show `~` with its age. Disconnect/reconnect and check that old HP is not displayed as fresh.
4. Have an existing plugin connected simultaneously; its normal scouting,
   Faloop and train behaviour should continue without Bear reports.
5. Disable the preview and confirm the UI returns to its normal sources.
6. Confirm plugin sightings win over Bear, Bear wins over Faloop, and the
   Faloop pull-time clock stays the same through HP updates and source changes.

Automated tests exercise parsing, freshness and compatibility. Account access,
token renewal and actual in-game rendering still require this live check.
Return to the installed build by unloading the development copy and re-enabling
the installed plugin. Bear preview data is not persisted into shared hunt state.
