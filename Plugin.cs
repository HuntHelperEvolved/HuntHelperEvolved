using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace HuntTrainRelay;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Hunt Train Relay";

    private const string ConfigCommand = "/htr";
    private const int MaxWebhooks = 5;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;

    private readonly Configuration _config;
    private readonly HuntHelperIpc _ipc;
    private readonly TrainWatcher _watcher;

    private bool _configWindowVisible;
    private string _lastPostResult = string.Empty;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        ICommandManager commandManager,
        IChatGui chatGui,
        IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _chatGui = chatGui;
        _log = pluginLog;

        _config = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _config.Initialize(_pluginInterface);

        _ipc = new HuntHelperIpc(_pluginInterface);
        _watcher = new TrainWatcher(framework, _ipc, _config);
        _watcher.TrainCompleted += OnTrainCompleted;

        _commandManager.AddHandler(ConfigCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Hunt Train Relay settings.",
        });

        _pluginInterface.UiBuilder.Draw += DrawUI;
        _pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
    }

    private void OnCommand(string command, string args) => _configWindowVisible = true;

    private void OnOpenConfigUi() => _configWindowVisible = true;

    private void OnTrainCompleted(List<TrackedMark> marks)
    {
        _log.Info($"Hunt Train Relay: train complete, {marks.Count} marks. Posting to Discord...");
        _ = PostAsync(marks);
    }

    private async Task PostAsync(List<TrackedMark> marks)
    {
        var (success, message) = await DiscordRelay.PostTrainCompleteAsync(_config.WebhookUrls, marks);
        _lastPostResult = message;

        if (success)
        {
            _chatGui.Print($"[Hunt Train Relay] Posted train summary to Discord ({marks.Count} marks).");
        }
        else
        {
            _chatGui.PrintError($"[Hunt Train Relay] Failed to post to Discord: {message}");
            _log.Error($"Hunt Train Relay post failed: {message}");
        }
    }

    private async Task SendTestAsync()
    {
        var (success, message) = await DiscordRelay.PostTestAsync(_config.WebhookUrls);
        _lastPostResult = message;
        if (!success) _log.Error($"Hunt Train Relay test post failed: {message}");
    }

    private async Task SendScoutingReportAsync()
    {
        var list = _ipc.TryGetTrainList();
        if (list == null)
        {
            _lastPostResult = "Hunt Helper not detected — can't build a scouting report.";
            return;
        }

        var (success, message) = await DiscordRelay.PostScoutingReportAsync(_config.WebhookUrls, list);
        _lastPostResult = message;
        if (!success) _log.Error($"Hunt Train Relay scouting report failed: {message}");
    }

    /// <summary>
    /// Manual fallback for "End Train": reads Hunt Helper's train list directly,
    /// completely independent of the auto-detect polling loop, so it still works
    /// even if auto-post was never turned on or somehow missed the trigger.
    /// </summary>
    private async Task EndTrainNowAsync()
    {
        var list = _ipc.TryGetTrainList();
        if (list == null)
        {
            _lastPostResult = "Hunt Helper not detected — nothing to post.";
            return;
        }

        if (list.Count == 0)
        {
            _lastPostResult = "Nothing to post — Hunt Helper's train list is empty.";
            return;
        }

        // Prefer a death time the auto-detect loop actually observed live (accurate
        // to the moment it happened). Hunt Helper's own LastSeenUTC isn't a reliable
        // stand-in for time-of-death — it can be stale for a mark that's been dead
        // a while — so for anything we never personally saw transition, treat the
        // button press itself as the reference time instead of trusting that field.
        var now = DateTime.UtcNow;
        var marks = list.Select(m => new TrackedMark
        {
            Name = m.Name,
            ModelId = m.MobID,
            Instance = m.Instance,
            Dead = m.Dead,
            LastSeenUtc = m.LastSeenUTC,
            DeathObservedAtUtc = m.Dead
                ? (_watcher.GetObservedDeathTime(m.MobID, m.Instance) ?? now)
                : null,
        }).ToList();

        var (success, message) = await DiscordRelay.PostTrainCompleteAsync(_config.WebhookUrls, marks);
        _lastPostResult = message;
        if (!success) _log.Error($"Hunt Train Relay manual end-train post failed: {message}");

        // Clear internal auto-detect tracking too, so it doesn't also fire a
        // duplicate post later for a train we've just ended by hand.
        _watcher.ResetNow();
    }

    private void DrawUI()
    {
        if (!_configWindowVisible) return;

        ImGui.SetNextWindowSize(new Vector2(460, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Hunt Train Relay", ref _configWindowVisible))
        {
            if (ImGui.BeginTabBar("HuntTrainRelayTabs"))
            {
                if (ImGui.BeginTabItem("Conductor"))
                {
                    DrawConductorTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Scout"))
                {
                    DrawScoutTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettingsTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            if (!string.IsNullOrEmpty(_lastPostResult))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextWrapped($"Last post: {_lastPostResult}");
            }
        }
        ImGui.End();
    }

    private void DrawConductorTab()
    {
        ImGui.Spacing();

        var autoPost = _config.AutoPostEnabled;
        if (ImGui.Checkbox("I'm conducting - auto-post when the train is cleared", ref autoPost))
        {
            _config.AutoPostEnabled = autoPost;
            _config.Save();
        }
        ImGui.TextDisabled("Only enable this on the conductor's own client to avoid duplicate posts.");

        ImGui.Spacing();
        ImGui.TextWrapped($"Status: {_watcher.LastStatus}");

        ImGui.Spacing();
        if (ImGui.Button("Reset train tracking now"))
        {
            _watcher.ResetNow();
        }

        ImGui.SameLine();
        if (ImGui.Button("End Train Now"))
        {
            _ = EndTrainNowAsync();
        }
        ImGui.TextDisabled("Manual fallback — posts Hunt Helper's current train list as-is, in case auto-post didn't fire.");
    }

    private void DrawScoutTab()
    {
        ImGui.Spacing();
        if (ImGui.Button("Send Scouting Report"))
        {
            _ = SendScoutingReportAsync();
        }
        ImGui.TextDisabled("Posts Hunt Helper's current train list as a paste-able import code, plus a per-expansion up count.");
    }

    private void DrawSettingsTab()
    {
        ImGui.Spacing();
        if (ImGui.Button("Send test message"))
        {
            _ = SendTestAsync();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextWrapped(
            "Webhook URLs — one per Discord server you want this to post to. Create one in " +
            "Discord via Channel Settings > Integrations > Webhooks > New Webhook > Copy Webhook URL."
        );
        ImGui.Spacing();

        DrawWebhookList();

        ImGui.Spacing();
        ImGui.Separator();

        var pollInterval = _config.PollIntervalSeconds;
        if (ImGui.InputInt("Check interval (seconds)", ref pollInterval))
        {
            _config.PollIntervalSeconds = Math.Clamp(pollInterval, 1, 30);
            _config.Save();
        }
    }

    private void DrawWebhookList()
    {
        int? toRemove = null;

        for (var i = 0; i < _config.WebhookUrls.Count; i++)
        {
            ImGui.PushID(i);

            var url = _config.WebhookUrls[i];
            ImGui.SetNextItemWidth(320);
            if (ImGui.InputText("##webhookUrl", ref url, 512))
            {
                _config.WebhookUrls[i] = url;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _config.Save();
            }

            if (_config.WebhookUrls.Count > 1)
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove"))
                {
                    toRemove = i;
                }
            }

            ImGui.PopID();
        }

        if (toRemove.HasValue)
        {
            _config.WebhookUrls.RemoveAt(toRemove.Value);
            if (_config.WebhookUrls.Count == 0) _config.WebhookUrls.Add(string.Empty);
            _config.Save();
        }

        if (_config.WebhookUrls.Count < MaxWebhooks)
        {
            if (ImGui.Button("+ Add webhook"))
            {
                _config.WebhookUrls.Add(string.Empty);
                _config.Save();
            }
        }
        else
        {
            ImGui.TextDisabled($"Maximum of {MaxWebhooks} webhooks reached.");
        }
    }

    public void Dispose()
    {
        _watcher.TrainCompleted -= OnTrainCompleted;
        _watcher.Dispose();
        _pluginInterface.UiBuilder.Draw -= DrawUI;
        _pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        _commandManager.RemoveHandler(ConfigCommand);
    }
}
