using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>
/// Scans the object table for A-rank hunt marks and maintains our own train
/// list, independent of Hunt Helper. Detection runs on IObjectTable, a stable
/// first-class Dalamud service — the same tier as everything else here.
///
/// Coordinate conversion and the map-scale quirk are adapted from Hunt Helper
/// (img02/HuntHelper, MIT licensed): every zone uses a scale of 100 except the
/// Heavensward zones (territory 397-402) which use 95.
/// </summary>
/// <summary>
/// Any mark spotted while scanning, of any rank. Deliberately separate from
/// DetectedMark and from the train: sightings are "what I have seen", while
/// the train is "what I am recording". A-ranks appear in BOTH when recording
/// is on, and only here when it's paused — which is what lets the map and the
/// detection echo keep working with the train paused.
/// </summary>
public class OtherRankSighting
{
    public string Name = string.Empty;
    public uint NameId;
    public HuntRank Rank;
    public uint TerritoryId;
    public uint MapId;
    public uint Instance;
    public uint WorldId;
    public string WorldName = string.Empty;
    public Vector2 MapPosition;
    public DateTime LastSeenUtc;

    /// <summary>
    /// How much of its health the mark has left, as a percentage.
    ///
    /// Refreshed on every scan alongside the position, because it is the same
    /// kind of fact: what is true of this mark now. A mark well below full is
    /// one somebody is already on, which is the thing a scout wants to know
    /// before walking to it.
    /// </summary>
    public float HealthPercent = 100f;
    public int? SpawnPointIndex;

    /// <summary>Zone name, resolved once at first sighting.</summary>
    public string ZoneName = string.Empty;

    /// <summary>
    /// Who saw it, when it came from the sync server rather than this
    /// client's own scan. Empty for anything seen here.
    /// </summary>
    public string Reporter = string.Empty;

    public bool IsRemote => Reporter.Length > 0;

    /// <summary>As DetectedMark.Key, and for the same reason.</summary>
    public (uint NameId, uint Instance, uint WorldId) Key => (NameId, Instance, WorldId);
}

public sealed class MarkDetector
{
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly IDataManager _dataManager;
    private readonly Configuration _config;

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), DetectedMark> _marks = new();
    private int _nextOrder;
    private readonly MarkDeathEvidence _deathEvidence = new();

    // Synthetic ids for custom flags, counting down from the top so they can
    // never collide with a real BNpcName row id.
    //
    // Started from a random spot near the top rather than the very top, so
    // two clients sharing a train through the sync server do not both mint
    // 4294967295 for their first flag and have the server treat them as one.
    private uint _nextCustomId = uint.MaxValue - (uint)Random.Shared.Next(0, 8_000_000) * 16;

    private readonly Dictionary<(uint NameId, uint Instance, uint WorldId), OtherRankSighting> _otherRanks = new();

    /// <summary>
    /// Where the last scan ran. A change means a different set of marks is
    /// live, even when the territory alone has not moved — stepping between
    /// instances, or visiting another world.
    /// </summary>
    private (uint TerritoryId, uint Instance, uint WorldId) _lastScannedScope;

    /// <summary>
    /// Every mark seen, of every rank. Independent of the train, and populated
    /// whether or not recording is active.
    /// </summary>
    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), OtherRankSighting> OtherRanks => _otherRanks;

    /// <summary>Raised the first time any mark is spotted, regardless of rank.</summary>
    public event Action<OtherRankSighting>? OtherRankDetected;

    /// <summary>Raised once when a mark is first picked up by scanning.</summary>
    public event Action<DetectedMark>? MarkDetected;

    /// <summary>Raised when the train is emptied, by whoever did it.</summary>
    public event System.Action? Cleared;
    public event System.Action? Removing;
    public long TrainGeneration { get; private set; }

    /// <summary>Raised at the end of every scan pass, once the sightings are current.</summary>
    public event System.Action? Scanned;
    public event Action<OtherRankSighting, DateTime>? SightingObservedDead;
    /// <summary>
    /// Raised when a mark in the train is seen at zero health, whoever killed
    /// it. The row is already flagged dead by the time this fires; it exists so
    /// the Hunt Helper-derived list can be kept in step.
    /// </summary>
    public event Action<DetectedMark>? MarkObservedDead;

    public IReadOnlyDictionary<(uint NameId, uint Instance, uint WorldId), DetectedMark> Marks => _marks;
    public uint CurrentTerritoryId => _clientState.TerritoryType;

    public MarkDetector(IObjectTable objectTable, IClientState clientState, IDataManager dataManager, Configuration config)
    {
        _objectTable = objectTable;
        _clientState = clientState;
        _dataManager = dataManager;
        _config = config;
    }

    public void Clear()
    {
        TrainGeneration++;
        _marks.Clear();
        _deathEvidence.Clear();
        _otherRanks.Clear();
        _nextOrder = 0;
        Cleared?.Invoke();
    }

    /// <summary>
    /// Marks in scouted order (or whatever order the conductor has dragged them
    /// into), which is how the train list is always displayed.
    /// </summary>
    public List<DetectedMark> Ordered() => _marks.Values.OrderBy(m => m.Order).ToList();

    /// <summary>
    /// Rewrites every mark's Order to match the given sequence. Used after a
    /// drag swap so the new arrangement sticks.
    /// </summary>
    public void ApplyOrder(IReadOnlyList<DetectedMark> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;

        _nextOrder = ordered.Count;
    }

    public void Remove((uint NameId, uint Instance, uint WorldId) key) { Removing?.Invoke(); _marks.Remove(key); }

    /// <summary>
    /// Removes every mark currently flagged dead — the equivalent of Hunt
    /// Helper's own "Remove Dead" tidy-up.
    /// </summary>
    public void RemoveDead()
    {
        Removing?.Invoke();
        foreach (var key in _marks.Where(kv => kv.Value.Dead).Select(kv => kv.Key).ToList())
            _marks.Remove(key);
    }

    /// <summary>
    /// Folds an imported list into the current one. Existing entries win, so
    /// importing never overwrites a mark you've personally seen (and possibly
    /// already marked dead). Returns how many were genuinely new.
    /// </summary>
    public int Merge(IEnumerable<DetectedMark> incoming)
    {
        var added = 0;
        var worldId = CurrentWorldId();
        var worldName = CurrentWorldName();

        foreach (var mark in incoming)
        {
            // Our own codes carry the world they were scouted on, so this only
            // catches a Hunt Helper code or one exported before that field
            // existed. Such a list is a scout for wherever you are, so it is
            // stamped with that rather than left at zero, which would make
            // every imported mark a stranger to the live one standing on the
            // same spot.
            if (mark.WorldId == 0)
            {
                mark.WorldId = worldId;
                mark.WorldName = worldName;
            }

            var key = (mark.NameId, mark.Instance, mark.WorldId);
            if (_marks.ContainsKey(key)) continue;
            mark.Order = _nextOrder++;
            _marks[key] = mark;
            added++;
        }
        return added;
    }

    /// <summary>
    /// One scan pass. Adds any newly sighted A-rank marks and refreshes the last
    /// seen time on ones already known. Never removes anything — a mark going out
    /// of render range shouldn't drop it from the train.
    ///
    /// When recordNew is false, marks already in the list still update, but
    /// nothing new is picked up — that's the pause button, and it's deliberately
    /// narrower than switching tracking off entirely (which would also stop
    /// kill-time recording for the marks already being tracked).
    /// </summary>
    public void Scan(bool recordNew = true)
    {
        var territoryId = _clientState.TerritoryType;
        if (territoryId == 0) { _deathEvidence.Clear(); _otherRanks.Clear(); Scanned?.Invoke(); return; }

        var mapId = GetMapId(territoryId);
        var instance = GetCurrentInstance();
        var worldId = CurrentWorldId();
        var worldName = CurrentWorldName();
        var now = DateTime.UtcNow;

        // Live sightings belong only to the current world, instance and scan.
        var scope = (territoryId, instance, worldId);
        if (scope != _lastScannedScope) { _otherRanks.Clear(); _deathEvidence.Clear(); }
        _lastScannedScope = scope;

        var visibleObjects = new HashSet<ulong>();
        foreach (var obj in _objectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc mob) continue;

            var info = ExpansionData.Lookup(mob.NameId);
            if (info == null)
            {
                // B or S rank: worth showing on the map, never joins the train.
                TrackSighting(mob, territoryId, mapId, instance, worldId, worldName, now, null);
                continue;
            }

            // An A-rank is always recorded as a sighting, so the map and the
            // detection echo work even with train recording paused. Whether it
            // also joins the train is decided below.
            TrackSighting(mob, territoryId, mapId, instance, worldId, worldName, now, HuntRank.A);

            visibleObjects.Add(mob.GameObjectId);
            var witnessedDeath = _deathEvidence.Observe(mob.GameObjectId, mob.CurrentHp, mob.MaxHp);
            if (mob.MaxHp == 0) continue;
            var key = (mob.NameId, instance, worldId);
            if (_marks.TryGetValue(key, out var existing))
            {
                if (!IsDead(mob)) existing.LastSeenUtc = now;
                existing.MapPosition = MapCoordinates.FromWorld(_dataManager, mapId, mob.Position.X, mob.Position.Z);

                // Zero health is the death itself, seen rather than inferred,
                // and it carries as far as the object table does. The battle
                // log only reaches as far as the fight, so a mark killed across
                // the zone never produced a line and stayed lit.
                if (IsDead(mob) && !existing.Dead && _config.MarkDeadOnObservedDefeat)
                {
                    existing.Dead = true;
                    existing.DeathObservedAtUtc = witnessedDeath ? now : null;
                    if (witnessedDeath) MarkObservedDead?.Invoke(existing);
                }

                continue;
            }

            if (!recordNew) continue;

            _marks[key] = new DetectedMark
            {
                WorldId = worldId,
                WorldName = worldName,
                Name = mob.Name.TextValue,
                NameId = mob.NameId,
                TerritoryId = territoryId,
                MapId = mapId,
                Instance = instance,
                MapPosition = MapCoordinates.FromWorld(_dataManager, mapId, mob.Position.X, mob.Position.Z),
                Dead = IsDead(mob),
                FirstSeenUtc = now,
                LastSeenUtc = now,
                Order = _nextOrder++,
            };

            MarkDetected?.Invoke(_marks[key]);
        }

        _deathEvidence.Retain(visibleObjects);
        // A live icon needs an object in this exact pass. Train history is kept separately.
        foreach (var key in _otherRanks.Where(p => p.Value.LastSeenUtc != now).Select(p => p.Key).ToList())
            _otherRanks.Remove(key);
        Scanned?.Invoke();
    }

    /// <summary>
    /// Map ID is derived from the territory via the game's own data sheet — it
    /// is NOT a number visible anywhere in the UI, which is exactly why an
    /// earlier hand-entered version of this always produced a flag in the
    /// corner of the map.
    /// </summary>
    /// <summary>
    /// Adds a conductor-placed flag as a row in the train. Returns null if no
    /// flag is currently set.
    /// </summary>
    public DetectedMark? AddCustomFlag(string label)
    {
        if (!FlagCapture.TryGetCurrentFlag(out var territoryId, out var mapId, out var x, out var y))
            return null;

        var mark = new DetectedMark
        {
            Name = string.IsNullOrWhiteSpace(label) ? "Custom Flag" : label,
            NameId = _nextCustomId--,
            TerritoryId = territoryId,
            MapId = mapId,
            Instance = 0,
            MapPosition = MapCoordinates.FromWorld(_dataManager, mapId, x, y),
            Dead = false,
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow,
            Order = _nextOrder++,
            IsCustom = true,
            ZoneName = GetZoneName(territoryId),
            WorldId = CurrentWorldId(),
            WorldName = CurrentWorldName(),
        };

        _marks[(mark.NameId, mark.Instance, mark.WorldId)] = mark;
        return mark;
    }

    private void TrackSighting(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc mob,
        uint territoryId, uint mapId, uint instance, uint worldId, string worldName,
        DateTime now, HuntRank? knownRank)
    {
        HuntRank rank;
        if (knownRank is { } r)
        {
            rank = r;
        }
        else
        {
            var other = OtherRankData.Lookup(mob.NameId);
            if (other == null) return;
            rank = other.Rank;
        }

        var key = (mob.NameId, instance, worldId);

        // A corpse is not a mark that is up. Drop it rather than leaving a dot
        // on the map over something already dead, and do not re-add it as the
        // body lingers.
        if (IsDead(mob))
        {
            if (_otherRanks.TryGetValue(key, out var dying))
                SightingObservedDead?.Invoke(dying, now);
            _otherRanks.Remove(key);
            return;
        }

        if (_otherRanks.TryGetValue(key, out var existing))
        {
            existing.LastSeenUtc = now;
            existing.MapPosition = MapCoordinates.FromWorld(_dataManager, mapId, mob.Position.X, mob.Position.Z);
            existing.HealthPercent = HealthPercentOf(mob);
            if (existing.SpawnPointIndex is null && CanMatchSpawnPoint(mob))
                existing.SpawnPointIndex = MatchSpawnPoint(territoryId, existing.MapPosition, rank);
            return;
        }

        var sighting = new OtherRankSighting
        {
            WorldId = worldId,
            WorldName = worldName,
            Name = mob.Name.TextValue,
            NameId = mob.NameId,
            Rank = rank,
            TerritoryId = territoryId,
            MapId = mapId,
            Instance = instance,
            MapPosition = MapCoordinates.FromWorld(_dataManager, mapId, mob.Position.X, mob.Position.Z),
            LastSeenUtc = now,
            HealthPercent = HealthPercentOf(mob),
            SpawnPointIndex = CanMatchSpawnPoint(mob)
                ? MatchSpawnPoint(territoryId, MapCoordinates.FromWorld(_dataManager, mapId, mob.Position.X, mob.Position.Z), rank) : null,
            ZoneName = GetZoneName(territoryId),
        };

        _otherRanks[key] = sighting;
        OtherRankDetected?.Invoke(sighting);
    }

    /// <summary>
    /// A mark's remaining health as a percentage.
    ///
    /// MaxHp reads as zero for an object the game has not finished filling in,
    /// which would divide to infinity; an unknown mark is reported as untouched
    /// rather than as a mark on the point of dying.
    /// </summary>
    private static bool CanMatchSpawnPoint(Dalamud.Game.ClientState.Objects.Types.IBattleNpc mob) =>
        !mob.StatusFlags.HasFlag(Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat)
        && mob.MaxHp > 0 && mob.CurrentHp == mob.MaxHp;

    private static int? MatchSpawnPoint(uint territory, Vector2 position, HuntRank rank) =>
        Sync.SpawnMapping.Match(SpawnPointData.For(territory), position,
            rank == HuntRank.S ? SpawnRanks.S : rank == HuntRank.A ? SpawnRanks.A : SpawnRanks.B);

    private static float HealthPercentOf(Dalamud.Game.ClientState.Objects.Types.IBattleNpc mob)
    {
        if (mob.MaxHp == 0) return 100f;
        return Math.Clamp(mob.CurrentHp / (float)mob.MaxHp * 100f, 0f, 100f);
    }

    /// <summary>
    /// Whether the game is reporting this mob as dead.
    ///
    /// MaxHp is checked as well as CurrentHp on purpose. An object the game has
    /// not finished filling in reads zero for both, and calling that dead would
    /// tick a mark off the train the moment it came into range.
    /// </summary>
    private static bool IsDead(Dalamud.Game.ClientState.Objects.Types.IBattleNpc mob) =>
        mob.MaxHp > 0 && mob.CurrentHp == 0;

    /// <summary>Clears all sightings.</summary>
    public void ClearOtherRanks() => _otherRanks.Clear();

    /// <summary>
    /// Forgets one sighting, e.g. when a mark is known to be dead.
    ///
    /// The world is a parameter rather than "wherever we are", because the row
    /// being ticked dead is not necessarily on this world — and defaulting to
    /// here quietly cleared the wrong world's dot.
    /// </summary>
    public void RemoveSighting(uint nameId, uint instance, uint worldId) =>
        _otherRanks.Remove((nameId, instance, worldId));

    /// <summary>Zone name straight from the game's own data, so it's always correct.</summary>
    public string GetZoneName(uint territoryId)
    {
        try
        {
            var row = _dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
            var name = row?.PlaceName.ValueNullable?.Name.ExtractText();
            return string.IsNullOrWhiteSpace(name) ? $"Territory {territoryId}" : name;
        }
        catch
        {
            return $"Territory {territoryId}";
        }
    }

    /// <summary>Snapshot of the whole train for saving to disk.</summary>
    public List<PersistedMark> ToPersisted() =>
        Ordered().Select(m => new PersistedMark
        {
            Name = m.Name,
            NameId = m.NameId,
            TerritoryId = m.TerritoryId,
            MapId = m.MapId,
            Instance = m.Instance,
            WorldId = m.WorldId,
            WorldName = m.WorldName,
            X = m.MapPosition.X,
            Y = m.MapPosition.Y,
            Dead = m.Dead,
            FirstSeenUtc = m.FirstSeenUtc,
            LastSeenUtc = m.LastSeenUtc,
            DeathObservedAtUtc = m.DeathObservedAtUtc,
            Order = m.Order,
            IsCustom = m.IsCustom,
            ZoneName = m.ZoneName,
            Spiced = m.Spiced,
            SnipedAtUtc = m.SnipedAtUtc,
        }).ToList();

    /// <summary>
    /// Restores a saved train, replacing whatever's currently held. Custom
    /// flags keep their synthetic ids, and the id counter is moved below the
    /// lowest one so new flags can't collide with restored ones.
    /// </summary>
    public void LoadPersisted(List<PersistedMark> saved)
    {
        TrainGeneration++;
        _marks.Clear();
        _nextOrder = 0;

        // A train saved before marks knew about worlds has none recorded. It
        // was scouted somewhere, and the only reasonable somewhere is where you
        // are now — without this, every restored mark would sit at world 0 and
        // walking past it would create a second copy of the same mark.
        var worldId = CurrentWorldId();
        var worldName = CurrentWorldName();

        foreach (var p in saved)
        {
            var mark = new DetectedMark
            {
                Name = p.Name,
                NameId = p.NameId,
                TerritoryId = p.TerritoryId,
                MapId = p.MapId,
                Instance = p.Instance,
                WorldId = p.WorldId == 0 ? worldId : p.WorldId,
                WorldName = p.WorldId == 0 ? worldName : p.WorldName,
                MapPosition = new Vector2(p.X, p.Y),
                Dead = p.Dead,
                FirstSeenUtc = p.FirstSeenUtc,
                LastSeenUtc = p.LastSeenUtc,
                DeathObservedAtUtc = p.DeathObservedAtUtc,
                Order = p.Order,
                IsCustom = p.IsCustom,
                ZoneName = p.ZoneName,
                Spiced = p.Spiced,
                SnipedAtUtc = p.SnipedAtUtc,
            };

            _marks[(mark.NameId, mark.Instance, mark.WorldId)] = mark;
            if (mark.Order >= _nextOrder) _nextOrder = mark.Order + 1;
            if (mark.IsCustom && mark.NameId <= _nextCustomId) _nextCustomId = mark.NameId - 1;
        }
    }

    public uint GetMapId(uint territoryId) =>
        _dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.Map.RowId ?? 0;


    /// <summary>
    /// The world the player is on, which is part of a mark's identity. Read the
    /// same way HuntCounter reads it, since its tallies are world-scoped for the
    /// same reason.
    /// </summary>
    public uint CurrentWorldId()
    {
        try
        {
            return _objectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public string CurrentWorldName()
    {
        try
        {
            var name = _objectTable.LocalPlayer?.CurrentWorld.Value.Name.ExtractText();
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Looks a mark up on the world the player is on. Callers that only have a
    /// name and an instance — a kill coming out of the tally, say — mean the
    /// mark in front of them, which is this one.
    /// </summary>
    public bool TryGetCurrentWorldMark(uint nameId, uint instance, out DetectedMark mark) =>
        _marks.TryGetValue((nameId, instance, CurrentWorldId()), out mark!);

    public static unsafe uint GetCurrentInstance()
    {
        try
        {
            var uiState = UIState.Instance();
            return uiState == null ? 0 : uiState->PublicInstance.InstanceId;
        }
        catch
        {
            return 0;
        }
    }
}
