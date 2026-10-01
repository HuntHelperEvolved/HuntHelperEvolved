using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace HuntHelperEvolved;

internal static class HuntTheme
{
    private static bool _light;
    public static bool IsLight => _light;
    public static Vector4 Accent => Rgb(_light ? 0x245bd8 : 0x81aaff);
    public static Vector4 Muted => Rgb(_light ? 0x58626b : 0xb0b8c0);
    public static Vector4 Success => Rgb(_light ? 0x206c3c : 0x87c9a1);
    public static Vector4 ReportedUpRow => Rgb(_light ? 0xc3e6cf : 0x244c37);
    public static Vector4 Warning => Rgb(_light ? 0x805313 : 0xebc46f);
    public static Vector4 Danger => Rgb(_light ? 0xa93b2f : 0xf29e93);
    public static Vector4 Spice => new(1f, 0.35f, 0.35f, 1f);
    public static Vector4 Telemetry => Rgb(_light ? 0x076c7e : 0x68d4dc);
    public static Vector4 Surface => ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
    public static Vector4 Panel => ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
    public static Vector4 Chrome => ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg];
    public static Vector4 Line => ImGui.GetStyle().Colors[(int)ImGuiCol.Border];
    public static Vector4 Selected => ImGui.GetStyle().Colors[(int)ImGuiCol.Header];

    public static IDisposable Push(Configuration config)
    {
        var scope = new StyleScope();
        // Dalamud applies saved per-window opacity after this shared default.
        scope.Var(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * config.WindowOpacity);
        var background = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        _light = config.Theme == InterfaceTheme.Daylight ||
            config.Theme == InterfaceTheme.Dalamud && background.X + background.Y + background.Z > 1.5f;
        if (config.Theme == InterfaceTheme.Dalamud) return scope;

        var surface = Rgb(_light ? 0xf4f6f8 : 0x181a1c);
        var panel = Rgb(_light ? 0xffffff : 0x222426);
        var chrome = Rgb(_light ? 0xe9edf0 : 0x2a2d30);
        var line = Rgb(_light ? 0xcbd2d8 : 0x41464a);
        var selected = Rgb(_light ? 0xe5edff : 0x293b58);
        var hover = Rgb(_light ? 0xd9e5ff : 0x354b6b);
        var ink = Rgb(_light ? 0x22282c : 0xedf0f2);

        scope.Color(ImGuiCol.Text, ink);
        scope.Color(ImGuiCol.TextDisabled, Muted);
        scope.Color(ImGuiCol.WindowBg, surface);
        scope.Color(ImGuiCol.ChildBg, Vector4.Zero);
        scope.Color(ImGuiCol.PopupBg, panel);
        scope.Color(ImGuiCol.Border, line);
        scope.Color(ImGuiCol.BorderShadow, Vector4.Zero);
        scope.Color(ImGuiCol.TitleBg, chrome);
        scope.Color(ImGuiCol.TitleBgActive, panel);
        scope.Color(ImGuiCol.TitleBgCollapsed, chrome);
        scope.Color(ImGuiCol.MenuBarBg, panel);
        scope.Color(ImGuiCol.FrameBg, panel);
        scope.Color(ImGuiCol.FrameBgHovered, selected);
        scope.Color(ImGuiCol.FrameBgActive, hover);
        scope.Color(ImGuiCol.Button, panel);
        scope.Color(ImGuiCol.ButtonHovered, selected);
        scope.Color(ImGuiCol.ButtonActive, hover);
        scope.Color(ImGuiCol.Header, selected);
        scope.Color(ImGuiCol.HeaderHovered, hover);
        scope.Color(ImGuiCol.HeaderActive, selected);
        scope.Color(ImGuiCol.Tab, chrome);
        scope.Color(ImGuiCol.TabHovered, hover);
        scope.Color(ImGuiCol.TabActive, selected);
        scope.Color(ImGuiCol.TabUnfocused, chrome);
        scope.Color(ImGuiCol.TabUnfocusedActive, selected);
        scope.Color(ImGuiCol.CheckMark, Accent);
        scope.Color(ImGuiCol.SliderGrab, Accent);
        scope.Color(ImGuiCol.SliderGrabActive, Telemetry);
        scope.Color(ImGuiCol.Separator, line);
        scope.Color(ImGuiCol.SeparatorHovered, Accent);
        scope.Color(ImGuiCol.SeparatorActive, Accent);
        scope.Color(ImGuiCol.ResizeGrip, line);
        scope.Color(ImGuiCol.ResizeGripHovered, Accent);
        scope.Color(ImGuiCol.ResizeGripActive, Telemetry);
        scope.Color(ImGuiCol.PlotLines, Telemetry);
        scope.Color(ImGuiCol.PlotHistogram, Telemetry);
        scope.Color(ImGuiCol.TableHeaderBg, chrome);
        scope.Color(ImGuiCol.TableBorderStrong, line);
        scope.Color(ImGuiCol.TableBorderLight, line with { W = .4f });
        scope.Color(ImGuiCol.TableRowBg, Vector4.Zero);
        scope.Color(ImGuiCol.TableRowBgAlt, ink with { W = .025f });
        scope.Color(ImGuiCol.ScrollbarBg, surface);
        scope.Color(ImGuiCol.ScrollbarGrab, line);
        scope.Color(ImGuiCol.ScrollbarGrabHovered, Muted);
        scope.Color(ImGuiCol.ScrollbarGrabActive, Accent);
        scope.Color(ImGuiCol.TextSelectedBg, selected);
        scope.Color(ImGuiCol.NavHighlight, Accent);

        var scale = ImGuiHelpers.GlobalScale;
        scope.Var(ImGuiStyleVar.WindowRounding, 4 * scale);
        scope.Var(ImGuiStyleVar.ChildRounding, 2 * scale);
        scope.Var(ImGuiStyleVar.FrameRounding, 3 * scale);
        scope.Var(ImGuiStyleVar.PopupRounding, 4 * scale);
        scope.Var(ImGuiStyleVar.TabRounding, 2 * scale);
        scope.Var(ImGuiStyleVar.WindowBorderSize, 1);
        scope.Var(ImGuiStyleVar.ChildBorderSize, 1);
        scope.Var(ImGuiStyleVar.FrameBorderSize, 1);
        scope.Var(ImGuiStyleVar.ScrollbarSize, 11 * scale);
        scope.Var(ImGuiStyleVar.WindowPadding, new Vector2(10, 9) * scale);
        scope.Var(ImGuiStyleVar.FramePadding, new Vector2(7, 3) * scale);
        scope.Var(ImGuiStyleVar.ItemSpacing, new Vector2(7, 5) * scale);
        scope.Var(ImGuiStyleVar.CellPadding, new Vector2(7, 4) * scale);
        return scope;
    }

    public static IDisposable PushCompact()
    {
        var scope = new StyleScope();
        var scale = ImGuiHelpers.GlobalScale;
        scope.Var(ImGuiStyleVar.WindowPadding, new Vector2(7, 6) * scale);
        scope.Var(ImGuiStyleVar.FramePadding, new Vector2(4, 2) * scale);
        scope.Var(ImGuiStyleVar.ItemSpacing, new Vector2(5, 3) * scale);
        scope.Var(ImGuiStyleVar.CellPadding, new Vector2(4, 2) * scale);
        return scope;
    }

    public static void DrawPreferences(Configuration config)
    {
        var selected = Enum.IsDefined(config.Theme) ? (int)config.Theme : 0;
        ImGui.SetNextItemWidth(Math.Min(220 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.Combo(HuntUi.FieldLabel("Theme"), ref selected, new[] { "Graphite", "Daylight", "Dalamud" }, 3))
        {
            config.Theme = (InterfaceTheme)selected;
            config.Save();
        }
    }

    public static void DrawOpacityPreferences(Configuration config)
    {
        var percent = config.WindowOpacity * 100f;
        ImGui.SetNextItemWidth(Math.Min(220 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.SliderFloat(HuntUi.FieldLabel("Window opacity"), ref percent, 20f, 100f, "%.0f%%"))
        {
            config.WindowOpacity = percent / 100f;
            config.Save();
        }
        if (ImGui.Button("Reset opacity"))
        {
            config.WindowOpacity = 1f;
            config.Save();
        }
        ImGui.TextWrapped("Default opacity for all HHE windows, including Tally. Each window's Dalamud title-bar menu can override it; use Reset beside Opacity in that menu to follow this setting again.");
    }

    private static Vector4 Rgb(int hex) => new((hex >> 16 & 255) / 255f,
        (hex >> 8 & 255) / 255f, (hex & 255) / 255f, 1);

    // Scopes restore both ImGui's stack and semantic colours, including on early return.
    private sealed class StyleScope : IDisposable
    {
        private readonly bool _previousLight = _light;
        private int _colors;
        private int _vars;
        public void Color(ImGuiCol slot, Vector4 value) { ImGui.PushStyleColor(slot, value); _colors++; }
        public void Var(ImGuiStyleVar slot, float value) { ImGui.PushStyleVar(slot, value); _vars++; }
        public void Var(ImGuiStyleVar slot, Vector2 value) { ImGui.PushStyleVar(slot, value); _vars++; }
        public void Dispose()
        {
            if (_vars > 0) ImGui.PopStyleVar(_vars);
            if (_colors > 0) ImGui.PopStyleColor(_colors);
            _vars = _colors = 0;
            _light = _previousLight;
        }
    }
}
