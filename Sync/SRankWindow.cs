using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// The S-rank board: every timed S on a world, where it is in its cycle,
/// when its window opens and closes, who last saw it, and how many spawn
/// points are still possible. The in-game version of what the trackers
/// show, fed by the group's own server rather than the whole data centre.
/// </summary>
public sealed class SRankWindow
{
    private static readonly Vector4 UpColour = new(0.3f, 1f, 0.4f, 1f);
    private static readonly Vector4 ForcedColour = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 WindowColour = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 CooldownColour = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 UnknownColour = new(0.5f, 0.5f, 0.5f, 1f);

    private readonly LifestreamTravel _travel;
    private readonly Configuration _config;
    private readonly SyncCoordinator _sync;
    private readonly WorldData _worldData;
    private readonly MarkDetector _detector;

    private readonly Dictionary<(uint, DateTime?), ConditionWindow?> _conditionWindows = new();
    private int _killedMinutesAgo;
    private int _maintenanceMinutesAgo;

    public SRankWindow(Configuration config, SyncCoordinator sync, WorldData worldData, MarkDetector detector, LifestreamTravel travel)
    {
        _travel = travel;
        _config = config;
        _sync = sync;
        _worldData = worldData;
        _detector = detector;
        if (_config.SRankWindowExpansions is null)
        {
            _config.SRankWindowExpansions = _config.SRankWindowExpansion >= 0
                ? SRankTimerData.All.Where(t => t.ExpansionOrder == _config.SRankWindowExpansion).Select(t => t.Expansion).Distinct().ToList()
                : SRankTimerData.Expansions.ToList();
            _config.Save();
        }
    }

    public bool Visible
    {
        get => _config.SRankWindowOpen;
        set
        {
            if (_config.SRankWindowOpen == value) return;
            _config.SRankWindowOpen = value;
            _config.Save();
        }
    }

    public void Toggle() => Visible = !Visible;

    public void Draw()
    {
        if (!Visible) return;

        var open = true;
        ImGui.SetNextWindowSize(new Vector2(880, 520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 240), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("S Ranks", ref open))
        {
            try
            {
                DrawContents();
            }
            catch (Exception ex)
            {
                ImGui.TextColored(ForcedColour, $"The S-rank board hit an error: {ex.Message}");
            }
        }
        ImGui.End();

        if (!open) Visible = false;
    }

    private void DrawContents()
    {
        if (!_config.SyncEnabled)
        {
            ImGui.TextWrapped("Kill times live on your group's sync server. Turn sync on in the Sync tab and this board fills in as the group reports kills.");
            return;
        }

        if (!_sync.IsConnected)
            ImGui.TextColored(ForcedColour, _sync.Status);
        else
            ImGui.TextDisabled(_sync.Status);

        var faloop = _sync.Faloop;
        if (faloop.Enabled)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(FaloopFreshness(faloop));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(faloop.Status);
        }

        var worlds = DrawWorldPicker();
        ImGui.SameLine();
        DrawExpansionFilter();
        var available = _config.SRankWindowAvailableOnly;
        if (ImGui.Checkbox("Available to spawn only", ref available)) { _config.SRankWindowAvailableOnly = available; _config.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show marks whose known respawn window has opened, excluding unknown and uncertain timers. Active ranks remain visible at the top. Spawn conditions still need to be met.");
        ImGui.SameLine();
        var search = _config.SRankWindowSearch;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("##srankSearch", "Search mark or zone", ref search, 100)) { _config.SRankWindowSearch = search; _config.Save(); }
        if (ImGui.CollapsingHeader("Record maintenance"))
        {
            if (worlds.Count == 1) { ImGui.TextDisabled(_worldData.NameOf(worlds[0])); DrawMaintenanceRow(worlds[0]); }
            else ImGui.TextDisabled("Select exactly one world to record a maintenance reset.");
        }
        var now = DateTime.UtcNow;
        var rows = worlds.SelectMany(world => BuildRows(world, now).Select(row => (Row:row, World:world)))
            .OrderBy(r => r.Row.Window.Phase switch { SRankPhase.Up => 0, SRankPhase.Forced => 1, SRankPhase.Window => 2, SRankPhase.Uncertain => 3, SRankPhase.Cooldown => 4, _ => 5 })
            .ThenByDescending(r => r.Row.SeenUp ? r.Row.Status?.SpawnedAt ?? now : DateTime.MinValue)
            .ThenByDescending(r => r.Row.Window.Percent).ThenBy(r => r.Row.Window.OpensAtUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Row.Timer.Name).ThenBy(r => _worldData.NameOf(r.World)).ToList();
        ImGui.TextDisabled($"{rows.Count} marks across {worlds.Count} selected worlds. Server feed: {string.Join(", ", _sync.Faloop.DataCenters)}");

        ImGui.TextDisabled("Right-click headers for columns. Ctrl-click a mark name to travel for a hunt or spawn attempt.");
        if (!string.IsNullOrEmpty(_travel.Status)) ImGui.TextWrapped(_travel.Status);
        if (_travel.Busy && ImGui.SmallButton("Cancel travel")) _travel.Cancel();
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY
                                      | ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("sranksConditions", 10, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Mark", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Conditions", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Opens", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Ready by", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Killed", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Record", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
            DrawRow(row.Row, row.World, now);

        ImGui.EndTable();
    }

    // -----------------------------------------------------------------------
    // Header controls
    // -----------------------------------------------------------------------

    private List<uint> DrawWorldPicker()
    {
        var current = _config.SRankWindowCurrentWorld;
        if (ImGui.Checkbox("Current world", ref current)) { _config.SRankWindowCurrentWorld = current; _config.Save(); }
        if (current) { ImGui.SameLine(); ImGui.TextDisabled(_detector.CurrentWorldName()); return _detector.CurrentWorldId() == 0 ? new() : new() { _detector.CurrentWorldId() }; }
        ImGui.SameLine(); ImGui.SetNextItemWidth(190);
        if (ImGui.BeginCombo("##srankWorlds", $"Worlds ({_config.SRankWindowWorlds.Count})"))
        {
            foreach (var dc in _worldData.DataCenters)
            {
                if (!ImGui.TreeNode(dc.Name)) continue;
                var worlds = _worldData.WorldsIn(dc.Id);
                var all = worlds.All(w => _config.SRankWindowWorlds.Contains(w.RowId));
                if (ImGui.Checkbox("All##" + dc.Id, ref all))
                {
                    foreach (var world in worlds)
                    { _config.SRankWindowWorlds.Remove(world.RowId); if (all) _config.SRankWindowWorlds.Add(world.RowId); }
                    _config.Save();
                }
                foreach (var world in worlds)
                {
                    var selected = _config.SRankWindowWorlds.Contains(world.RowId);
                    if (ImGui.Checkbox(world.Name, ref selected))
                    { if (selected) _config.SRankWindowWorlds.Add(world.RowId); else _config.SRankWindowWorlds.Remove(world.RowId); _config.Save(); }
                }
                ImGui.TreePop();
            }
            ImGui.EndCombo();
        }
        return _config.SRankWindowWorlds.Distinct().Where(w => _worldData.LocateWorld(w) is not null).ToList();
    }

    private void DrawExpansionFilter()
    {
        var chosen = _config.SRankWindowExpansions!;
        ImGui.SetNextItemWidth(180);
        if (!ImGui.BeginCombo("##srankExpansions", $"Expansions ({chosen.Count})")) return;
        var all = SRankTimerData.Expansions.All(chosen.Contains);
        if (ImGui.Checkbox("All expansions", ref all))
        { chosen.Clear(); if (all) chosen.AddRange(SRankTimerData.Expansions); _config.Save(); }
        foreach (var name in SRankTimerData.Expansions)
        {
            var selected = chosen.Contains(name);
            if (ImGui.Checkbox(name, ref selected))
            { if (selected) chosen.Add(name); else chosen.Remove(name); _config.Save(); }
        }
        ImGui.EndCombo();
    }

    private void DrawMaintenanceRow(uint worldId)
    {
        ImGui.TextDisabled("After maintenance, every S is on the shorter clock from when the servers came back:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70);
        ImGui.InputInt("##maintAgo", ref _maintenanceMinutesAgo, 0, 0);
        _maintenanceMinutesAgo = Math.Clamp(_maintenanceMinutesAgo, 0, 60 * 24 * 7);
        ImGui.SameLine();
        ImGui.TextDisabled("min ago");
        ImGui.SameLine();

        var shift = ImGui.GetIO().KeyShift;
        var canSend = shift && _sync.IsConnected;
        if (!canSend) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        var pressed = ImGui.Button("Record maintenance");
        if (!canSend) ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(shift
                ? "Rewrites every S-rank clock on this world. Cannot be undone."
                : "Hold Shift to record — this rewrites every S-rank clock on this world.");
        if (pressed && canSend)
            _sync.ReportMaintenance(worldId, DateTime.UtcNow.AddMinutes(-_maintenanceMinutesAgo));
    }

    // -----------------------------------------------------------------------
    // Rows
    // -----------------------------------------------------------------------

    private sealed record Row(SRankTimer Timer, uint Instance, SyncSRankStatus? Status, SRankCycle Window, bool SeenUp);

    private List<Row> BuildRows(uint worldId, DateTime now)
    {
        var rows = new List<Row>();

        foreach (var timer in SRankTimerData.All)
        {
            if (!_config.SRankWindowExpansions!.Contains(timer.Expansion)) continue;
            if (!string.IsNullOrWhiteSpace(_config.SRankWindowSearch)
                && !(timer.Name + " " + timer.Zone).Contains(_config.SRankWindowSearch, StringComparison.OrdinalIgnoreCase)) continue;

            // One row per instance the server knows about, else instance 0.
            var instances = _sync.SRankStatuses.Keys
                .Where(k => k.NameId == timer.NameId && k.WorldId == worldId)
                .Select(k => k.Instance)
                .OrderBy(i => i)
                .ToList();
            if (worldId == _detector.CurrentWorldId() && timer.TerritoryId == _detector.CurrentTerritoryId
                && !instances.Contains(MarkDetector.GetCurrentInstance()))
                instances.Add(MarkDetector.GetCurrentInstance());
            if (instances.Count == 0) instances.Add(0);

            foreach (var instance in instances)
            {
                var status = _sync.StatusFor(timer.NameId, worldId, instance);
                var seenUp = _sync.IsSeenUp(timer.NameId, worldId, instance);
                seenUp |= status is not null && ActiveSRankFilter.Status(status,seenUp,now) is not null;
                var cycle = SRankTimerData.Compute(timer, status, now, seenUp);
                if (_config.SRankWindowAvailableOnly && !seenUp && !SRankBoardFilter.Available(cycle.Phase)) continue;
                rows.Add(new Row(timer, instance, status, cycle, seenUp));
            }
        }

        // What can be acted on first: up, then forced, then the window by
        // how far along it is, then cooldowns by how soon they open, then
        // the ones nobody knows anything about.
        return rows
            .OrderBy(r => r.Window.Phase switch
            {
                SRankPhase.Up => 0,
                SRankPhase.Forced => 1,
                SRankPhase.Window => 2,
                SRankPhase.Uncertain => 3,
                SRankPhase.Cooldown => 4,
                _ => 5,
            })
            .ThenByDescending(r => r.Window.Percent)
            .ThenBy(r => r.Window.OpensAtUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Timer.ExpansionOrder)
            .ThenBy(r => r.Timer.Name)
            .ToList();
    }

    private Vector2? TravelPosition(Row row, uint world)
    {
        var key=(row.Timer.NameId,row.Instance,world);
        if (_detector.OtherRanks.TryGetValue(key,out var local) && DateTime.UtcNow-local.LastSeenUtc < TimeSpan.FromSeconds(2)) return local.MapPosition;
        if (_sync.IsSeenUp(row.Timer.NameId,world,row.Instance) && _sync.RemoteSightings.TryGetValue(key,out var remote)) return remote.MapPosition;
        if (row.SeenUp && row.Status?.SpawnX is { } x && row.Status.SpawnY is { } y
            && float.IsFinite(x) && float.IsFinite(y) && x >= 1 && x <= 100 && y >= 1 && y <= 100) return new(x,y);
        return null;
    }

    private void DrawRow(Row row, uint worldId, DateTime now)
    {
        var timer = row.Timer;
        var window = row.Window;
        ImGui.PushID($"{worldId}_{timer.NameId}_{row.Instance}");
        ImGui.TableNextRow();
        if (row.SeenUp)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.65f,0.08f,0.08f,0.65f)));

        ImGui.TableNextColumn();
        var nameState=SRankBoardFilter.NameState(window.Phase, SpawnConditionData.HasTimedCondition(timer.Name), ConditionFor(row,now),now);
        var nameColour=nameState switch { SRankNameState.Ready => new Vector4(0.35f,0.95f,0.4f,1),
            SRankNameState.ConditionsUnmet => new Vector4(1f,0.3f,0.3f,1), _ => new Vector4(0.65f,0.65f,0.65f,1) };
        ImGui.TextColored(nameColour,$"{timer.Name}{ExpansionData.InstanceGlyph(row.Instance)}");
        if (ImGui.IsItemHovered())
        {
            var exact = TravelPosition(row, worldId);
            var position = exact ?? SpawnMapping.TravelEstimate(
                SpawnPointData.For(timer.TerritoryId),
                _sync.ZoneFor(timer.TerritoryId, worldId, row.Instance),
                row.Status?.KilledAt is not null && !row.Status.Uncertain);
            var destination = TeleportHelper.NearestTo(timer.TerritoryId, position);
            ImGui.SetTooltip(SpawnConditionData.Description(timer.Name) + "\nName: green = timer/timed conditions open; red = timed conditions unmet; grey = not ready or unknown. Required player actions still apply.\n" +
                (!_travel.Available ? "Lifestream is not available."
                    : destination is not { } target ? "No allowed aetheryte is available for this zone."
                    : $"Ctrl-click to travel to {target.Name} on {_worldData.NameOf(worldId)}. " +
                      (exact is null ? "Suggested for a spawn attempt; exact location unknown. " : "Nearest to the reported location. ") +
                      "Select the instance on arrival."));
            if (ImGui.GetIO().KeyCtrl && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && destination is not null && _travel.Available)
                _travel.Start(worldId, timer.TerritoryId, position);
        }

        ImGui.TableNextColumn();
        ImGui.TextDisabled(_worldData.NameOf(worldId));
        ImGui.TableNextColumn();
        ImGui.TextDisabled(timer.Zone);

        ImGui.TableNextColumn();
        DrawStatusCell(row, worldId, now);

        ImGui.TableNextColumn();
        DrawConditionCell(row, now);

        ImGui.TableNextColumn();
        ImGui.Text(window.OpensAtUtc is { } opens ? Local(opens) : "—");

        ImGui.TableNextColumn();
        ImGui.Text(window.ForcedAtUtc is { } forced ? Local(forced) : "—");

        ImGui.TableNextColumn();
        DrawKilledCell(row);

        ImGui.TableNextColumn();
        DrawPointsCell(row, worldId);

        ImGui.TableNextColumn();
        DrawRecordCell(row, worldId);

        ImGui.PopID();
    }

    private ConditionWindow? ConditionFor(Row row, DateTime now)
    {
        if (!SpawnConditionData.HasTimedCondition(row.Timer.Name)) return null;
        var gate=row.Window.OpensAtUtc;
        var key=(row.Timer.NameId,gate);
        if(!_conditionWindows.TryGetValue(key,out var window) || window is null || window.Value.End<=now)
        {
            window=SpawnConditionData.Next(row.Timer.Name,gate is { } opens && opens>now ? opens : now);
            _conditionWindows[key]=window;
        }
        return window;
    }
    private void DrawConditionCell(Row row, DateTime now)
    {
        var gate=row.Window.OpensAtUtc;
        var reliable=row.Status?.KilledAt is not null && !row.Status.Uncertain;
        if(row.Window.Phase==SRankPhase.Up) { ImGui.TextDisabled("Already reported up"); return; }
        if(!SpawnConditionData.HasTimedCondition(row.Timer.Name))
        {
            if(reliable && gate is { } opens && opens>now) ImGui.TextColored(ForcedColour,"Opens in "+Countdown(opens-now));
            else ImGui.TextDisabled("No timed restriction");
            return;
        }
        var window=ConditionFor(row,now);
        if(window is not { } w) { ImGui.TextDisabled("Forecast unavailable"); return; }
        var start=gate is { } g && g>w.Start ? g : w.Start;
        if(now<start) ImGui.TextColored(ForcedColour,"In "+Countdown(start-now));
        else ImGui.TextColored(reliable ? UpColour : WindowColour,(reliable ? "Open: " : "Condition: ")+Countdown(w.End-now));
        if(ImGui.IsItemHovered()) ImGui.SetTooltip(SpawnConditionData.Description(row.Timer.Name)
            + "\nCountdown uses real time; green means the respawn window and timed restrictions are open."
            + "\nRequired kills, gathering and player actions still apply; their completion is not verified."
            + (!reliable ? "\nKill time is unknown or uncertain, so spawn availability cannot be confirmed." : ""));
    }
    private static string Countdown(TimeSpan span) => $"{(int)Math.Max(0,span.TotalHours):00}:{Math.Max(0,span.Minutes):00}:{Math.Max(0,span.Seconds):00}";

    private void DrawStatusCell(Row row, uint worldId, DateTime now)
    {
        var w = row.Window;
        switch (w.Phase)
        {
            case SRankPhase.Up:
            {
                var hp = row.SeenUp
                    ? LiveHp(row, worldId)
                    : null;
                var text = hp is { } h ? $"UP — {h:F0}%" : row.SeenUp ? "UP" : "REPORTED UP";
                ImGui.TextColored(UpColour, text);
                if (ImGui.IsItemHovered() && row.Status?.SpawnedAt is { } spawned)
                    ImGui.SetTooltip($"Seen since {Local(spawned)}");
                break;
            }

            case SRankPhase.Forced:
                ImGui.TextColored(ForcedColour, "READY");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The respawn timer is ready. The mark still needs its spawn conditions to be met.");
                break;

            case SRankPhase.Window:
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, WindowColour * new Vector4(1f, 1f, 1f, 0.8f));
                ImGui.ProgressBar((float)(w.Percent / 100.0), new Vector2(-1, ImGui.GetTextLineHeight()), $"{w.Percent:F0}%");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Elapsed portion of the respawn window. Spawn conditions must still be met; this is not a confirmed spawn probability.");
                break;

            case SRankPhase.Cooldown:
            {
                var until = (w.OpensAtUtc ?? now) - now;
                ImGui.TextColored(CooldownColour, $"opens in {Duration(until)}");
                break;
            }

            case SRankPhase.Uncertain:
            {
                var opens = w.OpensAtUtc ?? now;
                ImGui.TextColored(WindowColour, opens > now ? $"sniped; not before {Duration(opens - now)}" : "sniped; may be open");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Faloop recorded it killed without a report, so the kill time is only the earliest it could have been. The window can start any time after it.");
                break;
            }

            default:
                ImGui.TextColored(UnknownColour, "no kill recorded");
                break;
        }
    }

    private float? LiveHp(Row row, uint worldId)
    {
        var key = (row.Timer.NameId, row.Instance, worldId);
        if (_detector.OtherRanks.TryGetValue(key, out var local)) return local.HealthPercent;
        if (_sync.RemoteSightings.TryGetValue(key, out var remote)) return remote.HealthPercent;
        return null;
    }

    private void DrawKilledCell(Row row)
    {
        var status = row.Status;
        if (status?.KilledAt is not { } killed)
        {
            ImGui.TextDisabled("—");
            return;
        }

        var ago = Duration(DateTime.UtcNow - killed);
        var what = status.Maintenance ? "maintenance" : status.KillSource ?? "unknown";

        // Faloop and a member disagree by more than a few minutes: say so
        // rather than let either quietly win.
        var disagreement = status.FaloopKilledAt is { } faloopAt
                           && status.KillSource is "observed" or "manual"
                           && (faloopAt - killed).Duration() > TimeSpan.FromMinutes(5);

        if (disagreement) ImGui.TextColored(ForcedColour, $"{ago} ago !");
        else ImGui.Text($"{ago} ago{(status.Uncertain ? " ~" : string.Empty)}");

        if (ImGui.IsItemHovered())
        {
            var who = string.IsNullOrEmpty(status.KillReporter) ? string.Empty : $" by {status.KillReporter}";
            var tip = $"{Local(killed)} — {what}{who}";
            if (status.Uncertain) tip += "\nEarliest possible; it died unreported some time after this.";
            if (disagreement && status.FaloopKilledAt is { } f)
                tip += $"\nFaloop has {Local(f)} instead. The member's report is being used.";
            ImGui.SetTooltip(tip);
        }
    }

    private void DrawPointsCell(Row row, uint worldId)
    {
        var points = SpawnPointData.For(row.Timer.TerritoryId);
        var capable = new List<int>();
        for (var i = 0; i < points.Length; i++)
            if (points[i].Ranks.HasFlag(SpawnRanks.S)) capable.Add(i);

        if (capable.Count == 0)
        {
            ImGui.TextDisabled("—");
            return;
        }

        var zone = _sync.ZoneFor(row.Timer.TerritoryId, worldId, row.Instance);
        if (zone is null)
        {
            ImGui.TextDisabled($"?/{capable.Count}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Nothing recorded for this zone yet.");
            return;
        }

        var left = capable.Count(i => !zone.IsRuledOut(i));
        var colour = left == 1 ? UpColour : left <= 3 ? WindowColour : Vector4.One;
        ImGui.TextColored(colour, $"{left}/{capable.Count}");
        if (ImGui.IsItemHovered())
        {
            var since = zone.SinceAt is { } s ? $"since {Local(s)}" : "since tracking began";
            ImGui.SetTooltip($"{left} of {capable.Count} S-capable points still possible, {since}.\n"
                             + $"{zone.Eliminated.Count} ruled out by A/B sightings"
                             + (zone.LastSDeathIndex is { } d ? $", plus point {d + 1} where it last died." : "."));
        }
    }

    private void DrawRecordCell(Row row, uint worldId)
    {
        var connected = _sync.IsConnected;
        var shift = ImGui.GetIO().KeyShift;

        if (!connected) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

        if (ImGui.SmallButton("Now") && connected)
            _sync.ReportManualKill(row.Timer, worldId, row.Instance, DateTime.UtcNow);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Record it killed just now.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(46);
        ImGui.InputInt("##ago", ref _killedMinutesAgo, 0, 0);
        _killedMinutesAgo = Math.Clamp(_killedMinutesAgo, 0, 60 * 24 * 7);
        ImGui.SameLine();
        if (ImGui.SmallButton("min ago") && connected)
            _sync.ReportManualKill(row.Timer, worldId, row.Instance, DateTime.UtcNow.AddMinutes(-_killedMinutesAgo));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Record it killed that many minutes ago.");

        if (row.Status?.KilledAt is not null)
        {
            ImGui.SameLine();
            var canClear = connected && shift;
            if (!canClear && connected) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            if (ImGui.SmallButton("Clear") && canClear)
                _sync.ClearKill(row.Timer.NameId, worldId, row.Instance);
            if (!canClear && connected) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Forget this kill for everyone. Hold Shift.");
        }

        if (!connected) ImGui.PopStyleVar();
    }

    // -----------------------------------------------------------------------
    // Formatting
    // -----------------------------------------------------------------------

    private static string FaloopFreshness(SyncFaloopStatus faloop)
    {
        if (!faloop.Connected) return "Faloop: not reachable";
        if (faloop.LastSyncAt is not { } at) return "Faloop: waiting for the first read";
        var age = DateTime.UtcNow - at;
        var dcs = faloop.DataCenters.Count > 0 ? $" ({string.Join(", ", faloop.DataCenters)})" : string.Empty;
        return age.TotalMinutes < 1 ? $"Faloop: read just now{dcs}" : $"Faloop: read {Duration(age)} ago{dcs}";
    }

    private static string Local(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.Now.Date;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(1)) return $"tmrw {local:HH:mm}";
        return local.ToString("ddd HH:mm");
    }

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = -span;
        if (span.TotalMinutes < 1) return "<1m";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
        return $"{(int)span.TotalDays}d {span.Hours}h";
    }
}
