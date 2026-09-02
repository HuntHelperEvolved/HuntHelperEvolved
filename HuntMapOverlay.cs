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
    private readonly SsEventWatcher _ssEvent;

    private readonly IDalamudPluginInterface _pluginInterface;
    private Dictionary<string, string>? _dotPaths;

    // The colours the files on disk were drawn in. A change means new files,
    // new paths, and markers that have to be re-placed to pick them up.
    private string _dotSignature = string.Empty;

    // What the last drawn set of markers was asked to show. Toggling an option
    // has to re-place them, and nothing else would notice: since the guides
    // started moving themselves, player movement no longer forces a rebuild,
    // so a toggle would otherwise sit there doing nothing until the map
    // happened to refresh for some other reason.
    private string _drawSignature = string.Empty;

    // Last known player position and facing, used when the object table cannot
    // answer for a frame. The guides read these every frame and move themselves;
    // nothing about the player triggers a rebuild any more.
    private Vector2 _lastPlayerPos;
    private float _lastPlayerRotation;

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
        SsEventWatcher ssEvent,
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
        _ssEvent = ssEvent;
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
    /// Marks where an SS event's minions were found.
    ///
    /// These outlive the minions themselves, which is the whole point — see
    /// SsEventWatcher. Drawn a little larger again than an off-point mark,
    /// because it is the one thing on this map that is not a live position but
    /// a remembered one.
    /// </summary>
    private int DrawSsEventPins(uint mapId, Dictionary<string, string> dots)
    {
        if (_overlay == null || !_config.ShowSsEventOnMap) return 0;
        if (!_ssEvent.Active || _ssEvent.Pins.Count == 0) return 0;

        var placed = 0;

        foreach (var pin in _ssEvent.Pins)
        {
            var world = MapCoordinates.ToWorld(
                _dataManager, mapId, pin.MapPosition.X, pin.MapPosition.Y);

            _overlay.AddMarker(new MapMarkerNode
            {
                AllowAnyMap = false,
                MapId = mapId,
                Position = world,
                TexturePath = dots["ssminion"],
                Size = new Vector2(_config.SpawnDotSize * 1.5f, _config.SpawnDotSize * 1.5f),
                TextTooltip = $"SS event — {pin.Name} seen here\n"
                              + $"{pin.MapPosition.X:F1}, {pin.MapPosition.Y:F1}\n"
                              + "Stays until the mark spawns or you leave the zone.",
            });
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Draws live marks that are not sitting on any known spawn point, at
    /// wherever they actually are.
    ///
    /// This is how an SS shows up. An SS spawns on its own spot, one no S rank
    /// uses, and those spots are not in the spawn point data — so there is no
    /// dot at that location to light up, and until now a live SS simply did not
    /// appear on the map at all. Rather than special-case the rank, anything
    /// the claiming step could not place gets drawn where it is: the same gap
    /// swallows a mark that spawned somewhere unlisted, or one whose nearest
    /// point was already taken by a higher rank, and a live mark should never
    /// be invisible.
    ///
    /// Drawn a little larger than a spawn point, and after them, so it is clear
    /// this is the mark itself rather than a point it happens to be near.
    /// </summary>
    private int DrawMarksOffSpawnPoints(
        uint mapId, Dictionary<string, string> dots, IEnumerable<OtherRankSighting> unclaimed)
    {
        if (_overlay == null) return 0;

        var placed = 0;

        foreach (var sighting in unclaimed)
        {
            // Same filters the spawn points use, so turning a rank off turns
            // it off everywhere rather than only half of the map.
            var wanted = sighting.Rank switch
            {
                HuntRank.A => _config.ShowARankPoints,
                HuntRank.B => _config.ShowBRankPoints,
                _ => _config.ShowSRankPoints,
            };
            if (!wanted) continue;

            var dot = sighting.Rank switch
            {
                HuntRank.S => "s",
                HuntRank.A => "a",
                _ => "b",
            };

            var world = MapCoordinates.ToWorld(
                _dataManager, mapId, sighting.MapPosition.X, sighting.MapPosition.Y);

            _overlay.AddMarker(new MapMarkerNode
            {
                AllowAnyMap = false,
                MapId = mapId,
                Position = world,
                TexturePath = dots[dot],
                Size = new Vector2(_config.SpawnDotSize * 1.35f, _config.SpawnDotSize * 1.35f),
                TextTooltip = $"{sighting.Name}  ({sighting.Rank} rank) — UP\n"
                              + $"{sighting.MapPosition.X:F1}, {sighting.MapPosition.Y:F1}\n"
                              + "Not on a known spawn point.",
            });
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// The player's position on the map, or the last one known when they are
    /// briefly unavailable — better a marker that holds still for a frame than
    /// one that snaps to the middle of the map.
    /// </summary>
    private Vector2 PlayerPosition()
    {
        var player = _objectTable.LocalPlayer;
        if (player is not null)
            _lastPlayerPos = new Vector2(player.Position.X, player.Position.Z);

        return _lastPlayerPos;
    }

    /// <summary>Player facing in radians, with the same fallback.</summary>
    private float PlayerRotation()
    {
        var player = _objectTable.LocalPlayer;
        if (player is not null)
            _lastPlayerRotation = player.Rotation;

        return _lastPlayerRotation;
    }

    /// <summary>
    /// Forward as a world X/Z vector. The game's convention is (sin, cos), with
    /// zero facing south, which is +Z, and +Z is down the map.
    /// </summary>
    private Vector2 PlayerForward()
    {
        var rotation = PlayerRotation();
        return new Vector2(MathF.Sin(rotation), MathF.Cos(rotation));
    }

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

        var radius = DetectionRadiusWorld(mapId);
        if (radius <= 0f) return 0;

        var placed = 0;

        // The path goes under the ring, matching the order Hunt Helper draws in.
        if (_config.ShowPlayerFacingOnMap)
        {
            var path = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                TexturePath = dots["facing"],
                WorldSize = new Vector2(ProjectedPathLength, radius * 2f),
                TextTooltip = "Projected path",

                // Centred half its length ahead, so it starts at the player and
                // runs forward rather than straddling them.
                PositionProvider = () => PlayerPosition() + (PlayerForward() * (ProjectedPathLength / 2f)),

                // The quad's length runs along its own +X, and a node's rotation
                // is clockwise on screen, so +X lands on (cos, sin). Solving that
                // against the game's forward vector of (sin, cos) gives this.
                RotationProvider = () => (MathF.PI / 2f) - PlayerRotation(),
            };

            _overlay.AddMarker(path);
            placed++;
        }

        // Between the path and the ring, matching Hunt Helper's order. The fill
        // only appears when the path does: on its own the ring is a range
        // marker and wants to be an outline, but alongside the path the two are
        // one shape — the swathe you are about to sweep, and the part of it you
        // are standing in. Filling it in the path's own colour is what joins
        // them up.
        if (_config.ShowPlayerCircleOnMap && _config.ShowPlayerFacingOnMap)
        {
            var fill = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                TexturePath = dots["fill"],
                WorldSize = new Vector2(radius * 2f, radius * 2f),
                TextTooltip = "Detection range",
                PositionProvider = PlayerPosition,
            };

            _overlay.AddMarker(fill);
            placed++;
        }

        if (_config.ShowPlayerCircleOnMap)
        {
            var ring = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                TexturePath = dots["circle"],
                WorldSize = new Vector2(radius * 2f, radius * 2f),
                TextTooltip = "Detection range",
                PositionProvider = PlayerPosition,
            };

            _overlay.AddMarker(ring);
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Everything that decides WHICH markers get placed, as a string. Compared
    /// each frame so that ticking any of these boxes shows up at once.
    /// </summary>
    private string DrawSignature() =>
        $"{_config.ShowSpawnPointsOnMap}{_config.ShowARankPoints}{_config.ShowBRankPoints}"
        + $"{_config.ShowSRankPoints}{_config.ShowPlayerCircleOnMap}"
        + $"{_config.ShowPlayerFacingOnMap}{_config.ShowSsEventOnMap}{_ssEvent.Pins.Count}{_config.SpawnDotSize}";

    /// <summary>
    /// The configured colours, as a string. Used both to name the files and to
    /// notice when they need drawing again.
    /// </summary>
    private string DotSignature() =>
        DotTextures.HexOf(_config.SpawnDotColourEmpty) + "-"
        + DotTextures.HexOf(_config.SpawnDotColourB) + "-"
        + DotTextures.HexOf(_config.SpawnDotColourA) + "-"
        + DotTextures.HexOf(_config.SpawnDotColourS) + "-"
        + DotTextures.HexOf(_config.SsMinionColour) + "-"
        + DotTextures.HexOf(_config.PlayerCircleColour) + "t"
        + ((int)_config.PlayerCircleThickness).ToString() + "-"
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

            // Kind and revision are in the file name alongside the colour, and
            // both matter. Naming these by colour alone was a real bug: when the
            // ring stopped being a filled dot and became an outline, and the
            // guide became a stretched block, the names did not move with them —
            // so the existence check kept handing back the old dots. The ring
            // came out solid and the path came out a lens, because that is what
            // a dot looks like stretched across a long quad.
            //
            // Bump Revision when what a kind DRAWS changes at the same colour.
            var wanted = new List<(string Key, string Name, Func<byte[]> Render)>
            {
                Texture("empty", "dot", _config.SpawnDotColourEmpty,
                    c => DotTextures.Render(c)),
                Texture("b", "dot", _config.SpawnDotColourB,
                    c => DotTextures.Render(c)),
                Texture("a", "dot", _config.SpawnDotColourA,
                    c => DotTextures.Render(c)),
                Texture("s", "dot", _config.SpawnDotColourS,
                    c => DotTextures.Render(c)),
                Texture("ssminion", "dot", _config.SsMinionColour,
                    c => DotTextures.Render(c)),

                // Not dots: an outline stretched to the circle's width, and a
                // flat block of colour stretched into the path band.
                // Thickness is in the name too: it changes what the ring
                // draws without changing its colour, which is exactly the case
                // that served up a stale image last time.
                Texture("circle", $"ring{(int)_config.PlayerCircleThickness}",
                    _config.PlayerCircleColour,
                    c => DotTextures.RenderRing(c, strokePixels: _config.PlayerCircleThickness)),

                // The ring's fill, in the path's colour so the two read as one
                // shape. Drawn large for the same reason the ring is: it is
                // stretched to the circle's width, not shown at icon size.
                Texture("fill", "disc", _config.PlayerFacingColour,
                    c => DotTextures.Render(c, 256)),
                Texture("facing", "band", _config.PlayerFacingColour,
                    c => DotTextures.RenderSolid(c)),
            };

            var paths = new Dictionary<string, string>();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, name, render) in wanted)
            {
                var path = Path.Combine(dir, name);

                if (!File.Exists(path))
                    File.WriteAllBytes(path, render());

                paths[key] = path;
                keep.Add(name);
            }

            PruneStaleDots(dir, keep);

            _dotPaths = paths;
            _dotSignature = signature;
            return _dotPaths;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not write the spawn point map images.");
            return null;
        }
    }

    /// <summary>
    /// Bumped when an existing kind's drawing changes but its name would not,
    /// so the files on disk are replaced rather than reused.
    /// </summary>
    private const int TextureRevision = 2;

    private static (string Key, string Name, Func<byte[]> Render) Texture(
        string key, string kind, System.Numerics.Vector4 colour, Func<System.Numerics.Vector4, byte[]> render) =>
        (key,
         $"{key}-{kind}{TextureRevision}-{DotTextures.HexOf(colour)}.png",
         () => render(colour));

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

            var drawSignature = DrawSignature();
            if (drawSignature != _drawSignature)
            {
                _drawSignature = drawSignature;
                _needsRefresh = true;
            }

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
                // An SS event is not a spawn point, so it is not switched off
                // with them.
                var pinsOnly = DrawSsEventPins(mapId, dots);

                Status = "Spawn points off."
                         + (pinsOnly > 0 ? $" {pinsOnly} SS event." : string.Empty)
                         + (guides > 0 ? $" {guides} guide markers." : string.Empty);
                return;
            }

            var points = SpawnPointData.For(territory);

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

            // Whatever fails to claim a point is not lost — it gets drawn where
            // it really is instead. An SS is always in here, because its spawn
            // spot is not one of the points.
            var unclaimed = new List<OtherRankSighting>();

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
                    else
                        unclaimed.Add(sighting);
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

                _overlay.AddMarker(new MapMarkerNode
                {
                    AllowAnyMap = false,
                    MapId = mapId,
                    Position = world,
                    TexturePath = dots[dot],
                    Size = new Vector2(_config.SpawnDotSize, _config.SpawnDotSize),
                    TextTooltip = tooltip,
                });
                placed++;
            }

            var pins = DrawSsEventPins(mapId, dots);
            var offPoint = DrawMarksOffSpawnPoints(mapId, dots, unclaimed);

            Status = $"{placed} spawn points shown, {occupied} with a mark on them."
                     + (pins > 0 ? $" {pins} SS event." : string.Empty)
                     + (offPoint > 0 ? $" {offPoint} off-point." : string.Empty)
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
