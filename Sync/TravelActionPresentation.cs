namespace HuntHelperEvolved.Sync;

internal readonly record struct TravelActionState(bool Enabled, string Tooltip);

internal static class TravelActionPresentation
{
    public static TravelActionState Evaluate(bool available, bool busy, bool offline, bool dead, bool hasPosition,
        string? destination, string world, uint instance)
    {
        if (offline) return new(false, "Travel unavailable while this world is offline.");
        if (dead) return new(false, "This mark is dead.");
        if (!hasPosition) return new(false, "Location not reported.");
        if (!available) return new(false, "Enable Lifestream for travel.");
        if (busy) return new(false, "Travel in progress.");
        if (destination is null) return new(false, "No eligible aetheryte. Check Settings > Travel.");
        return new(true, $"Travel to {destination} on {world}" + (instance == 0 ? "." : $", instance {instance}."));
    }
}
