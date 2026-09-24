# Train presets

Presets save a conductor's preferences for future trains: expansion and zone
order, optional strict mark order, aetheryte choices and rally stops. Each
train's marks and positions come from scouting.

## Create and select a preset

Open **/hht > Setup > Manage presets**. Create a named
preset, move its expansions and zones earlier or later, and save it locally.
New drafts begin with Dawntrail, Shadowbringers and Endwalker; add or remove
zones and expansions as needed. Saving a draft does not select it.

Before scouting, choose the conductor's preset under **Route preset** in
**Setup**. The selector works with an empty train. Incoming
marks follow the preset as they arrive, and it remains selected for later
trains until you choose **Manual order** or another preset.

The main Train tab and `/hht` popout share **Route**, **Reports** and **Setup**
pages. The **Route** page shows the active preset and any ordering pause above
its single-line mark rows; **Change** opens Setup. Next Mark, Next Aetheryte and
the right-aligned pause/resume icon stay available while moving between pages.
A page change never changes the route or starts or pauses recording.

For an Arch-Eta finale, put Endwalker last and Ultima Thule last within it.
Enable **Strict mark order** for Ultima Thule and use **Reverse mark order**
until it reads **Fan Ail > Arch-Eta**. Each instance follows that order.

## Share with the group

With shared train sync enabled, **Route preset** shows the server's library.
Use **Share / update server** in the editor to upload a draft, then select it
in **Route preset**. Selection changes the route for everyone, including
marks scouted by other clients. Shared presets require server **0.3.26 or later**.
Older plugins still receive the ordered train and ordinary rally flags.

Updating an active, unpaused server preset recalculates the current route.
The editor states this before submission. An outdated draft is rejected and
kept locally; reopen the server copy or duplicate the draft under a new ID.
Server presets can also be opened, saved locally or shared with another server.

With train sharing off, the selector uses your local presets. Local presets
and their selection are retained separately while sharing is enabled. An
older server can still share ordinary train updates, but cannot apply presets.

## Adjust the route during a train

Drag a mark or expansion heading to move it. A completed move pauses automatic
ordering and keeps the rest of the route and pending rally positions in place.
The saved preset is unchanged. **(paused)** appears beside its name, and new
scouts append without route calculation or new rally stops.

The pause survives reconnects, resets, restarts and future trains. Reselect
any preset, including the same one, to resume and recalculate. Saving an
edited preset while paused does not resume ordering. Shared adjustments send
the order and pause together, so everyone receives the accepted route.

**Manual order** releases the preset without clearing hunt marks. Deleting
the active preset also returns to manual order. Older plugins cannot override
an active shared preset with ordinary reorder messages; update the conducting
client to adjust and pause the route.

## Rally preferences

Rallies appear as **Rally: <aetheryte>** custom flags with a zone, world and
instance. They use the existing map links, teleport button, auto-advance and
removal shortly after a successful teleport.

- **Rally on zone entry and instance changes** adds a stop before the live
  marks in each numbered instance, starting at i1. It adds no extra stops
  between marks in the same instance or in uninstanced zones.
- Each expansion after the first has a **Rally when entering** checkbox.
  Choose either transition, both, or neither. The preference follows the
  destination expansion if you move it. The first expansion has no
  expansion-change rally.
- **Entry / rally aetheryte** applies to every instance of the selected zone.
  Automatic chooses the allowed aetheryte with the shortest estimated flight
  to the first live mark. A fixed choice sets the rally location and the
  starting point for automatic mark ordering.

An expansion entry and instance entry at the same stop produce one flag.
Expansion rallies follow the first scouted live zone in preset order.
Completing or removing a stop records that entry, so more scouting during the
same zone/instance visit and reconnects do not recreate it. Once that visit has
no live marks, scouting live marks there again restores its rallies. Other
zones, instances and worlds keep their progress. An expansion rally becomes
available again when that expansion has no live marks and is later rescouted.

Selecting any preset, including the current one, rebuilds its rallies for the
remaining live marks and resumes automatic ordering. Use this to restore rallies
without resetting the train, including when marks were never cleared or marked
dead. Pending flags keep their identities; completed stops become fresh flags.
Resetting or finishing the whole train clears rally progress while retaining
the preset and any ordering pause. Shared trains need the server update for
this rally recovery behavior.

Existing rally flags still support completion and removal while ordering is
paused. Use the row's check button to complete or restore a stop, or its
more-actions menu to remove it. Rally stops have no sniped state. Zones with no
live marks lose their preset flags. Disabling rallies
or choosing **Manual order** removes pending preset flags; manually placed
custom flags remain. Personal teleport exclusions still apply to travel.

## How routes are estimated

- World blocks keep their existing order. Within each world, the preset
  orders expansions, then zones, then instances numerically.
- Omitted zones follow listed zones in their expansion. Unlisted expansions
  and unknown zones follow listed expansions. Presets retain hunt marks,
  their coordinates and their death and report evidence.
- Automatic zones minimize total map distance from an available aetheryte
  through the live marks, using direct mark-to-mark flight. This estimates
  one entry teleport followed by flight; it does not compare extra teleports
  between marks, loading times, altitude or the conductor's current position.
- Tertium includes its required departure north from `(31.8, 17.9)` to
  `(31.5, 12.7)` before heading to the first mark. Both legs count, so it can
  still win when that route is shorter than Camp Broken Glass. Other terrain
  is not modelled.
- Automatic rally selection and this version's teleport recommendations use
  the same departure estimate. Rally flags stay at the aetheryte itself;
  Tertium's northern exit is not an extra stop. Older clients retain their
  own teleport recommendations.
- Presets capture the creator's aetheryte exclusions. **Use current aetheryte
  exclusions** refreshes them in the draft. Sharing them with the preset
  keeps the group's route calculation consistent.
- Aetherytes without usable coordinates are excluded. If no usable entrance
  or mark coordinates exist, scout order is retained. Completed marks precede
  live marks in automatic runs.
- Automatic ordering treats manually placed custom flags as barriers within
  a zone and instance. Strict ordering sorts ordinary marks while keeping
  flags in their existing slots.

Late scouts and location corrections update the route while an unpaused
preset is active. If the estimate is unsuitable, drag the marks into the
preferred order to pause it.
