using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelperEvolved;

/// <summary>Somewhere an SS event's minions were seen, kept after they are gone.</summary>
public sealed class SsMinionPin
{
    public string Name = string.Empty;
    public uint NameId;
    public uint Instance;
    public uint WorldId;
    public Vector2 MapPosition;
    public DateTime SeenUtc;
}

/// <summary>
/// Follows an SS event from its announcement to the mark showing up.
///
/// The game says "the minions of an extraordinarily powerful mark are on the
/// hunt for prey" zone-wide when one starts, but it never says where — and
/// nothing in the game's data holds the spot either. Hunt Helper does not carry
/// it, and this plugin's own S-rank table says outright that location data was
/// deferred rather than guessed at. So the location is learned rather than
/// looked up: the announcement arms the watch, and the first sighting of the
/// minions pins where they are.
///
/// A pin outlives the minions on purpose. They are killed as the event runs,
/// and once they are gone the ordinary sighting is forgotten and the map goes
/// blank again — which loses the one thing worth remembering. The pin stays
/// until the event is genuinely over: the mark itself appears, or the zone is
/// left behind.
/// </summary>
public sealed class SsEventWatcher : IDisposable
{
    /// <summary>
    /// Minion to the mark it heralds. Both pairs are already in OtherRankData,
    /// which is where the ids come from.
    /// </summary>
    private static readonly Dictionary<uint, uint> MinionToMark = new()
    {
        [8916] = 8915,    // Forgiven Gossip     -> Forgiven Rebellion   (ShB)
        [10616] = 10615,  // Ker Shroud          -> Ker                  (EW)
        [13407] = 13406,  // crystal incarnation -> arch aethereater     (DT)
    };

    /// <summary>Whether a mark id is one of the SS minions.</summary>
    public static bool IsMinion(uint nameId) => MinionToMark.ContainsKey(nameId);

    /// <summary>Whether a mark id is the SS mark itself, the one the minions herald.</summary>
    public static bool IsSsMark(uint nameId) => MinionToMark.ContainsValue(nameId);

    /// <summary>
    /// Either half of an SS event: a minion, or the mark it leads to.
    ///
    /// Both are S ranks in OtherRankData, and neither spawns on a B/A/S spawn
    /// point — a minion stands where the event put it, and the mark appears on
    /// its own fixed spot. Anything asking "should this be snapped to a spawn
    /// point" wants this rather than the rank.
    /// </summary>
    public static bool IsSsEventMob(uint nameId) => IsMinion(nameId) || IsSsMark(nameId);

    /// <summary>
    /// Matched against the announcement. A fragment rather than the whole line,
    /// so trailing punctuation and any surrounding decoration do not matter.
    ///
    /// This is English text, and that is the honest limit of it: the message has
    /// no LogMessage id reaching us to match on instead, the way the tally
    /// matches its reward confirmation. On another client language the watch
    /// simply never arms from chat — a minion sighting still starts it, which is
    /// the path that produces a pin anyway.
    /// </summary>
    private const string AnnouncementFragment = "extraordinarily powerful mark";

    /// <summary>
    /// The channels the game itself announces on. Nothing a player can type
    /// into is here, which is the whole point: the phrase is a sentence in
    /// English, and someone saying "who's up for the extraordinarily powerful
    /// mark then" in party chat was starting an event that had not happened.
    ///
    /// An allowlist rather than a list of player channels to ignore, because
    /// the two fail in opposite directions. Miss a player channel off a
    /// blocklist and the bug is still there. Miss the real announcement channel
    /// off this and the watch simply does not arm from chat — and seeing a
    /// minion arms it anyway, which is the path that produces a pin. Being too
    /// strict here costs very little; being too loose is the bug.
    ///
    /// Echo is in the list and is not an oversight. Nothing reaches another
    /// client's echo log, so it cannot be used to start an event on someone
    /// else's screen — which is the thing being guarded against — and it gives
    /// the one way to test this without waiting for a real event:
    ///
    ///     /echo the minions of an extraordinarily powerful mark are on the hunt
    ///
    /// Worth knowing that plugins print to echo as well, so nothing here should
    /// ever print the fragment itself or it would arm its own watch.
    /// </summary>
    private static readonly XivChatType[] AnnouncementChannels =
    {
        XivChatType.SystemMessage,
        XivChatType.NPCDialogueAnnouncements,
        XivChatType.Notice,
        XivChatType.Urgent,
        XivChatType.Echo,
    };

    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;
    private readonly MarkDetector _detector;

    private readonly List<SsMinionPin> _pins = new();

    public SsEventWatcher(
        IChatGui chatGui, IClientState clientState, IPluginLog log, MarkDetector detector)
    {
        _chatGui = chatGui;
        _clientState = clientState;
        _log = log;
        _detector = detector;

        _chatGui.ChatMessage += OnChatMessage;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _detector.OtherRankDetected += OnSighting;
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _detector.OtherRankDetected -= OnSighting;
    }

    /// <summary>An SS event is running in <see cref="Territory"/>.</summary>
    public bool Active { get; private set; }

    /// <summary>The zone the event was announced or seen in.</summary>
    public uint Territory { get; private set; }

    /// <summary>Where the minions were found, if they have been.</summary>
    public IReadOnlyList<SsMinionPin> Pins => _pins;

    public string Status =>
        !Active ? "No SS event."
        : SsMinionSpawns.Known(Territory) ? "SS event up; this zone's spots are known."
        : _pins.Count > 0 ? $"SS event up, {_pins.Count} minion location(s) found."
        : "SS event up — minions not found yet, and this zone's spots aren't on file.";

    private void OnChatMessage(IChatMessage message)
    {
        try
        {
            if (Array.IndexOf(AnnouncementChannels, message.LogKind) < 0)
                return;

            var text = message.Message.TextValue;
            if (text.IndexOf(AnnouncementFragment, StringComparison.OrdinalIgnoreCase) < 0)
                return;

            Begin(_clientState.TerritoryType, "announced in chat");
        }
        catch (Exception ex)
        {
            // Never throw back into the chat pipeline.
            _log.Warning(ex, "Could not read a chat message while watching for an SS event.");
        }
    }

    private void OnSighting(OtherRankSighting sighting)
    {
        // The mark itself: the event is over, and its pins have served their
        // purpose. The mark's own dot takes over from here.
        if (MinionToMark.ContainsValue(sighting.NameId))
        {
            if (Active)
                _log.Information($"{sighting.Name} is up; clearing the SS event pins.");

            Clear();
            return;
        }

        if (!MinionToMark.ContainsKey(sighting.NameId))
            return;

        // Seeing minions is itself proof of an event, whether or not the
        // announcement was caught — logging in partway through is normal.
        Begin(sighting.TerritoryId, "minions sighted");

        var already = _pins.Exists(
            p => p.NameId == sighting.NameId
                 && p.Instance == sighting.Instance
                 && p.WorldId == sighting.WorldId);
        if (already)
            return;

        _pins.Add(new SsMinionPin
        {
            Name = sighting.Name,
            NameId = sighting.NameId,
            Instance = sighting.Instance,
            WorldId = sighting.WorldId,
            MapPosition = sighting.MapPosition,
            SeenUtc = DateTime.UtcNow,
        });

        _log.Information(
            $"Pinned an SS event location: {sighting.Name} at "
            + $"{sighting.MapPosition.X:F1}, {sighting.MapPosition.Y:F1}.");
    }

    private void Begin(uint territory, string how)
    {
        // A different zone is a different event; do not carry pins across.
        if (Active && Territory != territory)
            Clear();

        if (!Active)
            _log.Information($"SS event started in territory {territory} ({how}).");

        Active = true;
        Territory = territory;
    }

    private void OnTerritoryChanged(uint territory)
    {
        if (Active && territory != Territory)
            Clear();
    }

    /// <summary>Ends the watch and drops any pins.</summary>
    public void Clear()
    {
        Active = false;
        Territory = 0;
        _pins.Clear();
    }
}
