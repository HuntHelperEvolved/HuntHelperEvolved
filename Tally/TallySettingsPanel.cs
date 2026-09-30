using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using HuntTheme = HuntHelperEvolved.HuntTheme;
using HuntUi = HuntHelperEvolved.HuntUi;

namespace HuntTally.Windows;

/// <summary>
/// The tally's settings, as a panel rather than a window of its own.
///
/// Up to the merge this was a second settings window, opened from Dalamud's
/// plugin list alongside the relay's. Both plugins are one now, so it is drawn
/// as a tab inside the relay's config window instead - same controls, in the
/// place a user would look for them. The tally's own display window stays
/// separate, since that one is a view rather than settings.
/// </summary>
public sealed class TallySettingsPanel
{
    private readonly Configuration config;
    private readonly AchievementSeeder seeder;
    private readonly CharacterContext characters;
    private readonly DamageWatch damage;
    private readonly RewardWatch reward;
    private readonly KillTracker tracker;
    private bool confirmReset;

    public TallySettingsPanel(
        Configuration config, AchievementSeeder seeder, CharacterContext characters,
        DamageWatch damage, RewardWatch reward, KillTracker tracker)
    {
        this.config = config;
        this.seeder = seeder;
        this.characters = characters;
        this.damage = damage;
        this.reward = reward;
        this.tracker = tracker;
    }

    public void Draw()
    {
        ImGui.PushTextWrapPos(0);
        DrawDetectionSection();

        ImGui.Separator();
        DrawRankSection();

        ImGui.Separator();
        DrawSeedingSection();

        ImGui.Separator();
        var limit = config.HistoryLimit;
        ImGui.TextUnformatted("Detail log entries kept");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt("##Detail log entries kept", ref limit, 500))
        {
            config.HistoryLimit = Math.Clamp(limit, 100, 100000);
            config.MarkChanged();
        }

        ImGui.Separator();
        DrawResetSection();
        ImGui.PopTextWrapPos();
    }

    private void DrawDetectionSection()
    {
        var damageInUse = DrawCreditStatus();

        var requireCombat = config.RequireCombat;
        var label = damageInUse
            ? "Only count marks I hit"
            : "Only count marks I was in combat for";

        if (HuntUi.WrappedCheckbox(label, ref requireCombat))
        {
            config.RequireCombat = requireCombat;
            config.MarkChanged();
        }

        // Strict credit exists only to tighten the combat proxy. Reading the
        // player's own actions is already stricter and more accurate than
        // anything it can add, so it does nothing while that is live.
        using (ImRaii.Disabled(damageInUse))
        {
            var strict = config.StrictCredit;
            if (HuntUi.WrappedCheckbox("Strict credit", ref strict))
            {
                config.StrictCredit = strict;
                config.MarkChanged();
            }
        }
        if (damageInUse && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Damage detection already uses your own actions. Strict credit only applies to the combat fallback.");

        DrawRewardConfirmation();

        var distance = config.MaxDistance;
        ImGui.TextUnformatted("Detection radius (yalms)");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##Detection radius (yalms)", ref distance, 20f, 200f, "%.0f"))
        {
            config.MaxDistance = distance;
            config.MarkChanged();
        }

        var chat = config.ChatOnKill;
        if (HuntUi.WrappedCheckbox("Print a chat message on each kill", ref chat))
        {
            config.ChatOnKill = chat;
            config.MarkChanged();
        }

        var allDeaths = config.PublishAllMarkDeaths;
        if (HuntUi.WrappedCheckbox("Send every mark death over IPC", ref allDeaths))
        {
            config.PublishAllMarkDeaths = allDeaths;
            config.MarkChanged();
        }

    }

    /// <summary>
    /// Says out loud which credit signal is in use. A hook that stops firing
    /// after a patch would otherwise show up only as kills quietly going
    /// uncounted, which is exactly the failure this plugin keeps having to fix.
    /// </summary>
    /// <summary>
    /// The reward confirmation toggle, with the two counters that make it
    /// diagnosable. If a rank ever stops emitting the confirmation, the drop
    /// count is what says so before the totals quietly go wrong.
    /// </summary>
    private void DrawRewardConfirmation()
    {
        var require = config.RequireRewardMessage;
        if (HuntUi.WrappedCheckbox("Only count A and S ranks the game says it rewarded", ref require))
        {
            config.RequireRewardMessage = require;
            config.MarkChanged();
        }

        if (!config.RequireRewardMessage)
        {
            HuntUi.SameLineIfFits(ImGui.CalcTextSize("(off)").X);
            ImGui.TextDisabled("(off)");
            return;
        }

        var dropped = tracker.DroppedUnconfirmed;
        var status = dropped == 0 ? $"({reward.Seen} confirmed)" : $"({reward.Seen} confirmed, {dropped} dropped)";
        HuntUi.SameLineIfFits(ImGui.CalcTextSize(status).X);
        if (dropped == 0)
            ImGui.TextDisabled(status);
        else
            ImGui.TextColored(HuntTheme.Warning, status);
    }

    private bool DrawCreditStatus()
    {
        if (!damage.IsActive)
        {
            ImGui.TextColored(HuntTheme.Warning, "Damage detection: unavailable");
            ImGui.TextDisabled(damage.Status);

            ImGui.Spacing();
            return false;
        }

        var use = config.UseDamageDetection;
        if (HuntUi.WrappedCheckbox("Use damage detection", ref use))
        {
            config.UseDamageDetection = use;
            config.MarkChanged();
        }

        var status = config.UseDamageDetection ? $"({damage.EventsSeen} of your actions seen)" : "(off - using combat fallback)";
        HuntUi.SameLineIfFits(ImGui.CalcTextSize(status).X);
        if (config.UseDamageDetection)
            ImGui.TextDisabled(status);
        else
            ImGui.TextColored(HuntTheme.Warning, status);

        ImGui.Spacing();
        return config.UseDamageDetection;
    }

    private void DrawRankSection()
    {
        ImGui.Text("Ranks to track");

        var b = config.TrackB;
        if (HuntUi.WrappedCheckbox("B ranks", ref b)) { config.TrackB = b; config.MarkChanged(); }
        var a = config.TrackA;
        if (HuntUi.WrappedCheckbox("A ranks", ref a)) { config.TrackA = a; config.MarkChanged(); }
        var s = config.TrackS;
        if (HuntUi.WrappedCheckbox("S and SS ranks", ref s)) { config.TrackS = s; config.MarkChanged(); }

    }

    private void DrawSeedingSection()
    {
        ImGui.Text("Seed from achievements");
        var auto = config.AutoSeedOnLogin;
        if (HuntUi.WrappedCheckbox("Check on login", ref auto))
        {
            config.AutoSeedOnLogin = auto;
            config.MarkChanged();
        }

        ImGui.BeginDisabled(seeder.IsRunning);
        if (ImGui.Button("Seed now"))
            seeder.Start();
        HuntUi.SameLineIfFits(ImGui.CalcTextSize("Re-resolve names").X + ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.Button("Re-resolve names"))
            seeder.ResolveAll();
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(seeder.Status))
            ImGui.TextDisabled(seeder.Status);

        if (config.LastSeeded != default)
            ImGui.TextDisabled($"Last seeded {config.LastSeeded:yyyy-MM-dd HH:mm}");

        var profile = characters.Current;
        if (profile is null)
        {
            ImGui.TextDisabled("Log in to seed. Achievement progress is per-character.");
            return;
        }

        ImGui.TextDisabled($"Editing {profile.Display}");

        DrawSeedTable(profile);
        DrawDrift(profile);
        DrawSeedOutcomes();
    }

    private void DrawSeedTable(CharacterProfile profile)
    {
        if (!ImGui.BeginTable("##seeds", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Counter", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Achievement", ImGuiTableColumnFlags.WidthStretch, 3);
        ImGui.TableSetupColumn("Seeded", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableHeadersRow();

        foreach (var def in SeedDefinitions.All)
        {
            var key = def.CategoryKey;
            var resolved = config.ResolvedAchievements.GetValueOrDefault(key);

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(key);

            // Name comes from the seeder's cache. Reading it from the Excel
            // sheet here meant a sheet lookup and a string allocation per row
            // per frame for as long as this window was open.
            ImGui.TableNextColumn();
            if (resolved == 0)
                ImGui.TextDisabled($"{def.NamePrefix} (unresolved)");
            else
                ImGui.TextUnformatted(seeder.NameOf(key));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(resolved == 0 ? $"{def.NamePrefix} (unresolved)" : seeder.NameOf(key));

            // Baseline stays editable: it is the only way to correct a bad read
            // or to fill in a counter whose achievement is already complete.
            ImGui.TableNextColumn();
            var baseline = profile.BaselineFor(key);
            ImGui.SetNextItemWidth(-1);
            ImGui.PushID(key);
            if (ImGui.InputInt("##base", ref baseline, 1))
            {
                profile.CategoryBaselines[key] = Math.Max(0, baseline);
                config.MarkChanged();
            }
            ImGui.PopID();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(profile.TotalFor(key).ToString());
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Shows where the local tally has run ahead of the game's own count.
    ///
    /// The plugin counts a mark when one of your actions hits it, but the game
    /// only awards credit above a contribution threshold it does not expose. A
    /// tally therefore creeps upward, and without this the only symptom is a
    /// number that is quietly wrong forever.
    /// </summary>
    private void DrawDrift(CharacterProfile profile)
    {
        ImGui.Spacing();

        if (!profile.HasAchievementReads)
        {
            ImGui.TextDisabled("Seed once to compare this tally against your achievements.");
            return;
        }

        var drifted = new List<string>();
        foreach (var def in SeedDefinitions.All)
        {
            if (profile.DriftFor(def.CategoryKey) > 0)
                drifted.Add(def.CategoryKey);
        }

        if (drifted.Count == 0)
        {
            ImGui.TextColored(HuntTheme.Success,
                "In step with your achievements.");
        }
        else
        {
            ImGui.TextColored(HuntTheme.Warning,
                $"Ahead of your achievements in {drifted.Count} "
                + (drifted.Count == 1 ? "counter:" : "counters:"));

            foreach (var key in drifted)
            {
                var reported = profile.AchievementReads.GetValueOrDefault(key);
                ImGui.TextDisabled(
                    $"    {key}: tally {profile.TotalFor(key)}, achievement {reported} "
                    + $"(+{profile.DriftFor(key)})");
            }

            ImGui.TextDisabled("Lower that counter's baseline above to bring it back in line.");
        }

        if (profile.AchievementReadAt != default)
            ImGui.TextDisabled($"Compared against readings from {profile.AchievementReadAt:yyyy-MM-dd HH:mm}.");

    }

    private void DrawSeedOutcomes()
    {
        if (seeder.Outcomes.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.Text("Last run");

        foreach (var def in SeedDefinitions.All)
        {
            if (!seeder.Outcomes.TryGetValue(def.CategoryKey, out var outcome))
                continue;

            // Anything the user has to act on is worth more than grey text.
            var needsAction = outcome.StartsWith("Complete", StringComparison.Ordinal);
            if (needsAction)
                ImGui.TextColored(HuntTheme.Warning, $"{def.CategoryKey}: {outcome}");
            else
                ImGui.TextDisabled($"{def.CategoryKey}: {outcome}");
        }
    }

    private void DrawResetSection()
    {
        if (!confirmReset)
        {
            if (ImGui.Button("Reset all characters"))
                confirmReset = true;
            return;
        }

        ImGui.TextColored(HuntTheme.Danger,
            "Deletes every character's tally. This cannot be undone.");
        if (ImGui.Button("Confirm reset"))
        {
            config.Characters.Clear();
            config.LastSeeded = default;

            // The context caches a profile object. Without this it would keep
            // handing out the one just detached from the configuration, and
            // kills would be recorded onto an orphan.
            characters.Invalidate();

            config.MarkChanged();
            config.Flush(force: true);
            confirmReset = false;
        }
        HuntUi.SameLineIfFits(ImGui.CalcTextSize("Cancel").X + ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.Button("Cancel"))
            confirmReset = false;
    }

}
