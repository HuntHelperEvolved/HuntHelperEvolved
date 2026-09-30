using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace HuntHelperEvolved;

internal static class HuntUi
{
    public static string FieldLabel(string label, float maxWidth = float.MaxValue)
    {
        ImGui.TextWrapped(label.Split("##", 2, StringSplitOptions.None)[0]);
        ImGui.SetNextItemWidth(Math.Max(1, Math.Min(maxWidth, ImGui.GetContentRegionAvail().X)));
        return "##" + label;
    }

    public static void SameLineIfFits(float width)
    {
        ImGui.SameLine();
        if (ImGui.GetContentRegionAvail().X < width) ImGui.NewLine();
    }

    public static bool WrappedCheckbox(string label, ref bool value)
    {
        var visibleLabel = label.Split("##", 2, StringSplitOptions.None)[0];
        var style = ImGui.GetStyle();
        var box = ImGui.GetFrameHeight();
        var gap = style.ItemInnerSpacing.X;
        var textWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - box - gap);
        var textSize = ImGui.CalcTextSize(visibleLabel, false, textWidth);
        var size = new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, box + gap + textSize.X),
            Math.Max(box, textSize.Y + style.FramePadding.Y * 2));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        var changed = ImGui.Button("##checkbox-" + label, size);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        if (changed) value = !value;
        if (!ImGui.IsItemVisible()) return changed;
        var min = ImGui.GetItemRectMin();
        var draw = ImGui.GetWindowDrawList();
        var background = ImGui.IsItemActive() ? ImGuiCol.FrameBgActive
            : ImGui.IsItemHovered() ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg;
        draw.AddRectFilled(min, min + new Vector2(box), ImGui.GetColorU32(background), style.FrameRounding);
        if (style.FrameBorderSize > 0)
            draw.AddRect(min, min + new Vector2(box), ImGui.GetColorU32(ImGuiCol.Border), style.FrameRounding);
        if (value)
        {
            var color = ImGui.GetColorU32(ImGuiCol.CheckMark);
            var a = min + new Vector2(box * .23f, box * .50f);
            var b = min + new Vector2(box * .43f, box * .70f);
            var c = min + new Vector2(box * .78f, box * .30f);
            draw.AddLine(a, b, color, Math.Max(1, box / 10));
            draw.AddLine(b, c, color, Math.Max(1, box / 10));
        }
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(box + gap, style.FramePadding.Y),
            ImGui.GetColorU32(ImGuiCol.Text), visibleLabel, textWidth);
        return changed;
    }

    public static float ButtonWidth(string label, FontAwesomeIcon? icon = null)
    {
        var width = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
        if (icon is { } glyph)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            width += ImGui.CalcTextSize(glyph.ToIconString()).X;
            ImGui.PopFont();
            if (label.Length > 0) width += 6 * ImGuiHelpers.GlobalScale;
        }
        return Math.Max(ImGui.GetFrameHeight(), width);
    }

    public static bool Button(string id, string label, FontAwesomeIcon? icon = null,
        bool primary = false, bool selected = false, bool quiet = false, Vector2? size = null, string? tooltip = null)
    {
        var dimensions = size ?? new Vector2(ButtonWidth(label, icon), ImGui.GetFrameHeight());
        ImGui.PushStyleColor(ImGuiCol.Button, primary || selected ? HuntTheme.Selected
            : quiet ? Vector4.Zero : HuntTheme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, HuntTheme.Selected);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, HuntTheme.Selected);
        ImGui.PushStyleColor(ImGuiCol.Border, primary ? HuntTheme.Accent : HuntTheme.Line);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, primary || !quiet ? 1 : 0);
        var clicked = ImGui.Button("##" + id, dimensions);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        if (!ImGui.IsItemVisible()) return clicked;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var color = ImGui.GetColorU32(primary || selected ? HuntTheme.Accent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        var labelWidth = ImGui.CalcTextSize(label).X;
        var iconWidth = 0f;
        if (icon is { } glyph)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            iconWidth = ImGui.CalcTextSize(glyph.ToIconString()).X;
            ImGui.PopFont();
        }
        var gap = icon is not null && label.Length > 0 ? 6 * ImGuiHelpers.GlobalScale : 0;
        var x = min.X + Math.Max(0, (max.X - min.X - labelWidth - iconWidth - gap) / 2);
        var y = min.Y + (max.Y - min.Y - ImGui.GetFontSize()) / 2;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, max, true);
        if (icon is { } symbol)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(x, y), color, symbol.ToIconString());
            ImGui.PopFont();
            x += iconWidth + gap;
        }
        draw.AddText(new Vector2(x, y), color, label);
        draw.PopClipRect();
        if (tooltip is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, bool selected = false) =>
        Button(id, string.Empty, icon, selected: selected, quiet: true,
            size: new Vector2(ImGui.GetFrameHeight()), tooltip: tooltip);

    public static bool UnderlineTab(string label, bool selected)
    {
        var height = ImGui.GetFrameHeight() + 8 * ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : HuntTheme.Muted);
        var clicked = Button("tab-" + label, label, quiet: true,
            size: new Vector2(ButtonWidth(label) + 8 * ImGuiHelpers.GlobalScale, height));
        ImGui.PopStyleColor();
        if (selected && ImGui.IsItemVisible())
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y - 1), new Vector2(max.X, max.Y - 1),
                ImGui.GetColorU32(HuntTheme.Accent), 2 * ImGuiHelpers.GlobalScale);
        }
        return clicked;
    }

    public static void FillBand(float height, Vector4? color = null, bool bottomBorder = true)
    {
        var start = ImGui.GetCursorScreenPos();
        var end = start + new Vector2(ImGui.GetContentRegionAvail().X, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, end, ImGui.GetColorU32(color ?? HuntTheme.Panel));
        if (bottomBorder) draw.AddLine(new Vector2(start.X, end.Y), end, ImGui.GetColorU32(HuntTheme.Line));
    }
}
