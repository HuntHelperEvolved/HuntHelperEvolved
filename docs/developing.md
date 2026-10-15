# Building and integrations

## Build

Use the .NET 10 SDK and official Dalamud development assemblies (API 15).
Set `DALAMUD_HOME` to the directory containing `Dalamud.dll` if the SDK cannot
find it automatically.

```sh
dotnet build -c Release
```

On macOS or Linux, [`scripts/build-macos.sh`](../scripts/build-macos.sh) locates
the usual XIV on Mac or XIVLauncher.Core installation:

```sh
./scripts/build-macos.sh
```

The build produces `bin/Release/HuntHelperEvolved/latest.zip`.
Use [`scripts/package-testing.py`](../scripts/package-testing.py) to create a
distributable ZIP with the runtime files and licences, excluding debug symbols
and local configuration. CI checks the build, dependencies and package in
[`verify.yml`](../.github/workflows/verify.yml).

## Plugin integrations

HHE exposes Dalamud IPC under `HuntHelperEvolved.*`:

- **Train lists:** `ApiVersion`, `GetTrainListV2` and `ImportTrainListV2`.
  See [TrainIpcProvider](../Interop/TrainIpcProvider.cs) and
  [NativeTrainRecord](../Interop/NativeTrainRecord.cs) for the contract.
- **Train status:** `GetTrainStatusV1` covers the current world's DT/EW/ShB
  roster. `GetTrainStatusV2(uint worldId)` covers all six expansions; zero
  follows the current world. See [TrainStatusContract](../Interop/TrainStatusContract.cs)
  and [TrainStatusIpcProvider](../Interop/TrainStatusIpcProvider.cs).
- **Train window:** `ToggleTrainPopout` toggles the window; `OpenTrainPopout`
  opens it.

Legacy HHE train-list endpoints remain available. HHE does not register
Hunt Helper's `HH.*` endpoints. The sync protocol is defined in
[SyncProtocol](../Sync/SyncProtocol.cs).

The tally code in `Tally/` retains the `HuntTally` namespace and existing
`HuntTally.json` storage for compatibility with the standalone plugin.
