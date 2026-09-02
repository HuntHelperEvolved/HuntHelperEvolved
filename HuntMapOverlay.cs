using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using KamiToolKit.MapOverlay;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using MapMarkerInfo = KamiToolKit.Classes.MapMarkerInfo;

namespace HuntTrainRelay;

/// <summary>
/// Draws A-rank spawn points onto the real in-game map, rather than in a
/// separate radar window.
///
/// PROOF OF CONCEPT — Urqopacha only. The approach is adapted from
/// EurekaTrackerAutoPopper (Infiziert90, MIT licensed), which places markers
/// through KamiToolKit's MapOverlayController and re-places them whenever the
/// AreaMap addon refreshes.
///
/// Two things worth knowing before this is expanded:
///   * KamiToolKit is our first third-party dependency. Everything else here
///     rides on Dalamud's own services.
///   * MapMarkerInfo.Position wants WORLD coordinates, but Hunt Helper's spawn
///     data is in map coordinates, so it has to be converted back — see
///     MapCoordinates.ToWorld.
/// </summary>
public sealed unsafe class HuntMapOverlay : IDisposable
{
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly MarkDetector _detector;

    private readonly IDalamudPluginInterface _pluginInterface;
    private Dictionary<string, string>? _dotPaths;

    // The colours the files on disk were drawn in. A change means new files,
    // new paths, and markers that have to be re-placed to pick them up.
    private string _dotSignature = string.Empty;

    // Where the player was when the guides were last drawn. They follow the
    // character, so something has to decide when that is worth redoing — see
    // PlayerGuidesNeedRefresh.
    private Vector2 _lastPlayerPos;
    private float _lastPlayerRotation;
    private double _secondsSincePlayerCheck;

    private MapOverlayController? _overlay;
    private bool _enabled;
    private bool _needsRefresh = true;
    private uint _lastTerritory;

    // A failure here repeats every frame, so log it once and stop rather than
    // filling /xllog with thousands of identical lines.
    private bool _faulted;
    private long _lastMarkSignature = -1;

    public string Status { get; private set; } = "Not started.";

    public HuntMapOverlay(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log,
        Configuration config,
        MarkDetector detector,
        IDalamudPluginInterface pluginInterface)
    {
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _log = log;
        _config = config;
        _detector = detector;
        _pluginInterface = pluginInterface;

        try
        {
            _addonLifecycle.RegisterListener(AddonEvent.PostRefresh, "AreaMap", OnMapRefresh);
            _framework.Update += OnUpdate;
            Status = "Waiting to start.";
        }
        catch (Exception ex)
        {
            // A failure here must not take the rest of the plugin down.
            Status = "Map overlay unavailable — see /xllog.";
            _log.Error(ex, "Could not start the map overlay.");
        }
    }

    /// <summary>
    /// The controller is created on the framework thread rather than in the
    /// plugin constructor. Constructing it off-thread left its internals
    /// unready and produced a null reference on the first update.
    /// </summary>
    private bool EnsureOverlay()
    {
        if (_overlay != null) return true;

        try
        {
            _overlay = new MapOverlayController();
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Could not create the map overlay: {ex.GetType().Name}.";
            _log.Error(ex, "MapOverlayController could not be created.");
            return false;
        }
    }

    /// <summary>
    /// The detection ring's radius, in map coordinates.
    ///
    /// Hunt Helper draws its detection circle at "2 * SingleCoordSize", where
    /// SingleCoordSize is one map coordinate — so two coordinates, and the same
    /// figure is used here so the two plugins agree about what the ring means.
    /// It is not a preference: it is the range marks are picked up at, which
    /// also matches the tally's own 100 yalm default.
    /// </summary>
    private const float DetectionRadiusCoords = 2f;

    /// <summary>
    /// How far the projected path runs, in map units. The map itself is 2048
    /// across, so this always leaves the edge of it whichever way you face —
    /// which is what "runs off into the distance" needs to mean here.
    /// </summary>
    private const float ProjectedPathLength = 4096f;

    /// <summary>
    /// Whether the ring and facing guide have drifted far enough from where
    /// they were drawn to be worth re-placing.
    ///
    /// These follow the character, so without a check they would rebuild every
    /// marker on the map on every frame. Sampled ten times a second, and only
    /// acted on once the player has actually moved half a yalm or turned about
    /// three degrees — standing still costs nothing at all.
    /// </summary>
    private bool PlayerGuidesNeedRefresh(IFramework framework)
    {
        if (!_config.ShowPlayerCircleOnMap && !_config.ShowPlayerFacingOnMap)
            return false;

        // These are the only thing here that redraws because the player moved,
        // and they are hundreds of markers. With the map shut nobody can see
        // them, so nothing is rebuilt until it is open.
        if (!IsMapOpen())
            return false;

        _secondsSincePlayerCheck += framework.UpdateDelta.TotalSeconds;
        if (_secondsSincePlayerCheck < 0.1)
            return false;
        _secondsSincePlayerCheck = 0;

        var player = _objectTable.LocalPlayer;
        if (player is null)
            return false;

        var pos = new Vector2(player.Position.X, player.Position.Z);
        var moved = Vector2.Distance(pos, _lastPlayerPos) > 0.5f;

        // Only meaningful for the facing guide; the ring looks the same however
        // the character is turned.
        var turned = _config.ShowPlayerFacingOnMap
                     && MathF.Abs(WrapAngle(player.Rotation - _lastPlayerRotation)) > 0.05f;

        return moved || turned;
    }

    /// <summary>Whether the map window is on screen.</summary>
    private bool IsMapOpen()
    {
        try
        {
            var addon = _gameGui.GetAddonByName("AreaMap");
            return !addon.IsNull && addon.IsVisible;
        }
        catch
        {
            // If we cannot tell, assume it is open rather than silently
            // refusing to draw.
            return true;
        }
    }

    private static float WrapAngle(float radians)
    {
        while (radians > MathF.PI) radians -= MathF.Tau;
        while (radians < -MathF.PI) radians += MathF.Tau;
        return radians;
    }

    /// <summary>
    /// Places the ring and the facing guide, returning how many markers went
    /// down. Both are built from the same small dots as everything else, since
    /// a map marker is a texture at a position — there is no line to draw with
    /// and no way to rotate one, so a line has to be made of points.
    ///
    /// Positions are world coordinates, which is what MapMarkerInfo wants, so
    /// the radius is in yalms and stays honest at any zoom rather than being a
    /// fixed number of screen pixels.
    /// </summary>
    /// <summary>
    /// The detection radius in map units, derived from the map itself rather
    /// than assumed, by asking what two map coordinates are worth here.
    /// </summary>
    private float DetectionRadiusWorld(uint mapId)
    {
        var origin = MapCoordinates.ToWorld(_dataManager, mapId, 1f, 1f);
        var offset = MapCoordinates.ToWorld(_dataManager, mapId, 1f + DetectionRadiusCoords, 1f);
        return MathF.Abs(offset.X - origin.X);
    }

    /// <summary>
    /// The map's current marker scaling factor, which WorldSizedMarker needs to
    /// undo the constant-size counter-scaling every marker is given.
    /// </summary>
    private float MarkerPositionScaling()
    {
        try
        {
            var addon = (FFXIVClientStructs.FFXIV.Client.UI.AddonAreaMap*)
                _gameGui.GetAddonByName("AreaMap").Address;
            if (addon == null) return 1f;

            var scaling = addon->AreaMap.MarkerPositionScaling;
            return scaling > 0f ? scaling : 1f;
        }
        catch
        {
            return 1f;
        }
    }

    /// <summary>
    /// Places the detection ring and the projected path, returning how many
    /// markers went down.
    ///
    /// Two markers, not two hundred. Both are WorldSizedMarkers, so they are
    /// measured in map units and keep their meaning at any zoom: the ring stays
    /// the same number of yalms across, and the path stays exactly as wide as
    /// the ring.
    ///
    /// The path is one rotated quad rather than a row of sprites because it is
    /// translucent. Overlapping translucent shapes compound their alpha, so a
    /// band built from pieces would be visibly blotched where they met, and a
    /// band built from pieces that only touch would be scalloped along its
    /// edges. A single quad is flat, which is what Hunt Helper's AddLine gives.
    /// </summary>
    private int DrawPlayerGuides(uint mapId, Dictionary<string, string> dots)
    {
        if (_overlay == null) return 0;
        if (!_config.ShowPlayerCircleOnMap && !_config.ShowPlayerFacingOnMap) return 0;

        var player = _objectTable.LocalPlayer;
        if (player is null) return 0;

        var centre = new Vector2(player.Position.X, player.Position.Z);
        _lastPlayerPos = centre;
        _lastPlayerRotation = player.Rotation;

        var radius = DetectionRadiusWorld(mapId);
        if (radius <= 0f) return 0;

        var placed = 0;

        // The path goes under the ring, matching the order Hunt Helper draws in.
        if (_config.ShowPlayerFacingOnMap)
        {
            // Rotation is radians about the vertical axis, and the game's own
            // convention puts forward at (sin, cos) in world X/Z — zero facing
            // south, which is +Z, and +Z is down the map.
            var forward = new Vector2(MathF.Sin(player.Rotation), MathF.Cos(player.Rotation));

            // The quad's length runs along its own +X, and a node's rotation is
            // clockwise on screen, so +X lands on (cos, sin). Solving that
            // against forward gives this angle. Negate it if the band ever comes
            // out mirrored.
            var rotation = (MathF.PI / 2f) - player.Rotation;

            // Centred half its length ahead, so it starts at the player and runs
            // forward rather than straddling them.
            var midpoint = centre + (forward * (ProjectedPathLength / 2f));

            var path = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                Position = midpoint,
                TexturePath = dots["facing"],
                WorldSize = new Vector2(ProjectedPathLength, radius * 2f),
                Rotation = rotation,
                TextTooltip = "Projected path",
            };

            _overlay.AddMarker(path);
            placed++;
        }

        if (_config.ShowPlayerCircleOnMap)
        {
            var ring = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                Position = centre,
                TexturePath = dots["circle"],
                WorldSize = new Vector2(radius * 2f, radius * 2f),
                TextTooltip = "Detection range",
            };

            _overlay.AddMarker(ring);
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// The configured colours, as a string. Used both to name the files and to
    /// notice when they need drawing again.
    /// </summary>
    private string DotSignature() =>
        DotTextures.HexOf(_config.SpawnDotColourEmpty) + "-"
        + DotTextures.HexOf(_config.SpawnDotColourB) + "-"
        + DotTextures.HexOf(_config.SpawnDotColourA) + "-"
        + DotTextures.HexOf(_config.SpawnDotColourS) + "-"
        + DotTextures.HexOf(_config.PlayerCircleColour) + "-"
        + DotTextures.HexOf(_config.PlayerFacingColour);

    /// <summary>
    /// Draws the dots in the configured colours and writes them to the
    /// plugin's config folder, returning their paths. KamiToolKit takes a file
    /// path, so they have to exist on disk.
    ///
    /// The colour is part of the file name on purpose. Textures are cached by
    /// path, so rewriting the same name with new pixels would keep handing back
    /// the old image; a new colour has to mean a new path to be picked up.
    /// Files for colours no longer in use are pruned as we go, since otherwise
    /// every colour ever tried would stay in that folder.
    /// </summary>
    private Dictionary<string, string>? EnsureDotFiles()
    {
        var signature = DotSignature();
        if (_dotPaths != null && _dotSignature == signature) return _dotPaths;

        try
        {
            var dir = Path.Combine(_pluginInterface.GetPluginConfigDirectory(), "dots");
            Directory.CreateDirectory(dir);

            var colours = new Dictionary<string, System.Numerics.Vector4>
            {
                ["empty"] = _config.SpawnDotColourEmpty,
                ["b"] = _config.SpawnDotColourB,
                ["a"] = _config.SpawnDotColourA,
                ["s"] = _config.SpawnDotColourS,
            };

            // The guides are not dots: a ring outline stretched to the circle's
            // width, and a flat block of colour stretched into the path band.
            var shapes = new Dictionary<string, Func<byte[]>>
            {
                ["circle"] = () => DotTextures.RenderRing(_config.PlayerCircleColour),
                ["facing"] = () => DotTextures.RenderSolid(_config.PlayerFacingColour),
            };

            var paths = new Dictionary<string, string>();
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (state, colour) in colours)
            {
                var name = $"{state}-{DotTextures.HexOf(colour)}.png";
                var path = Path.Combine(dir, name);

                if (!File.Exists(path))
                    File.WriteAllBytes(path, DotTextures.Render(colour));

                paths[state] = path;
                wanted.Add(name);
            }

            foreach (var (state, render) in shapes)
            {
                var colour = state == "circle"
                    ? _config.PlayerCircleColour
                    : _config.PlayerFacingColour;

                var name = $"{state}-{DotTextures.HexOf(colour)}.png";
                var path = Path.Combine(dir, name);

                if (!File.Exists(path))
                    File.WriteAllBytes(path, render());

                paths[state] = path;
                wanted.Add(name);
            }

            PruneStaleDots(dir, wanted);

            _dotPaths = paths;
            _dotSignature = signature;
            return _dotPaths;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not write the spawn point dot images.");
            return null;
        }
    }

    /// <summary>
    /// Removes dot images the current colours no longer use. Best effort: a
    /// file still held open by the texture cache simply stays, and is caught
    /// the next time round.
    /// </summary>
    private void PruneStaleDots(string dir, HashSet<string> keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
            {
                if (keep.Contains(Path.GetFileName(file)))
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // In use, or gone already. Neither is worth a log line.
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not tidy up old spawn point dot images.");
        }
    }

    private void OnMapRefresh(AddonEvent type, AddonArgs args) => _needsRefresh = true;

    private void OnUpdate(IFramework framework)
    {
        if (_faulted) return;

        try
        {
            // Nothing to do until the player is actually in the world.
            if (!_clientState.IsLoggedIn) return;

            unsafe
            {
                // Eureka guards this too — the map agent isn't there during
                // loading screens, and touching the overlay then throws.
                if (FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance() == null)
                    return;
            }

            if (!EnsureOverlay() || _overlay == null) return;

            if (!_config.AnyMapOverlayEnabled)
            {
                if (_enabled)
                {
                    _overlay.RemoveAllMarkers();
                    _overlay.Disable();
                    _enabled = false;
                    Status = "Off.";
                }
                return;
            }

            if (!_enabled)
            {
                _overlay.Enable();
                _enabled = true;
                _needsRefresh = true;
            }

            var territory = _clientState.TerritoryType;
            if (territory != _lastTerritory)
            {
                _lastTerritory = territory;
                _needsRefresh = true;
            }

            // Re-place when the detected marks change, so a dot lights up as
            // soon as something is found there.
            var markSignature = _detector.Marks.Count == 0
                ? 0
                : _detector.Marks.Values.Where(m => !m.Dead).Sum(m => (long)m.NameId);
            markSignature += _detector.OtherRanks.Values.Sum(o => (long)o.NameId);
            if (markSignature != _lastMarkSignature)
            {
                _lastMarkSignature = markSignature;
                _needsRefresh = true;
            }

            // Recolouring changes which file each marker points at, so the
            // markers have to be rebuilt for it to show. EnsureDotFiles below
            // redraws them and updates the signature on this same pass.
            if (DotSignature() != _dotSignature)
                _needsRefresh = true;

            if (PlayerGuidesNeedRefresh(framework))
                _needsRefresh = true;

            if (!_needsRefresh) return;
            _needsRefresh = false;

            _overlay.RemoveAllMarkers();

            var dots = EnsureDotFiles();
            if (dots == null)
            {
                Status = "Could not prepare the dot images — see /xllog.";
                return;
            }

            var mapId = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                .GetRowOrDefault(territory)?.Map.RowId ?? 0;
            if (mapId == 0)
            {
                Status = "Could not resolve the map id for this zone.";
                return;
            }

            var guides = DrawPlayerGuides(mapId, dots);

            if (!_config.ShowSpawnPointsOnMap)
            {
                Status = guides > 0
                    ? $"{guides} guide markers shown; spawn points off."
                    : "Player guides on, but nothing to draw yet.";
                return;
            }

            var points = SpawnPointData.For(territory);
            if (points.Length == 0)
            {
                Status = guides > 0
                    ? $"No spawn point data for territory {territory}; {guides} guide markers shown."
                    : $"No spawn point data for territory {territory}.";
                return;
            }

            // Live A-ranks come from the train list; B and S from the separate
            // sighting store, which never touches the train.
            // A-ranks come from sightings rather than the train, so they still
            // show with recording paused. Anything already killed in the train
            // is excluded so a dead mark doesn't stay lit.
            var deadNameIds = _detector.Marks.Values
                .Where(m => m.Dead)
                .Select(m => m.NameId)
                .ToHashSet();

            // Instance matters: Heritage Found 1 and 2 are different worlds as
            // far as marks are concerned, so sightings from one must not show
            // on the other's map.
            var instance = MarkDetector.GetCurrentInstance();

            var here = _detector.OtherRanks.Values
                .Where(o => o.TerritoryId == territory && o.Instance == instance)
                .ToList();

            var aMarks = here.Where(o => o.Rank == HuntRank.A && !deadNameIds.Contains(o.NameId)).ToList();
            var bSightings = here.Where(o => o.Rank == HuntRank.B).ToList();
            var sSightings = here.Where(o => o.Rank == HuntRank.S).ToList();

            var radius = Math.Max(0.5f, _config.SpawnPointMatchRadius);

            // Claim points per MARK rather than per point. Checking each point
            // for "is any mark near me" lit up every point within the radius —
            // Chernobog filled four dots at once. Each mark now takes only its
            // single closest point.
            var claimed = new Dictionary<int, OtherRankSighting>();

            void Claim(List<OtherRankSighting> sightings)
            {
                foreach (var sighting in sightings)
                {
                    var bestIndex = -1;
                    var bestDistance = float.MaxValue;

                    for (var i = 0; i < points.Length; i++)
                    {
                        var d = Vector2.Distance(new Vector2(points[i].X, points[i].Y), sighting.MapPosition);
                        if (d > radius || d >= bestDistance) continue;
                        bestDistance = d;
                        bestIndex = i;
                    }

                    // Higher ranks claim first, so don't overwrite them.
                    if (bestIndex >= 0 && !claimed.ContainsKey(bestIndex))
                        claimed[bestIndex] = sighting;
                }
            }

            Claim(sSightings);
            Claim(aMarks);
            Claim(bSightings);

            var placed = 0;
            var occupied = 0;

            for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                var point = points[pointIndex];

                // Only draw points that can host a rank the player wants shown.
                var wanted = SpawnRanks.None;
                if (_config.ShowARankPoints) wanted |= SpawnRanks.A;
                if (_config.ShowBRankPoints) wanted |= SpawnRanks.B;
                if (_config.ShowSRankPoints) wanted |= SpawnRanks.S;
                if ((point.Ranks & wanted) == SpawnRanks.None) continue;

                string dot;
                string tooltip;

                if (claimed.TryGetValue(pointIndex, out var mark))
                {
                    dot = mark.Rank switch
                    {
                        HuntRank.S => "s",
                        HuntRank.A => "a",
                        _ => "b",
                    };
                    tooltip = $"{mark.Name}  ({mark.Rank} rank)\n{point.X:F1}, {point.Y:F1}";
                    occupied++;
                }
                else
                {
                    dot = "empty";
                    var canSpawn = new List<string>();
                    if (point.Ranks.HasFlag(SpawnRanks.B)) canSpawn.Add("B");
                    if (point.Ranks.HasFlag(SpawnRanks.A)) canSpawn.Add("A");
                    if (point.Ranks.HasFlag(SpawnRanks.S)) canSpawn.Add("S");
                    var ranks = canSpawn.Count > 0 ? string.Join("/", canSpawn) : "?";
                    tooltip = $"Spawn point ({ranks})\n{point.X:F1}, {point.Y:F1}";
                }

                var world = MapCoordinates.ToWorld(_dataManager, mapId, point.X, point.Y);

                _overlay.AddMarker(new MapMarkerInfo
                {
                    AllowAnyMap = false,
                    MapId = mapId,
                    Position = world,
                    TexturePath = dots[dot],
                    Size = new Vector2(_config.SpawnDotSize, _config.SpawnDotSize),
                    Tooltip = tooltip,
                });
                placed++;
            }

            Status = $"{placed} spawn points shown, {occupied} with a mark on them."
                     + (guides > 0 ? $" {guides} guide markers." : string.Empty);
        }
        catch (Exception ex)
        {
            // This runs every frame, so log once and stand down rather than
            // filling /xllog with thousands of identical lines.
            var where = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "unknown";
            Status = $"Map overlay disabled after an error: {ex.GetType().Name} — {ex.Message} @ {where}";
            _log.Error(ex, "Map overlay update failed; disabling it for this session.");

            _faulted = true;
            _needsRefresh = false;

            try
            {
                _overlay?.RemoveAllMarkers();
                _overlay?.Disable();
            }
            catch
            {
                // Already broken; nothing useful to do.
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _framework.Update -= OnUpdate;
            _addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, "AreaMap", OnMapRefresh);

            if (_overlay != null)
            {
                _overlay.RemoveAllMarkers();
                _overlay.Disable();
                _overlay.Dispose();
                _overlay = null;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Map overlay did not shut down cleanly.");
        }
    }
}
