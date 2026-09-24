using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

internal sealed record DiscordEmbedPreview(string Title, string Description);

/// <summary>A materialized report: the preview and HTTP payloads share the same text.</summary>
internal sealed class PreparedDiscordReport
{
    public IReadOnlyList<DiscordEmbedPreview> Embeds { get; }
    public IReadOnlyList<object> Messages { get; }
    public int MessageCount => Messages.Count;
    public bool ExportCodeOmitted { get; }
    internal string EmptyMessage { get; }

    internal PreparedDiscordReport(IEnumerable<DiscordEmbedPreview> embeds, bool packEmbeds,
        bool exportCodeOmitted = false, string emptyMessage = "Nothing to report — the train list is empty.")
    {
        var snapshot = embeds.ToList().AsReadOnly();
        Embeds = snapshot;
        ExportCodeOmitted = exportCodeOmitted;
        EmptyMessage = emptyMessage;
        var messages = new List<object>();
        var batch = new List<DiscordEmbedPreview>();
        var textLength = 0;
        foreach (var embed in snapshot)
        {
            if (embed.Title.Length > 256 || embed.Description.Length > 4096)
                throw new ArgumentException("Discord embed text exceeds its limit.", nameof(embeds));
            var length = embed.Title.Length + embed.Description.Length;
            if (batch.Count > 0 && (!packEmbeds || batch.Count == 10 || textLength + length > 6000))
            {
                messages.Add(Payload(batch));
                batch.Clear();
                textLength = 0;
            }
            batch.Add(embed);
            textLength += length;
        }
        if (batch.Count > 0) messages.Add(Payload(batch));
        Messages = messages.AsReadOnly();
    }

    private static object Payload(IEnumerable<DiscordEmbedPreview> embeds) => new
    {
        embeds = embeds.Select(e => new { title = e.Title, description = e.Description, color = 3066993 }).ToList().AsReadOnly(),
    };
}
