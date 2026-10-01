# Local 0.7.0.0 testing build

Plugin version `0.7.0.0` combines the redesigned UI with Bear Toolkit S-rank
integration and named filter presets. It is staged in local `main` for final
testing; `repo.json` continues to point to the public `0.6.0.3` build.

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
Bear live sightings and HP remain a separate read model. They do not become
group scout observations, train edits or tally credit. Confirmed Bear S-rank
kill reports are also accepted by an updated server into the shared S-rank
timers and spawn mapping, even when neither a plugin nor Faloop saw the spawn.
The kill starts a new mapping cycle; a previous spawn point is recorded when
reported coordinates can be matched reliably. These shared updates reach all
connected compatible clients, including clients that have not opted in to the
live Bear preview. The server resolves conflicting sources with
**plugin > Bear > Faloop** priority.

Live evidence uses the same source priority. HP is fresh for
15 seconds. While the Bear sighting remains active, an older positive HP report
stays visible in grey as `~80%` with its age in the tooltip. This is the last reported
value, not current health or combat evidence. The sighting still expires after
five minutes without a new qualifying report. Zero HP lasts only 15 seconds
unless a separate death report confirms the kill.
Disconnecting clears live Bear evidence; it does not create a kill.

RELAY chat alerts briefly wait one second for accompanying health reports.
They use current plugin HP first, then Bear HP; older positive Bear health is
grey with a `~` prefix, and HP is unknown only when neither source has usable
health. Reports from different sources, releases and local detections share
one notification per mark, world, instance and spawn cycle. Confirmed deaths,
maintenance resets or the mark's minimum respawn interval allow a later spawn
to notify again.

Active Marks (`/hhv`) has independent inclusion rules under its filter button.
In **Worlds & ranks**, each rule chooses a world or DC, ranks and expansions;
marks matching any enabled rule appear once. **Current world** and **Current DC**
follow the visited world, and wait for a known world during loading. **All**
expansions includes every expansion; **None** includes nothing. **ShB+** selects
Shadowbringers, Endwalker and Dawntrail. Search and the rank tabs further narrow
the result. **Status & display** controls common source, life and combat filters.

Use **Saved presets** to create your own named collection. Configure the rules,
choose **Save as...** and enter a name. Selecting a saved preset does not apply
it: use **Load** to replace the current rules, or **Update saved** to replace
that preset with the current rules. **Rename...** changes its name; **Delete...**
asks for confirmation and keeps the current rules. The current preset name
shows **(modified)** when active rules differ. Active rules save as you edit;
saved presets change only when explicitly updated. Presets store scope rules,
not the common status/display settings or temporary rank tabs and search.

For example, add an S-rank rule for Crystal with all expansions, an A-rank rule
for Mateus (or **Current world**), and S-rank **ShB+** rules for Aether, Primal and
Dynamis; then save that combination with a name of your choice. Existing active rules stay intact on upgrade.

S-rank RELAY chat has its own rule editor and separate saved-preset library in
**Settings > Notifications > Community S-rank alerts**. These rules control both
the chat line and relay sound. **Copy /hhv S-rank rules** copies only S-rank
location and expansion selections into active chat rules; later edits remain
independent. You can then save these as a named chat preset. No A-rank or SS
relays are added. Existing relay DC preferences migrate with their previous
meaning, including an empty selected-DC list allowing nothing. **Reset relay
rules** returns to all S ranks in the current DC. Preference resets preserve
both saved-preset libraries. **Test chat format** displays a local TEST example
regardless of the chat-alert switch or rules, without consuming notification
suppression for a real spawn. The server's feed coverage remains separate.

The pull-time clock always uses the Faloop report time, even when a plugin or
Bear supplies the displayed health. If Faloop has not supplied a timer, the
clock is unknown. Bear updates never restart this pull-time clock. Confirmed
Bear kills do update the S-rank respawn timer and mapping through the server;
A-rank timers retain their existing sources. A zero-HP sighting without a
confirmed kill report does not create a shared kill time.

For in-game verification, compare the same S-rank, world and instance in Bear and HHE:

1. Observe a spawn, its coordinates and subsequent HP changes.
2. Observe a confirmed kill and check that its S-rank timer and mapping cycle
   update, including when the plugin and Faloop did not see the spawn.
3. Wait 15 seconds without HP changes: Bear should remain the source and HP should show `~` with its age. Disconnect/reconnect and check that old HP is not displayed as fresh.
4. Have an existing plugin connected simultaneously: it should receive the
   shared S-rank kill and mapping update without receiving live Bear snapshots.
5. Disable the preview: live Bear rows should disappear, while accepted shared
   kill times and mapping remain available.
6. Confirm plugin sightings win over Bear, Bear wins over Faloop, and the
   Faloop pull-time clock stays the same through HP updates and source changes.
7. Check RELAY chat HP against the latest plugin/Bear report. The same spawn
   arriving from another source or being released should not notify again;
   a different world or instance should still notify independently.
8. Open `/hhv` filters, create rules for your chosen worlds, ranks and expansions,
   then save them under a name. Change rules and check the modified indicator;
   Load should restore the saved rules, while Update saved should keep the edits.
   Verify Save as, Rename and Delete, including duplicate-name validation,
   independent copies, reload persistence and preference resets retaining presets.
9. In Notifications, create chat presets or copy the `/hhv` S rules and save
   them. Check chat and sound exclusions, per-expansion matching, current-world/DC
   travel, and that later `/hhv` edits do not alter chat rules or presets.
10. Check preset controls and the rule editor at narrow window widths and larger
    UI scales in both compact `/hhv` and workspace Active Marks settings.

Automated tests exercise parsing, freshness and compatibility. Account access,
token renewal and actual in-game rendering still require this live check.
Return to the installed build by unloading the development copy and re-enabling
the installed plugin. Live Bear sightings and HP are not persisted into shared
hunt state. Accepted Bear S-rank kills and their mapping cycle are shared server
state and remain after the preview is disabled or the plugin is unloaded.
