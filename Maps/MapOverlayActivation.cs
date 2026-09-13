using System;

namespace HuntHelperEvolved;

// Visibility changes do not own the native addon's lifetime. KamiToolKit handles
// AreaMap setup/finalize; an ordinary framework tick must never detach its nodes.
internal sealed class MapOverlayActivation
{
    public bool Started { get; private set; }
    public bool Visible { get; private set; }

    public bool Update(bool eligible, Func<bool> start, Action<bool> setVisible)
    {
        if (!eligible)
        {
            if (Visible) { setVisible(false); Visible = false; }
            return false;
        }
        if (!Started)
        {
            if (!start()) return false;
            Started = true;
        }
        if (!Visible) { setVisible(true); Visible = true; }
        return true;
    }
}
