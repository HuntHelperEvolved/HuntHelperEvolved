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

    private readonly Configuration _config;
    private readonly SyncCoordinator _sync;
    private readonly WorldData _worldData;
    private readonly MarkDetector _detector;

    private int _dcIndex;
    private int _worldIndex;
    private uint _followedWorld;
    private int _killedMinutesAgo;
    private int _maintenanceMinutesAgo;

    public SRankWindow(Configuration config, SyncCoordinator sync, WorldData worldData, MarkDetector detector)
    {
        _config = config;
        _sync = sync;
        _worldData = worldData;
        _detector = detector;
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

        var worldId = DrawWorldPicker();
        ImGui.SameLine();
        DrawExpansionFilter();

        ImGui.Spacing();
        DrawMaintenanceRow(worldId);
        ImGui.Spacing();

        var now = DateTime.UtcNow;
        var rows = BuildRows(worldId, now);

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY
                                      | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("sranks", 8, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Mark", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Opens", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Forced", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Killed", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Record", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
            DrawRow(row, worldId, now);

        ImGui.EndTable();
    }

    // -----------------------------------------------------------------------
    // Header controls
    // -----------------------------------------------------------------------

    private uint DrawWorldPicker()
    {
        var dcs = _worldData.DataCenters;
        if (dcs.Count == 0)
        {
            ImGui.TextDisabled(_detector.CurrentWorldName());
            return _detector.CurrentWorldId();
        }

        // Follow the player's world until a world is picked by hand.
        var live = _detector.CurrentWorldId();
        if (live != 0 && live != _followedWorld)
        {
            _followedWorld = live;
            if (_worldData.LocateWorld(live) is { } located)
            {
                _dcIndex = located.DcIndex;
                _worldIndex = located.WorldIndex;
            }
        }

        var dcNames = dcs.Select(d => d.Name).ToArray();
        _dcIndex = Math.Clamp(_dcIndex, 0, dcs.Count - 1);
        ImGui.SetNextItemWidth(130);
        if (ImGui.Combo("##srankDc", ref _dcIndex, dcNames, dcNames.Length))
            _worldIndex = 0;

        var worlds = _worldData.WorldsIn(dcs[_dcIndex].Id);
        if (worlds.Count == 0) return live;

        var worldNames = worlds.Select(w => w.Name).ToArray();
        _worldIndex = Math.Clamp(_worldIndex, 0, worlds.Count - 1);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        ImGui.Combo("##srankWorld", ref _worldIndex, worldNames, worldNames.Length);
        return worlds[_worldIndex].RowId;
    }

    private void DrawExpansionFilter()
    {
        var names = new[] { "All expansions" }.Concat(SRankTimerData.Expansions).ToArray();
        var index = Math.Clamp(_config.SRankWindowExpansion + 1, 0, names.Length - 1);
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##srankExpansion", ref index, names, names.Length))
        {
            _config.SRankWindowExpansion = index - 1;
            _config.Save();
        }
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
        var filter = _config.SRankWindowExpansion;

        foreach (var timer in SRankTimerData.All)
        {
            if (filter >= 0 && timer.ExpansionOrder != filter) continue;

            // One row per instance the server knows about, else instance 0.
            var instances = _sync.SRankStatuses.Keys
                .Where(k => k.NameId == timer.NameId && k.WorldId == worldId)
                .Select(k => k.Instance)
                .OrderBy(i => i)
                .ToList();
            if (instances.Count == 0) instances.Add(0);

            foreach (var instance in instances)
            {
                var status = _sync.StatusFor(timer.NameId, worldId, instance);
                var seenUp = _sync.IsSeenUp(timer.NameId, worldId, instance);
                rows.Add(new Row(timer, instance, status, SRankTimerData.Compute(timer, status, now, seenUp), seenUp));
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

    private void DrawRow(Row row, uint worldId, DateTime now)
    {
        var timer = row.Timer;
        var window = row.Window;
        ImGui.PushID($"{timer.NameId}_{row.Instance}");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.Text($"{timer.Name}{ExpansionData.InstanceGlyph(row.Instance)}");

        ImGui.TableNextColumn();
        ImGui.TextDisabled(timer.Zone);

        ImGui.TableNextColumn();
        DrawStatusCell(row, worldId, now);

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

    private void DrawStatusCell(Row row, uint worldId, DateTime now)
    {
        var w = row.Window;
        switch (w.Phase)
        {
            case SRankPhase.Up:
            {
                var hp = row.SeenUp
                    ? LiveHp(row, worldId)
                    : row.Status?.LastSeenHp;
                var text = hp is { } h ? $"UP — {h:F0}%" : "UP";
                ImGui.TextColored(UpColour, text);
                if (ImGui.IsItemHovered() && row.Status?.SpawnedAt is { } spawned)
                    ImGui.SetTooltip($"Seen since {Local(spawned)}");
                break;
            }

            case SRankPhase.Forced:
                ImGui.TextColored(ForcedColour, "FORCED");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Past the end of its window. It must be up, or the kill was never recorded.");
                break;

            case SRankPhase.Window:
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, WindowColour * new Vector4(1f, 1f, 1f, 0.8f));
                ImGui.ProgressBar((float)(w.Percent / 100.0), new Vector2(-1, ImGui.GetTextLineHeight()), $"{w.Percent:F0}%");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How far through its window it is — the chance it has spawned by now, if spawns are even across the window.");
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
