using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelperEvolved;

internal sealed record DiscordEmbedPreview(string Title, string Description);

/// <summary>A materialized report: the preview and HTTP payloads share the same text.</summary>
internal sealed class PreparedDiscordReport
{
    private const string DefaultEmptyMessage = "Nothing to report — the train list is empty.";
    public IReadOnlyList<DiscordEmbedPreview> Embeds { get; }
    public IReadOnlyList<object> Messages { get; }
    public int MessageCount => Messages.Count;
    internal string EmptyMessage { get; }

    internal PreparedDiscordReport(IEnumerable<DiscordEmbedPreview> embeds, bool packEmbeds,
        string emptyMessage = DefaultEmptyMessage) : this(Pack(embeds, packEmbeds), emptyMessage) { }

    private PreparedDiscordReport(IReadOnlyList<IReadOnlyList<DiscordEmbedPreview>> messageEmbeds, string emptyMessage)
    {
        var snapshot = messageEmbeds.Select(message => message.ToList().AsReadOnly()).ToList();
        if (snapshot.Any(message => !FitsOneMessage(message)))
            throw new ArgumentException("Discord message text exceeds its limit.", nameof(messageEmbeds));
        Embeds = snapshot.SelectMany(message => message).ToList().AsReadOnly();
        Messages = snapshot.Select(message => Payload(message)).ToList().AsReadOnly();
        EmptyMessage = emptyMessage;
    }

    internal static bool FitsOneMessage(IReadOnlyList<DiscordEmbedPreview> embeds) =>
        embeds.Count is > 0 and <= 10
        && embeds.All(embed => embed.Title.Length <= 256 && embed.Description.Length <= 4096)
        && embeds.Sum(embed => (long)embed.Title.Length + embed.Description.Length) <= 6000;

    /// <summary>Preserves explicit message boundaries after validating every complete message.</summary>
    internal static PreparedDiscordReport FromMessages(params IReadOnlyList<DiscordEmbedPreview>[] messages) =>
        new(messages, DefaultEmptyMessage);

    private static IReadOnlyList<IReadOnlyList<DiscordEmbedPreview>> Pack(IEnumerable<DiscordEmbedPreview> embeds, bool packEmbeds)
    {
        var messages = new List<IReadOnlyList<DiscordEmbedPreview>>();
        var batch = new List<DiscordEmbedPreview>();
        var textLength = 0;
        foreach (var embed in embeds)
        {
            if (embed.Title.Length > 256 || embed.Description.Length > 4096)
                throw new ArgumentException("Discord embed text exceeds its limit.", nameof(embeds));
            var length = embed.Title.Length + embed.Description.Length;
            if (batch.Count > 0 && (!packEmbeds || batch.Count == 10 || textLength + length > 6000))
            {
                messages.Add(batch);
                batch = new List<DiscordEmbedPreview>();
                textLength = 0;
            }
            batch.Add(embed);
            textLength += length;
        }
        if (batch.Count > 0) messages.Add(batch);
        return messages;
    }

    private static object Payload(IEnumerable<DiscordEmbedPreview> embeds) => new
    {
        embeds = embeds.Select(e => new { title = e.Title, description = e.Description, color = 3066993 }).ToList().AsReadOnly(),
    };
}
