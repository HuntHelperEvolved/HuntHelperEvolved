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

namespace HuntHelperEvolved;

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

    // Instance and world are part of "which map am I looking at" just as much
    // as the territory is, and neither changes the territory when it changes.
    private uint _lastInstance;
    private uint _lastWorldId;

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
    private int DrawSsEventPins(uint territory, uint mapId, Dictionary<string, string> dots)
    {
        if (_overlay == null || !_config.ShowSsEventOnMap) return 0;
        if (!_ssEvent.Active) return 0;

        // The four fixed spots when this zone's are on file, which is the whole
        // set whether or not any have been visited. Otherwise fall back to the
        // ones actually found, so the feature still says something in a zone
        // that has not been filled in yet.
        var known = SsMinionSpawns.For(territory);
        var spots = known.Length > 0
            ? known.Select(p => (Position: p, Label: "Minion spawn point")).ToList()
            : _ssEvent.Pins.Select(p => (Position: p.MapPosition, Label: $"{p.Name} seen here")).ToList();

        var placed = 0;

        // Where it is all heading, drawn first so a minion spot sitting on top
        // of it still reads.
        if (SsMinionSpawns.MarkSpawnFor(territory) is { } markSpawn)
        {
            var markWorld = MapCoordinates.ToWorld(_dataManager, mapId, markSpawn.X, markSpawn.Y);

            _overlay.AddMarker(new MapMarkerNode
            {
                AllowAnyMap = false,
                MapId = mapId,
                Position = markWorld,
                TexturePath = dots["ssmark"],
                Size = new Vector2(_config.SpawnDotSize * 2.2f, _config.SpawnDotSize * 2.2f),
                TextTooltip = "SS event — the mark spawns here\n"
                              + $"{markSpawn.X:F1}, {markSpawn.Y:F1}\n"
                              + "Once all four minions are down.",
            });
            placed++;
        }

        foreach (var (position, label) in spots)
        {
            var world = MapCoordinates.ToWorld(_dataManager, mapId, position.X, position.Y);

            _overlay.AddMarker(new MapMarkerNode
            {
                AllowAnyMap = false,
                MapId = mapId,
                Position = world,
                TexturePath = dots["ssminion"],
                Size = new Vector2(_config.SpawnDotSize * 1.5f, _config.SpawnDotSize * 1.5f),
                TextTooltip = $"SS event — {label}\n"
                              + $"{position.X:F1}, {position.Y:F1}\n"
                              + "Stays until the mark spawns or you leave the zone.",
            });
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Draws every mark that is up, at the position it is actually standing on.
    ///
    /// No mark is snapped to a spawn point any more. A mark near a point is
    /// only NEAR it — up to the old match radius away, a couple of map
    /// coordinates — and drawing it on the point sent anyone following the map
    /// to the wrong place. An SS event's mobs never spawn on one of those
    /// points at all, so for them the dot could be somewhere they had no
    /// business being.
    ///
    /// Drawn a little larger than a spawn point, and after them, so a mark
    /// standing on top of its point still reads as the mark rather than
    /// disappearing into the dot underneath.
    /// </summary>
    private int DrawLiveMarks(
        uint mapId, Dictionary<string, string> dots, IEnumerable<OtherRankSighting> live)
    {
        if (_overlay == null) return 0;

        var placed = 0;

        foreach (var sighting in live)
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

            // Minions come through as S ranks, so without this a live one would
            // be an ordinary green dot and read as the S itself.
            var dot = SsEventWatcher.IsMinion(sighting.NameId)
                ? "ssminion"
                : sighting.Rank switch
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
                              + $"{sighting.MapPosition.X:F1}, {sighting.MapPosition.Y:F1}",
            });
            placed++;

            AddMarkLabel(mapId, sighting.MapPosition, sighting, _config.SpawnDotSize * 1.35f);
        }

        return placed;
    }

    /// <summary>
    /// Writes a live mark's name and health next to it.
    ///
    /// The sighting is captured rather than copied out of, because the detector
    /// keeps updating that same object on every scan — so the label reads the
    /// current health each frame without anything having to be rebuilt.
    /// </summary>
    /// <param name="dotSize">
    /// The dot this label belongs to, so the text clears it. Off-point marks
    /// are drawn larger than spawn points and their labels have to drop
    /// further to match.
    /// </param>
    private void AddMarkLabel(uint mapId, Vector2 mapPosition, OtherRankSighting sighting, float dotSize)
    {
        if (_overlay == null || !_config.ShowMarkLabelsOnMap) return;

        var world = MapCoordinates.ToWorld(_dataManager, mapId, mapPosition.X, mapPosition.Y);

        _overlay.AddMarker(new MarkLabelMarker(
            _config.MarkLabelColour,
            _config.MarkLabelOutlineColour,
            _config.MarkLabelFontSize,
            LabelWidth,
            (dotSize / 2f) + 2f)
        {
            AllowAnyMap = false,
            MapId = mapId,
            Position = world,
            TextProvider = () => $"{sighting.Name}\n{sighting.HealthPercent:0.#}%",
        });
    }

    /// <summary>
    /// How wide a label's box is. The text is centred in it and never wraps, so
    /// this only has to be wider than the longest mark name — "Sanu Vali of
    /// Dancing Wings" — at the largest font size on offer.
    /// </summary>
    private const float LabelWidth = 320f;

    /// <summary>
    /// What clicking a spawn point should do: drop the flag on the point.
    ///
    /// Useful before anything has spawned, which is the point of it — a
    /// conductor can send the group to a spot to go and look at it, rather than
    /// reading coordinates out loud. It is the spot itself being flagged, so
    /// this is the one place on the map where the flag is deliberately a fixed
    /// location rather than a live one.
    ///
    /// Returns null when the feature is off, which leaves MapMarkerNode.OnClick
    /// unset — KamiToolKit only shows the clickable cursor for markers that
    /// have one, so the map stops offering something that would not happen.
    /// </summary>
    private Action? FlagSpawnPointOnClick(uint territory, uint mapId, SpawnPoint point)
    {
        if (!_config.ClickSpawnPointToFlag) return null;

        return () =>
        {
            try
            {
                // Instance 0: a spawn point is a place, not a sighting, so it
                // carries no instance of its own. The flag lands in whichever
                // instance the map is showing, which is the one being looked at.
                MapFlagHelper.FlagPosition(_gameGui, territory, mapId, 0, point.X, point.Y);
            }
            catch (Exception ex)
            {
                // A click must never take the overlay down with it.
                _log.Warning(ex, $"Could not flag the spawn point at {point.X:F1}, {point.Y:F1}.");
            }
        };
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
        var radius = MathF.Abs(offset.X - origin.X);

        return radius * Math.Clamp(_config.PlayerCircleRadiusScale, 0.25f, 4f);
    }

    /// <summary>
    /// Whether marks happen here at all.
    ///
    /// Judged by whether the zone is in either of our own tables — the spawn
    /// points, which cover every hunt zone from ARR to Dawntrail, or the SS
    /// minion spots. A city, a dungeon or a housing ward is in neither.
    ///
    /// Deliberately not read from the game's TerritoryIntendedUse, which would
    /// be the tidier test but means guessing at what its values stand for. A
    /// zone missing from both tables is treated as none of ours, which is also
    /// the honest answer: there would be nothing to draw in it either way.
    /// </summary>
    private static bool IsHuntZone(uint territoryId) =>
        SpawnPointData.For(territoryId).Length > 0 || SsMinionSpawns.Known(territoryId);

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
    /// <param name="waiting">
    /// Set when the guides were wanted but could not be built on this pass —
    /// there is no player object yet, or the radius did not come out. The
    /// caller has to ask for another pass, because nothing else will: the
    /// player arriving is not part of any signature, so a refresh spent on a
    /// frame like this would otherwise be the last one until the map happened
    /// to refresh again. That is the teleport bug — the map left open refreshes
    /// mid-transition, this pass runs before the player exists, and the ring
    /// and path stay missing until the map is closed and opened.
    /// </param>
    private int DrawPlayerGuides(uint mapId, Dictionary<string, string> dots, out bool waiting)
    {
        waiting = false;

        if (_overlay == null) return 0;
        if (!_config.AnyPlayerGuideEnabled) return 0;

        var player = _objectTable.LocalPlayer;
        if (player is null)
        {
            waiting = true;
            return 0;
        }

        var radius = DetectionRadiusWorld(mapId);
        if (radius <= 0f)
        {
            waiting = true;
            return 0;
        }

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

        // Hunt Helper's order from here: direction line, then the player dot,
        // then the detection circle over the top of both.
        if (_config.ShowPlayerDirectionLine)
        {
            // Exactly the radius, so it runs from the middle of the ring to its
            // edge and no further.
            var length = radius;

            var line = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                TexturePath = dots["dirline"],
                WorldSize = new Vector2(
                    length,
                    radius * Math.Clamp(_config.PlayerDirectionLineThickness, 0.01f, 0.4f)),
                TextTooltip = "Facing",

                PositionProvider = () => PlayerPosition() + (PlayerForward() * (length / 2f)),
                RotationProvider = () => (MathF.PI / 2f) - PlayerRotation(),
            };

            _overlay.AddMarker(line);
            placed++;
        }

        if (_config.ShowPlayerPositionDot)
        {
            var diameter = radius * Math.Clamp(_config.PlayerPositionDotSize, 0.01f, 0.5f);

            var dot = new WorldSizedMarker(MarkerPositionScaling)
            {
                AllowAnyMap = false,
                MapId = mapId,
                TexturePath = dots["posdot"],
                WorldSize = new Vector2(diameter, diameter),
                TextTooltip = "You",
                PositionProvider = PlayerPosition,
            };

            _overlay.AddMarker(dot);
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
        + $"{_config.ShowPlayerGuides}{_config.ShowPlayerFacingOnMap}{_config.ShowPlayerDirectionLine}{_config.ShowPlayerPositionDot}{_config.ShowSsEventOnMap}{_ssEvent.Pins.Count}{_ssEvent.Active}{_config.SpawnDotSize}{_config.PlayerCircleRadiusScale}{_config.PlayerDirectionLineThickness}{_config.PlayerPositionDotSize}"
        + $"{_config.ShowMarkLabelsOnMap}{DotTextures.HexOf(_config.MarkLabelColour)}{DotTextures.HexOf(_config.MarkLabelOutlineColour)}{_config.MarkLabelFontSize}"
        + $"{_config.ClickSpawnPointToFlag}";

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
        + DotTextures.HexOf(_config.PlayerFacingColour) + "-"
        + DotTextures.HexOf(_config.PlayerDirectionLineColour) + "-"
        + DotTextures.HexOf(_config.PlayerPositionDotColour);

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

                // Where the mark itself will appear. Same colour as the minions
                // it belongs with; the shape is what tells them apart.
                Texture("ssmark", "star", _config.SsMinionColour,
                    c => DotTextures.RenderStar(c)),

                Texture("dirline", "band", _config.PlayerDirectionLineColour,
                    c => DotTextures.RenderSolid(c)),
                Texture("posdot", "disc", _config.PlayerPositionDotColour,
                    c => DotTextures.Render(c, 256)),
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

            // Nothing here draws anything worth seeing in a city or an
            // instance. The ring and the path are the reason this matters:
            // they follow the player rather than the zone's contents, so
            // without a check they would happily sweep across Limsa.
            if (!IsHuntZone(territory))
            {
                if (_enabled)
                {
                    _overlay.RemoveAllMarkers();
                    _overlay.Disable();
                    _enabled = false;
                }

                Status = "Not a hunt zone.";
                return;
            }

            // Changing instance is a different set of marks on the same map,
            // and it does not change the territory — so without this, stepping
            // between instances left whatever the last one had on screen.
            // World is here for the same reason: a world visit is a different
            // set of marks too.
            var instance = MarkDetector.GetCurrentInstance();
            var worldId = _detector.CurrentWorldId();

            if (instance != _lastInstance || worldId != _lastWorldId)
            {
                _lastInstance = instance;
                _lastWorldId = worldId;
                _needsRefresh = true;
            }

            // Re-place when the detected marks change, so a dot lights up as
            // soon as something is found there.
            //
            // Each mark contributes its whole identity rather than its name id.
            // Summing name ids could not tell one mark in two instances from
            // two marks, so a change that swapped one for the other left the
            // total identical and the map unrefreshed. Summed rather than
            // combined in sequence so the result does not depend on what order
            // the dictionaries happen to enumerate in.
            long markSignature = 0;

            foreach (var mark in _detector.Marks.Values)
            {
                if (mark.Dead) continue;
                markSignature += HashCode.Combine(mark.NameId, mark.Instance, mark.WorldId);
            }

            foreach (var sighting in _detector.OtherRanks.Values)
                markSignature += HashCode.Combine(sighting.NameId, sighting.Instance, sighting.WorldId);

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
                // The markers are already gone and the refresh already spent,
                // so leaving it here would leave the map bare for good. Ask
                // for another pass instead.
                _needsRefresh = true;
                Status = "Could not prepare the dot images — see /xllog.";
                return;
            }

            var mapId = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                .GetRowOrDefault(territory)?.Map.RowId ?? 0;
            if (mapId == 0)
            {
                // Transient during a zone change. Same reasoning as above.
                _needsRefresh = true;
                Status = "Could not resolve the map id for this zone.";
                return;
            }

            var guides = DrawPlayerGuides(mapId, dots, out var guidesWaiting);
            if (guidesWaiting) _needsRefresh = true;

            if (!_config.ShowSpawnPointsOnMap)
            {
                // An SS event is not a spawn point, so it is not switched off
                // with them.
                var pinsOnly = DrawSsEventPins(territory, mapId, dots);

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
            //
            // Keyed on the whole identity, not the name id. The same mark is up
            // in every instance and on every world at once, and they are
            // different marks: killing one in instance 1 was blanking the live
            // one in instance 2, and the one on the next world over, because
            // all three share a name id. DetectedMark.Key exists to stop
            // exactly this, and this was reaching past it.
            var deadKeys = _detector.Marks.Values
                .Where(m => m.Dead)
                .Select(m => m.Key)
                .ToHashSet();

            // instance and worldId are read further up, where a change in
            // either is what asks for this rebuild in the first place. Instance
            // matters because Heritage Found 1 and 2 are different worlds as
            // far as marks are concerned, and the same mark is up on every
            // world at once — only this one is on this map.
            var here = _detector.OtherRanks.Values
                .Where(o => o.TerritoryId == territory && o.Instance == instance
                            && o.WorldId == worldId)
                .ToList();

            // Every mark that is up, drawn below at the position it is actually
            // standing on.
            //
            // Marks used to be snapped to the nearest spawn point within a match
            // radius, and the point lit up in their colour. It read well — the
            // map said which point was taken — but it was not true: the mark is
            // only WITHIN the radius of that point, up to a couple of map
            // coordinates from where the dot sat. Walking to the dot was walking
            // to the wrong place, and for an SS event's mobs, which never spawn
            // on a B/A/S point at all, it could be a point they had no business
            // being drawn on.
            //
            // So nothing is snapped now. Spawn points stay spawn points, marks
            // are drawn where they are, and the two are separate things on the
            // map rather than one borrowing the other's position.
            var live = here
                .Where(o => o.Rank != HuntRank.A || !deadKeys.Contains(o.Key))
                .ToList();

            var placed = 0;

            for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                var point = points[pointIndex];

                // Only draw points that can host a rank the player wants shown.
                var wanted = SpawnRanks.None;
                if (_config.ShowARankPoints) wanted |= SpawnRanks.A;
                if (_config.ShowBRankPoints) wanted |= SpawnRanks.B;
                if (_config.ShowSRankPoints) wanted |= SpawnRanks.S;
                if ((point.Ranks & wanted) == SpawnRanks.None) continue;

                var canSpawn = new List<string>();
                if (point.Ranks.HasFlag(SpawnRanks.B)) canSpawn.Add("B");
                if (point.Ranks.HasFlag(SpawnRanks.A)) canSpawn.Add("A");
                if (point.Ranks.HasFlag(SpawnRanks.S)) canSpawn.Add("S");
                var ranks = canSpawn.Count > 0 ? string.Join("/", canSpawn) : "?";

                var world = MapCoordinates.ToWorld(_dataManager, mapId, point.X, point.Y);

                _overlay.AddMarker(new MapMarkerNode
                {
                    AllowAnyMap = false,
                    MapId = mapId,
                    Position = world,
                    TexturePath = dots["empty"],
                    Size = new Vector2(_config.SpawnDotSize, _config.SpawnDotSize),
                    TextTooltip = $"Spawn point ({ranks})\n{point.X:F1}, {point.Y:F1}"
                                  + (_config.ClickSpawnPointToFlag ? "\nClick to flag it." : string.Empty),
                    OnClick = FlagSpawnPointOnClick(territory, mapId, point),
                });
                placed++;
            }

            var pins = DrawSsEventPins(territory, mapId, dots);
            var marks = DrawLiveMarks(mapId, dots, live);

            Status = $"{placed} spawn points shown."
                     + (pins > 0 ? $" {pins} SS event." : string.Empty)
                     + (marks > 0 ? $" {marks} marks up." : string.Empty)
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
