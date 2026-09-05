using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace HuntHelperEvolved;

public enum SpawnStatus { Unknown, Spawned, NotSpawned }

[Serializable]
public class WebhookEntry
{
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// An S-rank watch for the current train. Label is the display text (mark
/// name, or mark name + which known spawn spot for Narrow-rift specifically).
/// </summary>
[Serializable]
public class FlagEntry
{
    public string Label { get; set; } = string.Empty;
    public SpawnStatus SpawnStatus { get; set; } = SpawnStatus.Unknown;

    /// <summary>Zone this watch belongs to, so the zone-entry reminder can match it.</summary>
    public uint TerritoryId { get; set; }

    /// <summary>Map coordinates of the chosen spawn spot, if one was picked.</summary>
    public bool HasLocation { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>
/// A train row as written to disk, so a crash or reload doesn't lose kill
/// times. Deliberately its own type rather than reusing the export shape:
/// this one keeps ordering and the exact observed death time, which the
/// interchange format has no reason to carry.
/// </summary>
[Serializable]
public class PersistedMark
{
    public string Name { get; set; } = string.Empty;
    public uint NameId { get; set; }
    public uint TerritoryId { get; set; }
    public uint MapId { get; set; }
    public uint Instance { get; set; }

    /// <summary>
    /// World it was scouted on. Part of the mark's identity — without it a
    /// saved train reloads with the same mark from two worlds merged into one.
    /// </summary>
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public bool Dead { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? DeathObservedAtUtc { get; set; }
    public int Order { get; set; }
    public bool IsCustom { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public bool Spiced { get; set; }
}

/// <summary>Per-mark auto-reset settings for the trigger-mob counters.</summary>
[Serializable]
public class CounterSettings
{
    public bool AutoResetEnabled { get; set; } = false;

    /// <summary>
    /// Hours of no contribution before the count clears. Measured from the
    /// last kill rather than the last reset, so an active grind never resets
    /// under you.
    /// </summary>
    public int AutoResetHours { get; set; } = 1;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    /// <summary>
    /// Discord webhooks. Enabled controls whether this one gets posted to at
    /// all — e.g. a testing channel can sit here disabled without needing to be
    /// removed. Label is just for your own reference (which server is which).
    /// </summary>
    public List<WebhookEntry> Webhooks { get; set; } = new() { new WebhookEntry() };

    /// <summary>Legacy field from before per-webhook Enabled/Label. Migrated once.</summary>
    [Obsolete("Use Webhooks instead. Kept only for migrating old saved configs.")]
    public List<string>? WebhookUrls { get; set; }

    /// <summary>
    /// S-rank watches for the current train. Empty at the start of every train —
    /// conductors add what they want, it clears on Reset or a successful End
    /// Train Now, same lifecycle as tracking itself.
    /// </summary>
    public List<FlagEntry> Flags { get; set; } = new();

    /// <summary>
    /// Only the conductor actively recording the train should have this on,
    /// to avoid two clients both posting the same "train complete" message.
    /// </summary>
    public bool TrackingEnabled { get; set; } = false;

    /// <summary>
    /// How often (in seconds) to check Hunt Helper's train list for changes.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>
    /// When Hunt Tally (kihtli/HuntTally) is installed, automatically mark a
    /// tracked mark dead the moment the game confirms you were credited with
    /// the kill — with Hunt Tally's exact kill timestamp rather than our
    /// poll-observed approximation. Has no effect if Hunt Tally isn't loaded.
    /// </summary>
    public bool AutoMarkDeadEnabled { get; set; } = true;

    /// <summary>
    /// Mark a train row dead when the battle log says the mark died, whoever
    /// killed it.
    ///
    /// AutoMarkDeadEnabled above only covers kills YOU were credited with,
    /// because that is all the tally can see. A mark the group brought down
    /// while you were running in, or that another train took, stayed lit as
    /// though it were still up — and the train report is built from this list.
    /// </summary>
    public bool MarkDeadOnObservedDefeat { get; set; } = true;

    /// <summary>
    /// Print a local chat reminder on entering Lakeland (Tyger), Ultima Thule
    /// (Narrow-rift) or Elpis (Ophioneus).
    /// </summary>
    /// <summary>
    /// Use our own mark detection for reports instead of Hunt Helper's list.
    /// Defaults to false so updating changes nothing until deliberately switched
    /// — both lists are always populated, so they can be compared side by side
    /// on the Train tab first.
    /// </summary>
    public bool UseOwnTrainList { get; set; } = false;

    /// <summary>
    /// Pauses picking up NEW marks, without stopping anything else — marks
    /// already in the list keep updating and still auto-mark dead from Hunt
    /// Tally. For detours into zones whose A-ranks shouldn't join the train.
    /// </summary>
    public bool ScanningPaused { get; set; } = false;

    /// <summary>Echo a mark to chat when its row is clicked in the train list.</summary>
    public bool EchoOnMarkClick { get; set; } = true;

    /// <summary>Echo to chat each time a new A-rank is picked up while scouting.</summary>
    public bool EchoOnDetection { get; set; } = false;

    /// <summary>Which ranks the detection echo covers. B is the noisy one.</summary>
    public bool EchoBRanks { get; set; } = false;
    public bool EchoARanks { get; set; } = true;
    public bool EchoSRanks { get; set; } = true;

    /// <summary>
    /// What the detection line says, per rank.
    ///
    /// These are Hunt Helper's own default messages, placeholders and all, so
    /// someone arriving from it can paste in the message they already use and
    /// get the line they already know. See MarkNotifier for the full list of
    /// placeholders; the short version is &lt;name&gt;, &lt;rank&gt;,
    /// &lt;hpp&gt;, &lt;flag&gt; and a dozen game icons.
    /// </summary>
    public string DetectionChatMessageA { get; set; } = "FOUND: <name> @ <flag> ---  <rank>  --  <hpp>";
    public string DetectionChatMessageB { get; set; } = "FOUND: <name> @ <flag> ---  <rank>  --  <hpp>";
    public string DetectionChatMessageS { get; set; } = "FOUND: <name> @ <flag> ---  <rank>  --  <hpp>";

    /// <summary>
    /// Say a detected mark out loud. Off by default, and unavailable anywhere
    /// without a Windows speech engine — MarkNotifier reports that rather than
    /// leaving a toggle that quietly does nothing.
    /// </summary>
    public bool DetectionTtsEnabled { get; set; } = false;

    public bool TtsBRanks { get; set; } = false;
    public bool TtsARanks { get; set; } = true;
    public bool TtsSRanks { get; set; } = true;

    /// <summary>Hunt Helper's own spoken defaults: nearby for a B or an A, in zone for an S.</summary>
    public string DetectionTtsMessageA { get; set; } = "<rank> Nearby";
    public string DetectionTtsMessageB { get; set; } = "<rank> Nearby";
    public string DetectionTtsMessageS { get; set; } = "<rank> in zone";

    /// <summary>Which installed voice to use. Empty means the system default.</summary>
    public string TtsVoiceName { get; set; } = string.Empty;

    public int TtsVolume { get; set; } = 100;

    /// <summary>
    /// Throw the mark's name up as fly text on your character — the channel a
    /// crit lands in, which is why it is the one thing here you cannot miss
    /// while running. Hunt Helper's third notification channel.
    /// </summary>
    public bool DetectionFlyTextEnabled { get; set; } = false;

    public bool FlyTextBRanks { get; set; } = false;
    public bool FlyTextARanks { get; set; } = true;
    public bool FlyTextSRanks { get; set; } = true;

    /// <summary>Teleporting to a mark also drops the map flag on it.</summary>
    public bool TeleportAlsoFlags { get; set; } = true;

    /// <summary>Drop the zone column in the train popout to keep it narrow.</summary>
    public bool HideZonesInPopout { get; set; } = false;

    /// <summary>Show how long ago each mark was last seen, on its row.</summary>
    public bool ShowMarkAge { get; set; } = true;

    /// <summary>
    /// When the current mark dies, move the pointer to the next live mark
    /// automatically — so a conductor can work through a train without
    /// touching the list.
    /// </summary>
    public bool AutoAdvance { get; set; } = true;

    /// <summary>Echo (and flag) the new current mark when the pointer advances.</summary>
    public bool EchoOnAdvance { get; set; } = true;

    /// <summary>
    /// Hide marks already killed from the train list display. They stay in the
    /// train and still appear in reports with their kill times — this only
    /// shortens what's on screen.
    /// </summary>
    public bool HideDeadMarks { get; set; } = false;

    /// <summary>
    /// Aetheryte ids never to route to — e.g. The Macarenses Angle in The
    /// Tempest, which looks close on a flat map but is far below everything.
    /// </summary>
    public List<uint> BlacklistedAetherytes { get; set; } = new();

    /// <summary>
    /// Whether the default blacklist has been applied once. Without this, an
    /// aetheryte someone deliberately un-blacklisted would silently come back
    /// every launch.
    /// </summary>
    public bool BlacklistSeeded { get; set; } = false;

    /// <summary>
    /// Show the "spicing" marker — a scout flagging a mark they intend to prep
    /// into a nastier rotation before the train arrives. Conductors who don't
    /// use it can turn it off and imported marks look completely ordinary.
    /// </summary>
    public bool ShowSpicing { get; set; } = true;

    /// <summary>
    /// Draw A-rank spawn points on the real in-game map. Currently a proof of
    /// concept covering Urqopacha only, and the one feature here that depends
    /// on a third-party library, so it's off by default.
    /// </summary>
    public bool ShowSpawnPointsOnMap { get; set; } = false;

    /// <summary>
    /// Map icon ids for spawn points. Configurable because there's no reliable
    /// published list of "coloured dot" icons — far quicker to try numbers
    /// in-game than to rebuild for each guess. Defaults are ids verified from
    /// other plugins: a generic pin, and the bronze/silver/gold markers, which
    /// at least differ visibly.
    /// </summary>
    /// <summary>Which ranks' spawn points to draw. ARR zones have up to sixty.</summary>
    public bool ShowARankPoints { get; set; } = true;
    public bool ShowBRankPoints { get; set; } = false;
    public bool ShowSRankPoints { get; set; } = true;

    /// <summary>
    /// Which ranks of live MARK to draw, which is a separate question from
    /// which spawn points to draw.
    ///
    /// They used to share one set of switches, from when a mark was drawn by
    /// lighting up the point it stood on — one dot, so one filter. Marks are
    /// drawn in their own right now, and the two wants pull apart: a zone's
    /// B-rank points are sixty grey dots nobody needs, while a B rank actually
    /// being up is worth seeing. Turning the points off was taking the marks
    /// with them.
    ///
    /// All three default on. A mark that is up is a handful of dots at most and
    /// is the thing the map is for; the points are the clutter, which is why B
    /// is off up there and on down here.
    /// </summary>
    public bool ShowARankMarks { get; set; } = true;
    public bool ShowBRankMarks { get; set; } = true;
    public bool ShowSRankMarks { get; set; } = true;

    /// <summary>
    /// Draw live marks at all. The companion to ShowSpawnPointsOnMap, and
    /// separate from it for the same reason the rank switches are: the points
    /// are a map of where things COULD be, the marks are what IS there, and
    /// wanting one without the other is entirely reasonable.
    /// </summary>
    public bool ShowMarksOnMap { get; set; } = true;

    /// <summary>Dot size in pixels. KamiToolKit's default marker is 32x32.</summary>
    public float SpawnDotSize { get; set; } = 16f;

    /// <summary>
    /// Dot colour for each state a spawn point can be in. Defaults are the
    /// exact colours the dots shipped as before they were configurable, so
    /// nothing changes on screen until someone picks something.
    ///
    /// Alpha is honoured, which is the point of making them editable at all —
    /// a zone with sixty ARR spawn points is a wall of solid dots, and dropping
    /// the empty ones to half opacity makes the occupied ones readable.
    /// </summary>
    public Vector4 SpawnDotColourEmpty { get; set; } = new(0.502f, 0.502f, 0.502f, 1f);

    /// <summary>Colour when a B rank is sitting on the point.</summary>
    public Vector4 SpawnDotColourB { get; set; } = new(0f, 0.549f, 0.933f, 1f);

    /// <summary>Colour when an A rank is sitting on the point.</summary>
    public Vector4 SpawnDotColourA { get; set; } = new(0.886f, 0.231f, 0.055f, 1f);

    /// <summary>Colour when an S rank is sitting on the point.</summary>
    public Vector4 SpawnDotColourS { get; set; } = new(0f, 0.827f, 0f, 1f);

    /// <summary>
    /// Click a spawn point on the map to drop the flag on it.
    ///
    /// For pointing people at a spot before anything is on it — "go and look
    /// here" — which is why it is the points that are clickable rather than the
    /// marks. A mark that is up is already visible on the map and can be
    /// flagged from the train list.
    /// </summary>
    public bool ClickSpawnPointToFlag { get; set; } = true;

    /// <summary>
    /// Write each live mark's name and remaining health on the map beside its
    /// dot, rather than leaving both in a tooltip you have to go and find.
    ///
    /// Only marks that are actually up get one — an empty spawn point has
    /// nothing to say — so this follows the rank filters rather than having its
    /// own. On by default: it is the thing the dots were always standing in for.
    /// </summary>
    public bool ShowMarkLabelsOnMap { get; set; } = true;

    /// <summary>
    /// Label colour, and the outline drawn behind it. White on black is what
    /// the game's own map labels use, and the outline is not decoration: the
    /// map runs from near-white snow to near-black caverns, and unoutlined text
    /// disappears into one end or the other.
    /// </summary>
    public Vector4 MarkLabelColour { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector4 MarkLabelOutlineColour { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Label text size. 12 is close to the game's own map lettering.</summary>
    public float MarkLabelFontSize { get; set; } = 12f;

    /// <summary>
    /// Mark where an SS event's minions were found, from the announcement until
    /// the mark appears or you leave the zone. See SsEventWatcher.
    /// </summary>
    public bool ShowSsEventOnMap { get; set; } = true;

    /// <summary>Orange by default, so it is not mistaken for a rank's dot.</summary>
    public Vector4 SsMinionColour { get; set; } = new(1f, 0.45f, 0f, 1f);

    /// <summary>
    /// Draw the detection ring around your character. Independent of the spawn
    /// points and of the facing guide — any of the three can be on by itself.
    ///
    /// The radius is not configurable because it means something: it is the
    /// range marks are actually detected at, which Hunt Helper draws at two map
    /// coordinates. A ring you could resize would just be a circle.
    /// </summary>
    public bool ShowPlayerCircleOnMap { get; set; } = false;

    public Vector4 PlayerCircleColour { get; set; } = new(1f, 1f, 0f, 0.9f);

    /// <summary>
    /// Multiplier on the detection radius. 1.00 is the real range — two map
    /// coordinates, the figure Hunt Helper draws — and is what this was fixed
    /// at before it could be changed.
    ///
    /// Hunt Helper has the same knob, _detectionCircleModifier, defaulted the
    /// same way. Worth knowing that anything other than 1.00 makes the ring
    /// stop meaning detection range, which is the reason it is a multiplier of
    /// a known figure rather than a radius you type in from nothing.
    ///
    /// The projected path takes its width from the ring, so this widens that
    /// with it.
    /// </summary>
    public float PlayerCircleRadiusScale { get; set; } = 1.00f;

    /// <summary>
    /// Ring line width, as a fraction of the ring texture's 256 pixels. It is
    /// drawn into the image rather than stroked on screen, so the line thickens
    /// and thins with the map's zoom along with the ring itself. 8 lands at
    /// roughly 3 pixels at the default zoom, which is Hunt Helper's width.
    /// </summary>
    public float PlayerCircleThickness { get; set; } = 8f;

    /// <summary>
    /// Master switch for everything drawn around your character — the range
    /// circle, the projected path, the heading line and the position dot.
    ///
    /// The four keep their own settings while it is off, the same way the rank
    /// filters keep theirs when the spawn points are switched off, so turning
    /// the lot off and back on returns exactly what was there.
    /// </summary>
    public bool ShowPlayerGuides { get; set; } = true;

    /// <summary>
    /// Draw the heading line: a short line from you to the edge of the ring.
    ///
    /// Hunt Helper's direction line, which is a separate thing from its
    /// projected path — the path is the wide translucent swathe, this is the
    /// thin one inside the circle that says which way you are pointing.
    /// </summary>
    public bool ShowPlayerDirectionLine { get; set; } = false;

    /// <summary>Hunt Helper's own direction line colour.</summary>
    public Vector4 PlayerDirectionLineColour { get; set; } = new(1f, 0.3f, 0.3f, 1f);

    /// <summary>
    /// Heading line thickness, as a fraction of the detection radius.
    ///
    /// A proportion rather than a pixel count so it holds at any zoom and
    /// follows the radius scale. Hunt Helper's own works out between about 0.06
    /// and 0.12 of its radius depending on how wide its window is dragged —
    /// its line is a flat 3 pixels while its radius grows with the window — so
    /// there is no single right answer to copy, hence the setting.
    /// </summary>
    public float PlayerDirectionLineThickness { get; set; } = 0.05f;

    /// <summary>Draw a dot on your exact position, inside the ring.</summary>
    public bool ShowPlayerPositionDot { get; set; } = false;

    /// <summary>Hunt Helper's own player icon colour.</summary>
    public Vector4 PlayerPositionDotColour { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Position dot diameter, as a fraction of the detection radius.</summary>
    public float PlayerPositionDotSize { get; set; } = 0.07f;

    /// <summary>
    /// Draw the projected path: the swathe ahead of you that your detection
    /// range will sweep if you keep walking. Exactly as wide as the ring, and
    /// long enough to leave the map, so neither is set here.
    /// </summary>
    public bool ShowPlayerFacingOnMap { get; set; } = false;

    /// <summary>
    /// Translucent by default, and by intention — it lies over the map and
    /// everything on it. Hunt Helper's own path colour.
    /// </summary>
    public Vector4 PlayerFacingColour { get; set; } = new(0.117647f, 0.5647f, 1f, 0.4f);

    /// <summary>
    /// Show the control bar pinned above the game's map window. It appears and
    /// disappears with the map, so it costs nothing while the map is shut.
    /// </summary>
    public bool ShowMapControlBar { get; set; } = true;

    /// <summary>True when any of the four player guides would be drawn.</summary>
    public bool AnyPlayerGuideEnabled =>
        ShowPlayerGuides
        && (ShowPlayerCircleOnMap || ShowPlayerFacingOnMap
            || ShowPlayerDirectionLine || ShowPlayerPositionDot);

    /// <summary>True when anything at all wants drawing on the map.</summary>
    public bool AnyMapOverlayEnabled =>
        ShowSpawnPointsOnMap || ShowMarksOnMap || ShowSsEventOnMap || AnyPlayerGuideEnabled;


    /// <summary>
    /// Count only kills you personally landed ("You defeat the X") rather than
    /// every kill in range. Both readings are legitimate — personal effort when
    /// splitting a zone with a party, versus total progress toward the spawn.
    /// </summary>
    public bool CountOnlyMyKills { get; set; } = true;

    /// <summary>
    /// Trigger-mob tallies, keyed "worldId:instance:mobName" so each world
    /// keeps its own count.
    /// </summary>
    public Dictionary<string, int> CounterTallies { get; set; } = new();

    /// <summary>
    /// Last contribution per counter, keyed "worldId:instance:markName".
    /// Drives the auto-reset window.
    /// </summary>
    public Dictionary<string, DateTime> CounterLastKill { get; set; } = new();

    /// <summary>Auto-reset settings, keyed by mark name.</summary>
    public Dictionary<string, CounterSettings> CounterConfig { get; set; } = new();

    /// <summary>
    /// The in-progress train, saved so a crash mid-train doesn't lose kill
    /// times. Cleared only by Reset or a successful End Train Now.
    /// </summary>
    public List<PersistedMark> SavedTrain { get; set; } = new();

    /// <summary>When the saved train was last written, so its age can be shown.</summary>
    public DateTime? SavedTrainAtUtc { get; set; }

    /// <summary>The current-mark pointer, saved alongside the train.</summary>
    public uint? SavedCurrentNameId { get; set; }
    public uint? SavedCurrentInstance { get; set; }
    public uint? SavedCurrentWorldId { get; set; }

    /// <summary>Height of a train list row, in pixels.</summary>
    public int TrainRowHeight { get; set; } = 22;

    public bool SRankZoneReminderEnabled { get; set; } = true;

    /// <summary>
    /// Also play a sound with that reminder. Separate toggle because the sound
    /// needs a ClientStructs call, which is a slightly less stable API surface
    /// than the rest of the plugin — if it ever misbehaves, the message can stay.
    /// </summary>
    public bool SRankZoneReminderSound { get; set; } = true;

    /// <summary>
    /// Extra names credited alongside the submitting character on a scouting
    /// report — e.g. a friend who scouted one expansion and sent you their
    /// Hunt Helper export code privately to fold into the combined report.
    /// Capped at 3 in the UI.
    /// </summary>
    public List<string> AdditionalScouts { get; set; } = new() { string.Empty };

    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    /// <summary>
    /// Reads the config file directly, for when Dalamud's own loader hands back
    /// something this assembly cannot use.
    ///
    /// GetPluginConfig resolves the "$type" line in the file —
    /// "HuntHelperEvolved.Configuration, HuntHelperEvolved" — by assembly name. On a
    /// dev-plugin reload the previous copy of this assembly is unloaded but not
    /// yet collected, and it can win that lookup. The object handed back is then
    /// the OLD assembly's Configuration: same name, same shape, different type
    /// identity, so "as Configuration" yields null and the caller silently falls
    /// back to a brand new one. Initialize() saves that immediately while seeding
    /// the aetheryte blacklist, and every setting in the file is gone — webhook
    /// URLs included. No exception is thrown anywhere along that path.
    ///
    /// Reading it here sidesteps the resolution entirely: the target type is
    /// known statically, so the "$type" hints are not needed and are ignored.
    ///
    /// MetadataPropertyHandling deliberately stays at its default. Setting it to
    /// Ignore would stop "$type" being treated as metadata, and inside a
    /// dictionary it then becomes an ordinary key — CounterTallies is
    /// Dictionary&lt;string, int&gt;, so it would fail converting "$type"'s value
    /// to an int and take the whole load down.
    /// </summary>
    public static Configuration? LoadDirect(IDalamudPluginInterface pluginInterface) =>
        LoadFromFile(pluginInterface.ConfigFile?.FullName);

    /// <summary>
    /// The config file this plugin used under its previous name.
    ///
    /// Dalamud names a plugin's config after its InternalName, so renaming the
    /// plugin moves the file and a returning user would start from defaults
    /// with everything they had configured still sitting on disk under the old
    /// name. Read once, only when there is no file under the new name, so it
    /// can never overwrite newer settings with older ones.
    /// </summary>
    private const string PreviousConfigFileName = "HuntTrainRelay.json";

    public static Configuration? LoadFromPreviousName(IDalamudPluginInterface pluginInterface)
    {
        var directory = pluginInterface.ConfigFile?.Directory;
        if (directory is null)
            return null;

        // Never when the current name already has a file — that one is the
        // truth, and this is only ever a one-time hand-over.
        if (pluginInterface.ConfigFile?.Exists == true)
            return null;

        return LoadFromFile(Path.Combine(directory.FullName, PreviousConfigFileName));
    }

    private static Configuration? LoadFromFile(string? fullPath)
    {
        try
        {
            if (fullPath is null || !File.Exists(fullPath))
                return null;

            return JsonConvert.DeserializeObject<Configuration>(
                File.ReadAllText(fullPath),
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,

                    // Replace, not Auto. Webhooks and AdditionalScouts are
                    // declared with a one-entry initialiser, and under Auto
                    // Newtonsoft keeps that entry and appends the file's rows
                    // after it — so a config would come back with a blank
                    // webhook at index 0 and everything shifted down by one,
                    // gaining another blank on every load.
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                });
        }
        catch
        {
            // Caller falls back to defaults, which is the old behaviour.
            return null;
        }
    }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

#pragma warning disable CS0618 // reading the obsolete field deliberately, once, to migrate it
        if ((Webhooks == null || Webhooks.Count == 0) && WebhookUrls is { Count: > 0 })
        {
            Webhooks = new List<WebhookEntry>();
            foreach (var url in WebhookUrls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                    Webhooks.Add(new WebhookEntry { Enabled = true, Label = string.Empty, Url = url });
            }
            WebhookUrls = null;
            Save();
        }
#pragma warning restore CS0618

        if (Webhooks == null || Webhooks.Count == 0)
        {
            Webhooks = new List<WebhookEntry> { new WebhookEntry() };
        }

        // Aetherytes that look near on a flat map but are a slog in practice.
        // Seeded once only, so removing one makes it stay removed.
        if (!BlacklistSeeded)
        {
            foreach (var id in new uint[] { 148, 181, 203 }) // Macarenses Angle, Base Omicron, Many Fires
            {
                if (!BlacklistedAetherytes.Contains(id))
                    BlacklistedAetherytes.Add(id);
            }
            BlacklistSeeded = true;
            Save();
        }
    }

    public void Save()
    {
        _pluginInterface?.SavePluginConfig(this);
    }
}
