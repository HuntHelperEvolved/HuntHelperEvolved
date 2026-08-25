# Hunt Train Relay

Connects Hunt Helper to Discord. When a hunt train's marks are all dead, it posts
a message listing each one and its estimated respawn window — shown in each
Discord reader's own local time. It can also post a scouting report on demand,
including a code that pastes straight into anyone else's Hunt Helper.

## Install

1. In-game, type `/xlsettings`, go to the **Experimental** tab, and find
   **Custom Plugin Repositories** near the bottom.
2. Paste this into the empty box and click the **+**:
   `https://raw.githubusercontent.com/MusicManBowls/HuntTrainRelay/main/repo.json`
3. Click **Save and Close**.
4. Type `/xlplugins`, search for **Hunt Train Relay**, and click **Install**.

Updates show up as a normal **Update** button in `/xlplugins` — no reinstalling needed.

## Getting started

1. Get a Discord webhook URL: in your server, right-click the channel you want
   reports posted to → Edit Channel → Integrations → Webhooks → New Webhook →
   Copy Webhook URL.
2. Type `/htr` in-game to open the settings window.
3. Go to the **Settings** tab, paste the webhook URL in, and click **Send test
   message** to confirm it posted.
4. Whoever is actively conducting a train ticks **I'm conducting** on the
   **Conductor** tab — everyone else leaves it off.

Treat the webhook URL like a password — anyone who has it can post to that channel.

## What each tab does

**Conductor**
- *I'm conducting — auto-post when the train is cleared*: turns on live
  tracking. When every mark Hunt Helper currently has recorded is dead, it
  posts automatically. Only the person actually running Hunt Helper's train
  recorder should have this on, to avoid duplicate posts.
- *Status*: a live line showing what tracking currently sees (how many marks,
  how many dead, waiting for Hunt Helper, etc.).
- *Reset train tracking now*: clears tracking so the next mark Hunt Helper
  reports is treated as a fresh train. Doesn't post anything.
- *End Train Now*: a manual fallback — posts whatever Hunt Helper's train list
  currently shows right away, in case auto-post didn't fire for some reason.

**Scout**
- *Send Scouting Report*: posts a paste-able Hunt Helper import code, plus how
  many marks are currently up per expansion — including which specific marks
  were found already dead ("sniped") and which haven't been scouted at all yet.

**Settings**
- *Send test message*: posts a simple test message to confirm the webhook(s)
  work.
- *Webhook URLs*: one row per Discord server to post reports to. Add up to 5
  with the **+ Add webhook** button.
- *Check interval (seconds)*: how often (while "I'm conducting" is on) it
  checks Hunt Helper for changes. 3 seconds is fine for most people.

## Known limitations (by design)

- Only A-rank marks get a computed respawn window. B-rank and S-rank marks
  still get listed if they're in Hunt Helper's train, just without a timer.
- Respawn windows are calculated from when *your own client* saw a mark flip
  to dead, not an exact server-side kill timestamp — accurate to within the
  check interval.
- "Not yet scouted" checks whether a named mark was seen at all, not whether
  every concurrent instance of its zone was checked.

## For whoever maintains this (build from source)

Source and the full build/publish walkthrough are in this repo if you ever
need to change something — ask Claude, since that's who wrote it. Short
version: `dotnet build -c Release`, zip `HuntTrainRelay.dll` +
`HuntTrainRelay.json` from `bin\Release\`, attach that zip to a new GitHub
Release, and update the version + download links in `repo.json`.
