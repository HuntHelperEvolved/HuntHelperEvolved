using System;
using System.Linq;
using System.Reflection;

namespace HuntHelperEvolved;

/// <summary>
/// One line of a release's notes.
///
/// Credit is per line rather than per release because a release is usually
/// several people's work, and "who do I thank for this particular thing" is the
/// question the notes are there to answer.
/// </summary>
public sealed record ReleaseChange(string Area, string Text, string Credit, int Issue = 0);

/// <summary>One published version.</summary>
public sealed record Release(string Version, string Date, string Summary, ReleaseChange[] Changes);

/// <summary>
/// What changed in each version, shown in game on the What's new tab.
///
/// Kept in code rather than shipped as a data file, for the same reason the
/// spawn point tables are: a release stays two files, a dll and a manifest,
/// with nothing alongside them to go missing or fall out of step.
///
/// Newest first. Add to the top when cutting a release, and make sure the
/// version matches the one in the csproj — MissingCurrentVersion below is what
/// catches it when they drift apart.
/// </summary>
public static class ReleaseNotes
{
    public const string Kihtli = "kihtli";
    public const string MusicManBowls = "MusicManBowls";

    public static readonly Release[] All =
    {
        new("0.3.0", "2026-09-05", "Marks on the map tell the truth about where they are.",
        [
            new("Map", "Marks are drawn where they actually stand, never snapped to the nearest spawn point. A mark near a point was only near it — up to a couple of map coordinates away — so walking to the dot was walking to the wrong place.", Kihtli, 14),
            new("Map", "Spawn points and marks have their own switches and their own B / A / S filters. Showing only A and S points while still being told about a B rank that has turned up now works.", Kihtli),
            new("Map", "Clicking a spawn point drops the flag on it, for sending people somewhere before anything has spawned there. SS minion spots and the spot the SS mark appears on work the same way.", Kihtli, 10),
            new("Map", "An SS minion that is up is drawn over its spot and larger than it, so a spot with something alive on it no longer looks identical to one still waiting.", Kihtli),
            new("Map", "Marks in different instances, and on different worlds, are told apart properly. Killing one in instance 1 was blanking the live one in instance 2.", Kihtli, 8),
            new("Train", "A mark is ticked off when the battle log says it died, whoever killed it. Marking dead ran off the tally, which only reports kills you were credited with, so one the group brought down while you were running in stayed lit as though it were still up.", Kihtli),
            new("Detection", "An SS event only starts on the game's own announcement. Anyone typing \"extraordinarily powerful mark\" in party chat was starting one that had not happened.", Kihtli),
            new("Tally", "Marks Slain filters by B / A / S rank as well as by name. The list is ordered by kills, so picking a rank puts your most-killed mark of it at the top.", Kihtli, 7),
            new("Commands", "/hh, /hht, /hhn, /hhna and /hhc answer here when Hunt Helper is not installed, so the muscle memory carries over. They are left alone if it is.", Kihtli, 4),
            new("Commands", "/htr, /htrt and /htrc close their windows again instead of only opening them.", Kihtli),
        ]),

        new("0.2.1", "2026-09-03", "The icon reaches installed copies, not just the installer list.",
        [
            new("Plugin", "The icon showed in the plugin list and then vanished once installed. The list is drawn from the repository manifest; an installed plugin is drawn from the one bundled in its own zip, and that copy had no icon in it.", Kihtli),
        ]),

        new("0.2.0", "2026-09-03", "Mark names on the map, Hunt Helper's notifications, and counters that count.",
        [
            new("Map", "Every mark that is up carries its name and remaining health beside its dot, counting down as it is pulled. Colour, outline and text size are settable.", Kihtli, 5),
            new("Map", "The detection ring and projected path no longer go missing after a teleport with the map left open.", Kihtli, 2),
            new("Detection", "A mark being spotted can announce itself in chat, as fly text, and out loud, with the message templates, placeholders and colours Hunt Helper uses. Each channel has its own B / A / S switches.", Kihtli, 6),
            new("Counters", "The non-kill trigger counters work again. Forgiven Pedantry, Squonk and Salt and Light were all being matched against a kill line when their real trigger is gathering, discarding or an ability firing, and Gandarewa had no counter at all.", MusicManBowls, 3),
            new("Counters", "Narrow-rift's Wee Ea headcount in Ultima Thule, and Nunyunuwi's no-FATE-failed clock in Southern Thanalan, neither of which fits the chat-line model.", MusicManBowls, 3),
        ]),

        new("0.1.0", "2026-09-03", "First release under the Hunt Helper Evolved name.",
        [
            new("Plugin", "Hunt Train Relay and Hunt Tally merged into one plugin and carried on, plus a substantial amount of new map work. Settings and tallies carry over from both.", MusicManBowls),
        ]),
    };

    /// <summary>
    /// The running version, as three parts — the fourth is always zero here and
    /// only ever gets in the way of matching what the notes are keyed on.
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null
                ? "unknown"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>The notes for the running version, if they have been written.</summary>
    public static Release? Current =>
        All.FirstOrDefault(r => r.Version == CurrentVersion);

    /// <summary>
    /// True when the running build has no notes. Says out loud on the tab that
    /// someone bumped the version and forgot this file, rather than quietly
    /// showing the previous release as though it were current.
    /// </summary>
    public static bool MissingCurrentVersion => Current is null;

    /// <summary>
    /// Whether a version string is one this build considers newer than what was
    /// last seen. String comparison would call 0.10.0 older than 0.9.0, so the
    /// parts are compared as numbers.
    /// </summary>
    public static bool IsNewerThan(string version, string previous)
    {
        if (string.IsNullOrWhiteSpace(previous)) return true;
        return Compare(version, previous) > 0;
    }

    private static int Compare(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);

        for (var i = 0; i < 3; i++)
        {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }

        return 0;
    }

    private static int[] Parts(string version)
    {
        var parts = new int[3];
        var split = version.Split('.');

        for (var i = 0; i < 3 && i < split.Length; i++)
            _ = int.TryParse(split[i], out parts[i]);

        return parts;
    }
}
