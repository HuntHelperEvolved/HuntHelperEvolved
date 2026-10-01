using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace HuntHelperEvolved;

public sealed partial class Plugin
{
    // Keep the existing ImGui IDs so saved window positions and sizes carry forward.
    private readonly WindowSystem _huntWindows = new(string.Empty);

    private void InitializeHuntWindows()
    {
        void Add(HuntWindow window, Vector2 size, Vector2 minimum)
        {
            window.Size = size;
            window.SizeCondition = ImGuiCond.FirstUseEver;
            window.SizeConstraints = new WindowSizeConstraints { MinimumSize = minimum, MaximumSize = new Vector2(float.MaxValue) };
            window.DisableWindowSounds = true;
            window.DisableFadeInFadeOut = true;
            _huntWindows.AddWindow(window);
        }

        bool LoggedIn() => _clientState.IsLoggedIn;
        Add(new HuntWindow("Hunt Train", () => _trainPopoutVisible, value => _trainPopoutVisible = value,
            DrawCompactTrainWindowContents) { Available = LoggedIn }, new(400, 340), new(300, 180));
        Add(new HuntWindow("Train presets", () => _presetEditorOpen, value => _presetEditorOpen = value,
            DrawPresetEditorContents)
        {
            Available = LoggedIn,
            ConsumeFocus = () => TakeFocus(ref _presetEditorFocusRequested)
        }, new(640, 700), new(380, 300));
        Add(new HuntWindow("Hunt Counter", () => _counterPopoutVisible, value => _counterPopoutVisible = value,
            DrawCounterPopoutContents) { Available = LoggedIn }, new(300, 400), new(220, 160));
        Add(_activeMarksWindow.CreateWindow(() => LoggedIn() || _releaseNotesChecked), new(430, 260), new(300, 150));
        Add(_srankWindow.CreateWindow(LoggedIn), new(880, 520), new(520, 240));
        Add(_arankWindow.CreateWindow(LoggedIn), new(1040, 520), new(520, 240));
        Add(new HuntWindow("Hunt Helper Evolved — what's new###HHEReleaseNotes",
            () => _releaseNotesVisible, value => _releaseNotesVisible = value,
            DrawReleaseNotesBody) { Available = LoggedIn }, new(560, 520), new(380, 240));
        Add(new HuntWindow("HHE Help", () => _workspaceHelpVisible, value => _workspaceHelpVisible = value,
            DrawHelpPage) { Available = LoggedIn }, new(720, 520), new(380, 240));
        Add(new HuntWindow("Hunt Helper Evolved", () => _configWindowVisible, value => _configWindowVisible = value,
            DrawWorkspace)
        {
            Available = LoggedIn,
            ConsumeFocus = () => TakeFocus(ref _focusWorkspace)
        }, new(900, 620), new(560, 320));
        Add(new HuntWindow("HHE Settings", () => _preferencesVisible, value => _preferencesVisible = value,
            DrawSettingsTab)
        {
            Available = LoggedIn,
            ConsumeFocus = () => TakeFocus(ref _focusPreferences),
            BeforeDraw = () => ImGui.PushStyleColor(ImGuiCol.WindowBg, HuntTheme.Panel),
            AfterDraw = () => ImGui.PopStyleColor()
        }, new(680, 580), new(480, 320));
    }

    internal static bool TakeFocus(ref bool requested)
    {
        var result = requested;
        requested = false;
        return result;
    }
}
