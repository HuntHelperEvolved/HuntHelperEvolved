using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelperEvolved.Sync;

public static class TimerTableUi
{
    public const ImGuiTableFlags Flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY
        | ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable | ImGuiTableFlags.SizingStretchProp
        | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate;
    public static Vector4 Up => HuntTheme.Success;
    public static Vector4 Window => HuntTheme.Warning;
    public static Vector4 Cooldown => HuntTheme.Muted;
    public static Vector4 Unknown => HuntTheme.Muted;

    public static float FooterHeight => ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y + 1;

    public static void Footer(string id, string summary, Configuration config, SyncCoordinator sync, Action openSettings)
    {
        ImGui.Separator();
        var width = ImGui.GetContentRegionAvail().X;
        var fullWidth = ConnectionUi.Width();
        var connectionWidth = width >= fullWidth ? fullWidth : Math.Min(ConnectionUi.Width(compact: true), width);
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var summaryWidth = Math.Max(0, width - connectionWidth - gap);
        if (summaryWidth > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(HuntTheme.Muted, TrainRowPresentation.FitText(summary, summaryWidth,
                static text => ImGui.CalcTextSize(text).X));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(summary);
            ImGui.SameLine();
        }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - connectionWidth));
        ConnectionUi.Draw(id, config, sync, openSettings, connectionWidth);
    }

    public static void Status(SRankPhase phase, double percent, DateTime? opens, DateTime? ready, DateTime now,
        bool offline = false, string upLabel = "UP", string? unknownLabel = null, string? evidence = null)
    {
        ImGui.AlignTextToFramePadding();
        if (offline) ImGui.TextColored(Cooldown, "OFFLINE / MAINTENANCE");
        else if (phase == SRankPhase.Window) Progress(percent);
        else
        {
            var (label, colour) = phase switch
            {
                SRankPhase.Up => (upLabel, Up),
                SRankPhase.Forced => ("READY", Up),
                SRankPhase.Cooldown => ("opens in " + Duration((opens ?? now) - now), Cooldown),
                SRankPhase.Uncertain => (opens is { } earliest && earliest > now
                    ? "sniped; not before " + Duration(earliest - now)
                    : ready is { } latest ? "sniped; ready by " + Local(latest) : "sniped; timing unknown", Window),
                _ => (unknownLabel ?? "no kill recorded", Unknown)
            };
            ImGui.TextColored(colour, label);
        }
        if (!ImGui.IsItemHovered()) return;
        var lines = new List<string>();
        if (offline) lines.Add("This world is offline or in maintenance. Timers are not actionable.");
        if (phase != SRankPhase.Up && opens is { } opening) lines.Add("Opens: " + Local(opening));
        if (phase != SRankPhase.Up && ready is { } end) lines.Add("Ready by: " + Local(end));
        if (!offline && phase == SRankPhase.Window)
            lines.Add($"{percent:F0}% of the respawn window elapsed; not spawn probability.");
        if (!string.IsNullOrEmpty(evidence)) lines.Add(evidence);
        if (lines.Count > 0) ImGui.SetTooltip(string.Join("\n", lines));
    }

    public static List<T> Sort<T>(List<T> rows, Func<T,int,IComparable?> key)
    {
        var specs=ImGui.TableGetSortSpecs();
        if (specs.SpecsCount == 0) return rows;
        var column=specs.Specs.ColumnIndex;
        var descending=specs.Specs.SortDirection == ImGuiSortDirection.Descending;
        specs.SpecsDirty=false;
        return TimerTableSort.Apply(rows, row=>key(row,column),descending);
    }
    public static void StrikeLastItem()
    {
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var y = (min.Y + max.Y) / 2;
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, y), new Vector2(max.X, y), ImGui.ColorConvertFloat4ToU32(Cooldown));
    }
    public static void Progress(double percent)
    {
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Window with { W = 0.22f });
        ImGui.ProgressBar((float)Math.Clamp(percent/100,0,1),new Vector2(-1,ImGui.GetTextLineHeight()),$"{percent:F0}%");
        ImGui.PopStyleColor();
    }
    public static string Local(DateTime utc)
    {
        var local=DateTime.SpecifyKind(utc,DateTimeKind.Utc).ToLocalTime();
        var today=DateTime.Now.Date;
        if(local.Date==today) return local.ToString("HH:mm");
        if(local.Date==today.AddDays(1)) return $"tmrw {local:HH:mm}";
        return local.ToString("ddd HH:mm");
    }
    public static string Duration(TimeSpan span)
    {
        span=span.Duration();
        if(span.TotalMinutes<1) return "<1m";
        if(span.TotalHours<1) return $"{(int)span.TotalMinutes}m";
        if(span.TotalDays<1) return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
        return $"{(int)span.TotalDays}d {span.Hours}h";
    }
}
