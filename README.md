# Hunt Helper Evolved

Hunt scouting and train management for FFXIV, built for Dalamud.

- Scout, organise and share hunt trains, with Discord reports and currency messages.
- Track A/S-rank timers, live marks and possible spawn points on the in-game map.
- Count S-rank trigger progress and keep a lifetime hunt tally for each character.

## Install

1. Open `/xlsettings` → **Experimental** and enable **Get plugin testing builds**.
2. Under **Custom Plugin Repositories**, add this URL with the **+** button:

   ```text
   https://raw.githubusercontent.com/HuntHelperEvolved/HuntHelperEvolved/main/repo.json
   ```

3. Save, then open `/xlplugins`, search for **Hunt Helper Evolved** and install it.

HHE is standalone; Hunt Helper is not required.

## Get started

Open `/hh` for the main workspace, Settings and Help (`/htr` if `/hh` is already
in use). Resume scouting in the Train view to start collecting marks.

| Command | Opens |
|---|---|
| `/hht` | Compact train view |
| `/hha` | A-rank timers |
| `/hhs` | S-rank timers and spawn mapping |
| `/hhv` | Active marks |
| `/hhc` | Trigger counters |
| `/hhtally` | Lifetime kill tally |

For group sharing, enter the server URL and password supplied by your organiser
in **Settings → Sharing**, then choose what to share. Everyone connected to the
group can edit the shared train. Local scouting and tally work without a server.

Coming from **Hunt Train Relay** or **Hunt Tally**? Uninstall the old plugins and
keep their configuration files. HHE carries over your settings and tally.

## Help and extras

Use **Help** in `/hh` for controls and feature explanations, or search Settings
for the option you need.

- [Train route presets](docs/train-presets.md)
- [Bear reports and filter presets](docs/BEAR-PREVIEW.md)
- [Optional Umbra train widget](https://github.com/HuntHelperEvolved/Umbra.HuntHelperEvolved)
- [Release notes](https://github.com/HuntHelperEvolved/HuntHelperEvolved/releases)
- [Report a problem](https://github.com/HuntHelperEvolved/HuntHelperEvolved/issues)
- [Building and integrations](docs/developing.md)

## Credits

By MusicManBowls and kihtli, continuing
[Hunt Train Relay](https://github.com/MusicManBowls/HuntTrainRelay) and Hunt Tally,
with data and design from [Hunt Helper](https://github.com/img02/HuntHelper) and
[Faloop](https://faloop.app/).

[MIT licence](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)

<!-- Keep this page focused on getting started. Detailed controls belong in Help
or focused guides; release history belongs in Releases. Avoid duplicating them here. -->
