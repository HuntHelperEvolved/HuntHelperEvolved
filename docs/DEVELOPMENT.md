# Development

Hunt Helper Evolved runs independently of Hunt Helper. HHE IPC contracts, configuration names and namespaces are retained. It does not
check for Hunt Helper or register its IPC endpoints.

## Source layout

| Location | Purpose |
| --- | --- |
| `Plugin.cs`, `Configuration.cs`, `ReleaseNotes.cs` | Entry point, settings and in-game release notes |
| `Detection/` | Mark observations, nearby-player estimates, alerts and counters |
| `GameData/` | Mark, world, expansion and spawn-point reference tables |
| `Maps/` | Map overlays, coordinates, flags and aetheryte selection |
| `Train/` | Local train tracking, progression and completion guards |
| `Reports/` | Scouting/train reports, report history and Discord delivery |
| `Interop/` | HHE IPC records, train exchange and providers |
| `Sync/` | Server client, shared state, timer windows and Lifestream travel |
| `Tally/` | Per-character lifetime kill tallies |
| `tests/` | Automated regression tests |
| `scripts/` | Build helpers |

Spawn points are compiled from `GameData/SpawnPointData.cs`. Preserve their order:
shared mapping uses point indices. Dot textures are generated at runtime in the
plugin configuration directory. Data provenance is recorded in
[third-party notices](../THIRD-PARTY-NOTICES.md).

## Build and validate

From the repository root, with the .NET SDK and Dalamud references installed:

```sh
dotnet build HuntHelperEvolved.csproj -c Release
dotnet test tests/HuntHelperEvolved.Sync.Tests -c Release
```

On macOS, `./scripts/build-macos.sh` locates common Dalamud installations; set
`DALAMUD_HOME` explicitly for another location. The helper works from any working
directory. Build output is under `bin/Release/`.

Tests cover local state and loopback transport without changing the live server.
Use the [testing checklist](../TESTING.md) for in-game validation. Retain deterministic
build path mapping; exclude debug symbols, credentials and local configuration
from distributable archives. Generated build outputs and test reports are ignored.
Release notes belong in GitHub releases and `ReleaseNotes.cs`, rather than README.
