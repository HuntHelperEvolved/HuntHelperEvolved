# Hunt Train Relay

A small Dalamud plugin that watches Hunt Helper's recorded A-rank train. When every
mark currently in the train is marked dead, it posts a Discord message listing each
mark and its estimated respawn window — shown in each Discord reader's own local time.

Only **one person** needs to follow the "Build it" section below. Once it's built,
the other conductors just need the built files copied onto their own PC (see
"Getting it onto other conductors' PCs").

## What's in this folder

- `HuntTrainRelay.csproj` — the project file
- `HuntTrainRelay.json` — the plugin's manifest (name, description, etc.)
- `Plugin.cs` — entry point, settings window, `/htr` command
- `Configuration.cs` — saved settings (webhook URL, on/off toggle)
- `HuntHelperIpc.cs` — talks to Hunt Helper's train data
- `TrainWatcher.cs` — watches for "every mark now dead"
- `ExpansionData.cs` — maps each A-rank mark to its respawn window
- `DiscordRelay.cs` — builds and sends the Discord message

## 1. Build it (one person, one time)

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) if you don't have
   it. If the build step below asks for a specific version, install whatever it asks for.
2. Install [Visual Studio Community](https://visualstudio.microsoft.com/) (free) —
   or if you're comfortable with a terminal, you can skip straight to step 4.
3. Open `HuntTrainRelay.csproj` in Visual Studio (double-click it), **or** open a
   terminal in this folder.
4. Build it:
   - In Visual Studio: press the green "Run"/hammer icon, or Build → Build Solution.
   - In a terminal: run `dotnet build`
5. The first build downloads Hunt Helper's plugin framework (Dalamud.NET.Sdk)
   automatically — this needs an internet connection and can take a minute.

**If the build fails:** copy the exact error text and send it back — this is normal
for a first build and usually a one-line fix.

When it succeeds, you'll have a `bin\Debug\` (or `bin\x64\Debug\`) folder containing
`HuntTrainRelay.dll` and `HuntTrainRelay.json`.

## 2. Load it in-game

1. Close the game if it's open.
2. Go to `%AppData%\XIVLauncher\devPlugins\` (paste that into File Explorer's address bar).
3. Make a new folder called `HuntTrainRelay`.
4. Copy `HuntTrainRelay.dll` and `HuntTrainRelay.json` from your `bin\Debug\` folder
   into that new folder.
5. Start the game. Dalamud loads anything in `devPlugins` automatically — you don't
   need to install it through the plugin browser.
6. Type `/htr` in chat to open its settings window.

## 3. Getting it onto other conductors' PCs

Easiest option for now: after building, zip up the `HuntTrainRelay` folder from
step 2 (containing the `.dll` and `.json`) and share it with the other three
conductors. Each of them creates the same `devPlugins\HuntTrainRelay\` folder and
drops those two files in.

Once you're happy it's working, a nicer long-term option is hosting it as a tiny
custom plugin repo on GitHub (like Hunt Helper itself), so everyone just pastes one
repo URL into Dalamud and gets a normal Install/Update button instead of manual
file copying. Happy to set that up with you when you're ready.

## 4. Create the Discord webhook

In your Discord server: right-click the hunt-train channel → Edit Channel →
Integrations → Webhooks → New Webhook → Copy Webhook URL.

Treat this URL like a password — anyone who has it can post to that channel.

## 5. Using it

Each conductor pastes the **same** webhook URL into their own `/htr` window
(saved locally, one-time). Only the conductor actively running Hunt Helper's train
recorder for a given session should tick "I'm conducting — auto-post when the
train is cleared" — that's what stops two people's clients both posting the same
message. Use "Send test message" to confirm the webhook works before your first
real train.

## 6. Publishing via GitHub (optional — replaces manual file sharing)

Once you're happy it's working, this lets your other conductors get a normal
Install/Update button in `/xlplugins` instead of you sending a zip every time.

**The repo can stay Private while you're setting this up — it only needs to be
Public once you're ready to actually share the URL with your friends.** Dalamud
doesn't log in to fetch it, it's a plain public request, so a private repo will
simply fail to load for anyone but you.

1. Create a GitHub repository (e.g. `HuntTrainRelay`), and push this whole folder
   to it — [GitHub Desktop](https://desktop.github.com) is the easiest way if you
   don't want to use git from the command line.
2. `repo.json` (in this folder) is the index file Dalamud reads. It already has
   your name and repo URL filled in — the only things you'll update each release
   are the version number and the three `DownloadLink` fields.
3. Build in **Release** mode this time: `dotnet build -c Release`. Zip up
   `HuntTrainRelay.dll` and `HuntTrainRelay.json` from `bin\Release\` — same two
   files as always.
4. On GitHub: Releases → Create a new release → tag it (e.g. `v1.0.0`) → attach
   that zip → Publish. GitHub gives you a direct download URL for the attached
   file afterward.
5. Paste that URL into all three `DownloadLink...` fields in `repo.json`, commit,
   and push.
6. Flip the repo to Public (Settings → Danger Zone → Change visibility).
7. Send your friends this URL to paste into Dalamud's Custom Plugin Repositories
   (`/xlsettings` → Experimental tab):
   `https://raw.githubusercontent.com/MusicManBowls/HuntTrainRelay/main/repo.json`

**For future updates:** bump the `Version` in `HuntTrainRelay.csproj` and the
`AssemblyVersion` in `repo.json` (they should match), rebuild, create a new
GitHub Release with the new zip, update the three `DownloadLink` fields to the
new release's URL, and push. Everyone with the repo added will see an Update
button next time they open the plugin installer.

## Known limitations (by design, for now)

- Only A-rank marks get a computed respawn window. B-rank and S-rank marks will
  still be listed if they're in Hunt Helper's train, just without a timer.
- The respawn window is calculated per-mark from when *your own client* saw it
  flip to dead while polling (every few seconds), not an exact server-side kill
  timestamp — accurate to within the poll interval.
- If Hunt Helper isn't running, or its version changes its IPC in a breaking way,
  the settings window's Status line will say so rather than posting.
