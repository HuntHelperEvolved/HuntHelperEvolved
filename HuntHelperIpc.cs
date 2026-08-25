using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HuntTrainRelay;

/// <summary>
/// Mirrors Hunt Helper's own internal MobRecord shape exactly (same field names,
/// same order, same types) so Dalamud's IPC serialization matches on both ends.
/// Source: img02/HuntHelper, HuntHelper/Managers/IpcSystem.cs
/// </summary>
public record struct HuntHelperMobRecord(
    string Name,
    uint MobID,
    uint TerritoryID,
    uint MapID,
    uint Instance,
    Vector2 Position,
    bool Dead,
    DateTime LastSeenUTC
);

public class HuntHelperIpc
{
    private const string IpcFuncNameGetTrainList = "HH.GetTrainList";

    private readonly ICallGateSubscriber<List<HuntHelperMobRecord>> _getTrainList;

    public HuntHelperIpc(IDalamudPluginInterface pluginInterface)
    {
        _getTrainList = pluginInterface.GetIpcSubscriber<List<HuntHelperMobRecord>>(IpcFuncNameGetTrainList);
    }

    /// <summary>
    /// Returns the current train list, or null if Hunt Helper isn't installed/loaded
    /// (or its IPC isn't ready yet). Never throws.
    /// </summary>
    public List<HuntHelperMobRecord>? TryGetTrainList()
    {
        try
        {
            return _getTrainList.InvokeFunc();
        }
        catch
        {
            return null;
        }
    }
}
