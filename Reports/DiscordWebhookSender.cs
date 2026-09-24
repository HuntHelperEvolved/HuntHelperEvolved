using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelperEvolved;

internal static class DiscordWebhookSender
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(15);
    private const int MaximumRateLimitRetries = 2;
    private const int ErrorTextLimit = 512;

    internal static async Task<(bool Success, string Message)> SendAsync(List<WebhookEntry>? webhooks,
        IReadOnlyList<object> messages, CancellationToken cancellationToken, HttpClient? client = null)
    {
        var targets = (webhooks ?? new List<WebhookEntry>())
            .Where(w => w.Enabled && !string.IsNullOrWhiteSpace(w.Url))
            .Select(w => w.Url).Distinct().ToList();
        if (targets.Count == 0) return (false, "No enabled webhook configured.");

        var payloads = messages.Select(JsonConvert.SerializeObject).ToList();
        var successCount = 0;
        var deliveredCount = 0;
        var failures = new List<string>();

        for (var target = 0; target < targets.Count; target++)
        {
            var sent = 0;
            foreach (var json in payloads)
            {
                var (success, message) = await SendRawAsync(client ?? Http, targets[target], json, cancellationToken).ConfigureAwait(false);
                if (!success)
                {
                    failures.Add($"Webhook {target + 1}: {sent}/{payloads.Count} messages confirmed. {message}");
                    break;
                }
                sent++;
                deliveredCount++;
            }
            if (sent == payloads.Count) successCount++;
            if (cancellationToken.IsCancellationRequested) break;
        }

        if (successCount == targets.Count && !cancellationToken.IsCancellationRequested)
            return (true, $"Posted to {successCount} webhook{(successCount == 1 ? "" : "s")} at {DateTime.Now:T}.");

        var detail = string.Join(" | ", failures.Take(3));
        if (failures.Count > 3) detail += $" | {failures.Count - 3} more webhook failures.";
        var partial = deliveredCount > 0 ? " Some messages were delivered; check Discord before retrying to avoid duplicates." : "";
        return (false, $"Posted to {successCount}/{targets.Count} webhooks. {detail}{partial}");
    }

    private static async Task<(bool Success, string Message)> SendRawAsync(HttpClient client, string webhookUrl,
        string json, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                TimeSpan retryDelay;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(RequestTimeout);
                    using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return (true, "OK");

                    var body = await ReadErrorAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaximumRateLimitRetries)
                        return (false, $"Discord returned {(int)response.StatusCode}: {CleanError(body)}");

                    retryDelay = RetryDelay(response, body);
                    if (retryDelay > MaximumRetryDelay)
                        return (false, "Discord is rate limiting requests; try again later.");
                }
                // Dispose the response before waiting, and only retry an explicit
                // 429. Retrying timeouts/5xx could duplicate an already-posted message.
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return (false, cancellationToken.IsCancellationRequested
                ? "Request cancelled; check Discord before retrying."
                : "Discord request timed out; delivery may have succeeded. Check Discord before retrying.");
        }
        catch (HttpRequestException)
        {
            // Exception messages may include a webhook URL containing credentials.
            return (false, "Could not reach Discord; check the webhook URL and connection before retrying.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UriFormatException)
        {
            return (false, "Discord request failed; check the webhook URL and connection before retrying.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[ErrorTextLimit + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
        }
        return new string(buffer, 0, read);
    }

    private static string CleanError(string text)
    {
        var result = new string(text.Take(ErrorTextLimit).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return result + (text.Length > ErrorTextLimit ? "…" : "");
    }

    internal static TimeSpan RetryDelay(HttpResponseMessage response, string body)
    {
        if (response.Headers.TryGetValues("Retry-After", out var headers))
        {
            var value = headers.FirstOrDefault();
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return DelayFromSeconds(seconds);
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                return DelayFromSeconds((date - DateTimeOffset.UtcNow).TotalSeconds);
        }
        try
        {
            var retry = JObject.Parse(body)["retry_after"];
            if (retry?.Type is JTokenType.Float or JTokenType.Integer)
                return DelayFromSeconds(retry.Value<double>());
        }
        catch (JsonException) { }
        return TimeSpan.FromSeconds(1);
    }

    private static TimeSpan DelayFromSeconds(double seconds)
    {
        // Reject an excessive delay instead of retrying earlier than Discord asked.
        if (!double.IsFinite(seconds) || seconds > MaximumRetryDelay.TotalSeconds)
            return MaximumRetryDelay + TimeSpan.FromSeconds(1);
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }
}
