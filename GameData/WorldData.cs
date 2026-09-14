using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

public readonly record struct WorldEntry(uint RowId, string Name, uint DataCenterId);

/// <summary>
/// Data centres and their worlds, read from the game's own sheets so the list
/// stays correct as Square adds or moves worlds. Used by the Scout tab's
/// counter view, where a scout may want to look at counts for a world they
/// aren't currently on.
/// </summary>
public sealed class WorldData
{
    public IReadOnlyList<(uint Id, string Name)> DataCenters { get; }
    private readonly List<WorldEntry> _worlds = new();
    private readonly Dictionary<uint, IReadOnlyList<WorldEntry>> _worldsByDc = new();
    private readonly Dictionary<uint, (int DcIndex, int WorldIndex)> _locations = new();
    private readonly Dictionary<uint, string> _names = new();

    public WorldData(IDataManager dataManager)
    {
        var dcs = new List<(uint, string)>();

        try
        {
            foreach (var world in dataManager.GetExcelSheet<World>())
            {
                // Skip test/internal worlds, which aren't playable.
                if (!world.IsPublic) continue;

                var name = world.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name)) continue;

                _worlds.Add(new WorldEntry(world.RowId, name, world.DataCenter.RowId));
            }

            foreach (var dc in dataManager.GetExcelSheet<WorldDCGroupType>())
            {
                var name = dc.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!_worlds.Any(w => w.DataCenterId == dc.RowId)) continue;
                dcs.Add((dc.RowId, name));
            }
        }
        catch
        {
            // A missing list just means the picker is empty; counts still work.
        }

        DataCenters = dcs.OrderBy(d => d.Item2).ToList();
        foreach (var world in _worlds) _names.TryAdd(world.RowId, world.Name);
        foreach (var group in _worlds.GroupBy(w => w.DataCenterId))
            _worldsByDc[group.Key] = group.OrderBy(w => w.Name).ToList().AsReadOnly();
        for (var dcIndex = 0; dcIndex < DataCenters.Count; dcIndex++)
        {
            var worlds = WorldsIn(DataCenters[dcIndex].Id);
            for (var worldIndex = 0; worldIndex < worlds.Count; worldIndex++)
                _locations.TryAdd(worlds[worldIndex].RowId, (dcIndex, worldIndex));
        }
    }

    public IReadOnlyList<WorldEntry> WorldsIn(uint dataCenterId) =>
        _worldsByDc.GetValueOrDefault(dataCenterId) ?? Array.Empty<WorldEntry>();

    /// <summary>
    /// Resolves a world to its position in the picker: which data centre index
    /// and which world index within that centre. Returns null when the world
    /// isn't in the list (unknown id, or the sheets failed to load).
    /// </summary>
    public (int DcIndex, int WorldIndex)? LocateWorld(uint worldId) =>
        worldId != 0 && _locations.TryGetValue(worldId, out var location) ? location : null;

    public uint IdOf(string name) => _worlds.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)).RowId;

    public string NameOf(uint worldId) =>
        _names.GetValueOrDefault(worldId) ?? $"World {worldId}";
}
