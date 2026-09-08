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
    public static readonly Vector4 Up = new(0.3f,1f,0.4f,1f);
    public static readonly Vector4 Window = new(1f,0.85f,0.3f,1f);
    public static readonly Vector4 Cooldown = new(0.7f,0.7f,0.7f,1f);
    public static readonly Vector4 Unknown = new(0.5f,0.5f,0.5f,1f);

    public static List<T> Sort<T>(List<T> rows, Func<T,int,IComparable?> key)
    {
        var specs=ImGui.TableGetSortSpecs();
        if (specs.SpecsCount == 0) return rows;
        var column=specs.Specs.ColumnIndex;
        var descending=specs.Specs.SortDirection == ImGuiSortDirection.Descending;
        specs.SpecsDirty=false;
        return TimerTableSort.Apply(rows, row=>key(row,column),descending);
    }
    public static void Progress(double percent)
    {
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram,Window * new Vector4(1f,1f,1f,0.8f));
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
