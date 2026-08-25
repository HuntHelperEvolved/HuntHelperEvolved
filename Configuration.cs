using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace HuntTrainRelay;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>
    /// Discord "Incoming Webhook" URLs, created in Discord via Channel Settings >
    /// Integrations > Webhooks > New Webhook > Copy Webhook URL. Every report is
    /// posted to every non-empty URL in this list, so multiple Discord servers can
    /// each get their own copy. Anyone with one of these URLs can post to that
    /// channel, so treat them like passwords.
    /// </summary>
    public List<string> WebhookUrls { get; set; } = new() { string.Empty };

    /// <summary>
    /// Legacy single-webhook field from before multi-webhook support. Only kept so
    /// existing saved configs migrate into WebhookUrls automatically; not used
    /// otherwise.
    /// </summary>
    [Obsolete("Use WebhookUrls instead. Kept only for migrating old saved configs.")]
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Only the conductor actively recording the train should have this on,
    /// to avoid two clients both posting the same "train complete" message.
    /// </summary>
    public bool AutoPostEnabled { get; set; } = false;

    /// <summary>
    /// How often (in seconds) to check Hunt Helper's train list for changes.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 3;

    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

#pragma warning disable CS0618 // reading the obsolete field deliberately, once, to migrate it
        if ((WebhookUrls == null || WebhookUrls.Count == 0 || WebhookUrls.TrueForAll(string.IsNullOrWhiteSpace))
            && !string.IsNullOrWhiteSpace(WebhookUrl))
        {
            WebhookUrls = new List<string> { WebhookUrl! };
            WebhookUrl = null;
            Save();
        }
#pragma warning restore CS0618

        if (WebhookUrls == null || WebhookUrls.Count == 0)
        {
            WebhookUrls = new List<string> { string.Empty };
        }
    }

    public void Save()
    {
        _pluginInterface?.SavePluginConfig(this);
    }
}
