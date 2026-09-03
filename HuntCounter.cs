using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HuntHelperEvolved;

/// <summary>
/// One S-rank whose spawn requires killing a number of specific lesser mobs.
/// Names and the match pattern are adapted from Hunt Helper's Constants.cs
/// (img02/HuntHelper, MIT licensed), English only.
/// </summary>
public sealed class CounterDefinition
{
    public string MarkName { get; init; } = string.Empty;
    public string Expansion { get; init; } = string.Empty;
    public string Zone { get; init; } = string.Empty;
    public uint TerritoryId { get; init; }
    public string[] MobNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Full battle-log regexes, index-aligned with <see cref="MobNames"/>, for
    /// marks whose trigger is not a kill and so never produces a "defeats the X"
    /// line: gathering yields (Forgiven Pedantry, Gandarewa), item discards
    /// (Salt and Light) and an ability-use line (Squonk). A non-null entry
    /// replaces the derived "defeats the &lt;mob&gt;" pattern for that name,
    /// which then serves only as a tally label; a null entry (or a short array)
    /// leaves that name on the ordinary kill pattern.
    ///
    /// The game only ever logs the local player's own gathers and discards, so
    /// these have no third-person form and "count only my kills" makes no
    /// difference to them.
    /// </summary>
    public string?[] TriggerPatterns { get; init; } = Array.Empty<string?>();
}

/// <summary>
/// Counts the events that trigger certain S-ranks by matching the game's own
/// battle log lines. Purely passive — it reads chat, never sends anything.
///
/// Most triggers are kills and match a "defeats the X" line. Four are not:
/// Forgiven Pedantry (gathering dwarven cotton), Gandarewa (gathering aurum
/// regis ore / seventh heaven), Salt and Light (discarding items) and Squonk
/// (an ability going off); those carry explicit
/// <see cref="CounterDefinition.TriggerPatterns"/> instead. Marks with no
/// loggable trigger at all (Narrow-rift's Wee Ea minions) are still out of
/// scope and handled elsewhere.
/// </summary>
public sealed class HuntCounter : IDisposable
{
    // Matches the game's English battle log for a defeated mob.
    private const string BattleRegexBase = "(?i)(defeat|defeats) the ";

    public static readonly List<CounterDefinition> Definitions = new()
    {
        new() { MarkName = "Ixtab", Expansion = "Shadowbringers", Zone = "The Rak'tika Greatwood", TerritoryId = 817,
                MobNames = new[] { "Cracked Ronkan Doll", "Cracked Ronkan Thorn", "Cracked Ronkan Vessel" } },
        new() { MarkName = "Forgiven Pedantry", Expansion = "Shadowbringers", Zone = "Kholusia", TerritoryId = 814,
                MobNames = new[] { "Dwarven Cotton Boll" },
                // Spawned by gathering dwarven cotton, not by a kill. The yield
                // line is "You obtain a/N dwarven cotton boll(s)." on the
                // Gathering channel. Pattern from Hunt Helper's Constants.cs.
                TriggerPatterns = new string?[] { @"(?i)You obtain.*dwarven cotton (boll|bolls)" } },
        new() { MarkName = "Sphatika", Expansion = "Endwalker", Zone = "Thavnair", TerritoryId = 957,
                MobNames = new[] { "Asvattha", "Pisaca", "Vajralangula" } },
        new() { MarkName = "Ruminator", Expansion = "Endwalker", Zone = "Mare Lamentorum", TerritoryId = 959,
                MobNames = new[] { "Thinker", "Wanderer", "Weeper" } },
        new() { MarkName = "Okina", Expansion = "Stormblood", Zone = "The Ruby Sea", TerritoryId = 613,
                MobNames = new[] { "Naked Yumemi", "Yumemi" } },
        new() { MarkName = "Udumbara", Expansion = "Stormblood", Zone = "The Fringes", TerritoryId = 612,
                MobNames = new[] { "Leshy", "Diakka" } },
        new() { MarkName = "Salt and Light", Expansion = "Stormblood", Zone = "The Lochs", TerritoryId = 621,
                MobNames = new[] { "Throw" },
                // Spawned by discarding items in the zone, not by a kill. The
                // line is "You throw away a/N <item>." on the SystemMessage
                // channel. Pattern from Hunt Helper's Constants.cs.
                TriggerPatterns = new string?[] { @"(?i)^You throw away " } },
        new() { MarkName = "Gandarewa", Expansion = "Heavensward", Zone = "The Churning Mists", TerritoryId = 400,
                MobNames = new[] { "Aurum Regis Ore", "Seventh Heaven" },
                // Spawned by gathering from the legendary nodes (mine aurum
                // regis ore, harvest seventh heaven), not by a kill. Yield
                // lines are "You obtain ... aurum regis ore" / "... seventh
                // heaven" on the Gathering channel. Patterns from Hunt Helper's
                // Constants.cs; one per item so each 50-count is its own row.
                TriggerPatterns = new string?[]
                {
                    @"(?i)You obtain.*aurum regis ore",
                    @"(?i)You obtain.*seventh heaven",
                } },
        new() { MarkName = "Leucrotta", Expansion = "Heavensward", Zone = "Azys Lla", TerritoryId = 402,
                MobNames = new[] { "Allagan Chimera", "Lesser Hydra", "Meracydian Vouivre" } },
        new() { MarkName = "Squonk", Expansion = "Heavensward", Zone = "The Sea of Clouds", TerritoryId = 401,
                MobNames = new[] { "Chirp" },
                // Not a kill either: the count tracks the Chirp ability going
                // off, logged as "Squonk uses Chirp" on the Action channel.
                // (Informational - it does not actually gate the spawn.)
                TriggerPatterns = new string?[] { @"(?i)Squonk uses Chirp" } },
        new() { MarkName = "Minhocao", Expansion = "ARR", Zone = "Northern Thanalan", TerritoryId = 147,
                MobNames = new[] { "Earth Sprite" } },
    };

    // "You defeat the X" — only your own kills.
    private const string PersonalRegexBase = "(?i)^you defeat the ";

    private readonly IChatGui _chatGui;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly Configuration _config;

    private readonly List<(Regex Personal, Regex Nearby, string MobName, string MarkName, uint TerritoryId)> _patterns = new();

    public HuntCounter(IChatGui chatGui, IClientState clientState, IObjectTable objectTable, Configuration config)
    {
        _chatGui = chatGui;
        _clientState = clientState;
        _objectTable = objectTable;
        _config = config;

        foreach (var def in Definitions)
        {
            for (var i = 0; i < def.MobNames.Length; i++)
            {
                var mob = def.MobNames[i];
                var triggerPattern = i < def.TriggerPatterns.Length ? def.TriggerPatterns[i] : null;

                if (triggerPattern is not null)
                {
                    // A gather / discard / ability line, not a "defeats the X"
                    // kill. There is no third-person form, so the same regex
                    // serves both the personal and the nearby slot.
                    var trigger = new Regex(triggerPattern, RegexOptions.Compiled);
                    _patterns.Add((trigger, trigger, mob, def.MarkName, def.TerritoryId));
                    continue;
                }

                _patterns.Add((
                    new Regex(PersonalRegexBase + Regex.Escape(mob), RegexOptions.Compiled),
                    new Regex(BattleRegexBase + Regex.Escape(mob), RegexOptions.Compiled),
                    mob,
                    def.MarkName,
                    def.TerritoryId));
            }
        }

        // Longer names first so "Naked Yumemi" can't be eaten by "Yumemi".
        _patterns.Sort((a, b) => b.MobName.Length.CompareTo(a.MobName.Length));

        _chatGui.ChatMessage += OnChatMessage;
    }

    /// <summary>Row id of the world the player is on, or 0 if unknown.</summary>
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

    /// <summary>Display name of the player's world, for labelling counts.</summary>
    public string CurrentWorldName()
    {
        try
        {
            var name = _objectTable.LocalPlayer?.CurrentWorld.Value.Name.ExtractText();
            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string TallyKey(uint worldId, uint instance, string mobName) =>
        $"{worldId}:{instance}:{mobName}";

    private static string MarkKey(uint worldId, uint instance, string markName) =>
        $"{worldId}:{instance}:{markName}";

    /// <summary>Current tally for a mob on a given world.</summary>
    public int GetTally(uint worldId, uint instance, string mobName) =>
        _config.CounterTallies.TryGetValue(TallyKey(worldId, instance, mobName), out var n) ? n : 0;

    /// <summary>When this counter last had a kill on that world, if ever.</summary>
    public DateTime? GetLastKill(uint worldId, uint instance, string markName) =>
        _config.CounterLastKill.TryGetValue(MarkKey(worldId, instance, markName), out var t) ? t : null;

    /// <summary>Auto-reset settings for a mark, created on first access.</summary>
    public CounterSettings SettingsFor(string markName)
    {
        if (!_config.CounterConfig.TryGetValue(markName, out var settings))
        {
            settings = new CounterSettings();
            _config.CounterConfig[markName] = settings;
        }
        return settings;
    }

    /// <summary>
    /// Clears any counter whose last contribution is older than its configured
    /// window. Measured from the last kill, so an active grind is never reset
    /// out from under the player.
    /// </summary>
    public void ApplyAutoResets()
    {
        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var def in Definitions)
        {
            var settings = SettingsFor(def.MarkName);
            if (!settings.AutoResetEnabled) continue;

            var window = TimeSpan.FromHours(Math.Clamp(settings.AutoResetHours, 1, 9));

            // Every world tracked for this mark, since a count on a world the
            // player has left should still age out.
            var stale = _config.CounterLastKill
                .Where(kv => kv.Key.EndsWith($":{def.MarkName}", StringComparison.Ordinal)
                             && now - kv.Value >= window)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var markKey in stale)
            {
                var prefix = markKey[..(markKey.LastIndexOf(':') + 1)];
                foreach (var mob in def.MobNames)
                {
                    if (_config.CounterTallies.Remove(prefix + mob)) changed = true;
                }

                _config.CounterLastKill.Remove(markKey);
                changed = true;
            }
        }

        if (changed) _config.Save();
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // Trigger lines land in these channels: SystemError / SystemMessage for
        // your own and a chocobo's kills, SystemMessage again for discards,
        // Gathering for gather yields, Action for "Squonk uses Chirp". Anything
        // else can't be a trigger, so skip the regex work entirely.
        var kind = message.LogKind;
        if (kind is not XivChatType.SystemError
            and not XivChatType.SystemMessage
            and not XivChatType.Gathering
            and not XivChatType.Action) return;

        var text = message.OriginalMessage.ToString();
        var territory = _clientState.TerritoryType;
        var worldId = CurrentWorldId();
        var instance = MarkDetector.GetCurrentInstance();

        foreach (var (personal, nearby, mobName, markName, patternTerritory) in _patterns)
        {
            // Every trigger mob for a given mark only counts toward its spawn
            // in that mark's own zone, and several of them - Earth Sprites,
            // discarded items - are common enough elsewhere to tick the wrong
            // counter constantly without this gate.
            if (territory != patternTerritory) continue;

            var pattern = _config.CountOnlyMyKills ? personal : nearby;
            if (!pattern.IsMatch(text)) continue;

            var key = TallyKey(worldId, instance, mobName);
            _config.CounterTallies[key] = (_config.CounterTallies.TryGetValue(key, out var n) ? n : 0) + 1;
            _config.CounterLastKill[MarkKey(worldId, instance, markName)] = DateTime.UtcNow;
            _config.Save();
            break; // one line is one kill
        }
    }

    /// <summary>Clears every count on every world.</summary>
    public void Reset()
    {
        _config.CounterTallies.Clear();
        _config.CounterLastKill.Clear();
        _config.Save();
    }

    /// <summary>Clears one mark's counts on the given world.</summary>
    public void ResetFor(CounterDefinition def, uint worldId, uint instance)
    {
        foreach (var mob in def.MobNames)
            _config.CounterTallies.Remove(TallyKey(worldId, instance, mob));

        _config.CounterLastKill.Remove(MarkKey(worldId, instance, def.MarkName));
        _config.Save();
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
    }
}
