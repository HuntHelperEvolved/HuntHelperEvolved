using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelperEvolved;

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
    /// <summary>
    /// Hunt Helper's InternalName, which is what Dalamud keys an installed
    /// plugin by — the folder under installedPlugins is named for it.
    /// </summary>
    public const string HuntHelperInternalName = "HuntHelper";

    /// <summary>
    /// Whether Hunt Helper is installed at all, loaded or not.
    ///
    /// Deliberately "installed" rather than "running". A disabled Hunt Helper
    /// holds no commands, so taking its /hh aliases would work right up until
    /// it was switched back on and wanted them again. Someone who still has it
    /// installed has not finished moving over.
    ///
    /// A failure here reports true, which is the safe answer: the only thing
    /// this decides is whether to claim commands that may not be ours.
    /// </summary>
    public static bool IsHuntHelperInstalled(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            foreach (var plugin in pluginInterface.InstalledPlugins)
            {
                if (string.Equals(plugin.InternalName, HuntHelperInternalName,
                                  StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

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
