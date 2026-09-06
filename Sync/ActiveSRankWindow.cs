using Dalamud.Bindings.ImGui;
using System;
using System.Linq;
using System.Numerics;

namespace HuntHelperEvolved.Sync;

/// <summary>Positive, fresh reports across every world the server knows.</summary>
public sealed class ActiveSRankWindow(Configuration config, SyncCoordinator sync, WorldData worlds, LifestreamTravel travel)
{
    private string _search = "";
    public void Toggle() { config.ActiveSRankWindowOpen = !config.ActiveSRankWindowOpen; config.Save(); }
    public void Draw()
    {
        if (!config.ActiveSRankWindowOpen) return;
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(850, 420), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Active S Ranks", ref open))
        {
            ImGui.TextWrapped("All worlds supplied by your server. Community reports do not imply a scout can currently see the mark.");
            ImGui.TextWrapped("Faloop coverage: " + string.Join(", ", sync.Faloop.DataCenters));
            if (!sync.IsConnected) ImGui.TextWrapped("Disconnected: active reports cannot be confirmed. " + sync.Status);
            else
            {
                if (sync.Faloop.Enabled && !sync.Faloop.Connected)
                    ImGui.TextWrapped("Faloop polling is unavailable; community reports expire after five minutes without confirmation.");
                ImGui.SetNextItemWidth(300);
                ImGui.InputTextWithHint("##activeSearch", "Filter mark, world, DC or zone", ref _search, 100);
                var now = DateTime.UtcNow;
                var rows = sync.SRankStatuses.Values.Select(s => (State: s,
                    Label: ActiveSRankFilter.Status(s, sync.IsSeenUp(s.NameId, s.WorldId, s.Instance), now)))
                    .Where(r => r.Label is not null && SRankTimerData.ByNameId.ContainsKey(r.State.NameId))
                    .Select(r => (r.State, r.Label, Mark: SRankTimerData.ByNameId[r.State.NameId],
                        World: worlds.NameOf(r.State.WorldId), Dc: worlds.LocateWorld(r.State.WorldId) is { } location
                            ? worlds.DataCenters[location.DcIndex].Name : "Unknown"))
                    .Where(r => (r.Mark.Name + " " + r.World + " " + r.Dc + " " + r.Mark.Zone).Contains(_search, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.Dc).ThenBy(r => r.World).ThenBy(r => r.Mark.Name).ToList();
                ImGui.TextDisabled($"{rows.Count} active reports. Right-click headers to show/hide columns.");
                var canTravel = travel.Available;
                if (!string.IsNullOrEmpty(travel.Status)) ImGui.TextWrapped(travel.Status);
                if (travel.Busy && ImGui.SmallButton("Cancel travel")) travel.Cancel();
                if (ImGui.BeginTable("activeSranksTravel", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable
                    | ImGuiTableFlags.Hideable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    foreach (var label in new[] { "Mark", "World", "DC", "Zone", "Active for", "Teleport" })
                        ImGui.TableSetupColumn(label, label == "Teleport" && !canTravel ? ImGuiTableColumnFlags.Disabled : ImGuiTableColumnFlags.None);
                    ImGui.TableHeadersRow();
                    foreach (var row in rows)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.Text(row.Mark.Name + ExpansionData.InstanceGlyph(row.State.Instance));
                        ImGui.TableNextColumn(); ImGui.Text(row.World);
                        ImGui.TableNextColumn(); ImGui.Text(row.Dc);
                        ImGui.TableNextColumn(); ImGui.Text(row.Mark.Zone);
                        ImGui.TableNextColumn();
                        var age = row.State.SpawnedAt is { } at ? now - at : (TimeSpan?)null;
                        ImGui.Text(age is { } elapsed ? $"{(int)Math.Max(0,elapsed.TotalHours):00}:{Math.Max(0,elapsed.Minutes):00}:{Math.Max(0,elapsed.Seconds):00}" : "—");
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Time since the earliest known spawn report. " + row.Label);
                        if (ImGui.TableNextColumn() && canTravel)
                        {
                            ImGui.PushID($"travel_{row.State.NameId}_{row.State.WorldId}_{row.State.Instance}");
                            var x=row.State.SpawnX; var y=row.State.SpawnY;
                            var hasPosition=x is { } px && y is { } py && float.IsFinite(px) && float.IsFinite(py) && px >= 1 && px <= 100 && py >= 1 && py <= 100;
                            ImGui.BeginDisabled(!hasPosition || travel.Busy);
                            if (ImGui.SmallButton("Teleport") && hasPosition)
                                travel.Start(row.State.WorldId, row.Mark.TerritoryId, new Vector2(x!.Value,y!.Value));
                            ImGui.EndDisabled();
                            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                ImGui.SetTooltip(!hasPosition ? "Location not reported yet." : "Travel to this world and the nearest eligible aetheryte. Select the mark's instance on arrival.");
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndTable();
                }
            }
        }
        ImGui.End();
        if (!open) { config.ActiveSRankWindowOpen = false; config.Save(); }
    }
}
