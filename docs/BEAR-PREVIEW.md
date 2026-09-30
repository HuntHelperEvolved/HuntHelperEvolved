# Local Bear Toolkit preview

Branch `codex/bear-integration-ui` combines the current reimagined UI with Bear
Toolkit integration. Plugin version `0.6.0.4` is a local preview; `repo.json`
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

Bear reports are a separate read model. They do not become group scout
observations, train edits, shared kill reports, spawn-point exclusions or tally
credit. Live evidence uses **plugin > Bear > Faloop** priority. Health has a shorter
validity period than spawn membership and returns to unknown when stale.
Disconnecting clears live Bear evidence; it does not create a kill.

The pull-time clock always uses the Faloop report time, even when a plugin or
Bear supplies the displayed health. If Faloop has not supplied a timer, the
clock is unknown. Bear updates never restart it. The existing S/A timer-board
values remain unchanged; Bear kill reports are shown separately in Active Marks.

For in-game verification, compare the same world and instance in Bear and HHE:

1. Observe a spawn, its coordinates and subsequent HP changes.
2. Observe a kill and check that the corresponding live community row clears.
3. Disconnect/reconnect the feed and check that old HP is not displayed as fresh.
4. Have an existing plugin connected simultaneously; its normal scouting,
   Faloop and train behaviour should continue without Bear reports.
5. Disable the preview and confirm the UI returns to its normal sources.
6. Confirm plugin sightings win over Bear, Bear wins over Faloop, and the
   Faloop pull-time clock stays the same through HP updates and source changes.

Automated tests exercise parsing, freshness and compatibility. Account access,
token renewal and actual in-game rendering still require this live check.
Return to the installed build by unloading the development copy and re-enabling
the installed plugin. Bear preview data is not persisted into shared hunt state.
