using System;
using Dalamud.Interface.Windowing;

namespace HuntHelperEvolved;

/// <summary>Hosts shared content in a native Dalamud window while retaining its existing open-state owner.</summary>
internal sealed class HuntWindow(string name, Func<bool> getOpen, Action<bool> setOpen, Action draw) : Window(name)
{
    private bool _openedAtFrameStart;
    public Func<bool>? Available { get; init; }
    public Func<bool>? ConsumeFocus { get; init; }
    public Action? BeforeDraw { get; init; }
    public Action? AfterDraw { get; init; }

    public override void PreOpenCheck() => IsOpen = _openedAtFrameStart = getOpen();
    public override bool DrawConditions() => Available?.Invoke() ?? true;
    public override void PreDraw()
    {
        if (ConsumeFocus?.Invoke() == true) BringToFront();
        BeforeDraw?.Invoke();
    }
    public override void Draw() => draw();
    public override void PostDraw()
    {
        AfterDraw?.Invoke();
        // Native close/Escape must reach the persisted owner before the next frame.
        // Do not overwrite a command or content button that changed that owner during Draw.
        if (IsOpen != _openedAtFrameStart) setOpen(IsOpen);
    }
}
