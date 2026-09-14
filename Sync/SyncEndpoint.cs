using System;
namespace HuntHelperEvolved.Sync;
public static class SyncEndpoint
{
    public static bool TryBuild(string raw, out Uri uri, out string problem, bool allowPlaintext = false)
    {
        uri = null!;
        problem = string.Empty;

        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            problem = "No server URL set.";
            return false;
        }

        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "wss://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            problem = "That server URL could not be read.";
            return false;
        }

        var scheme = parsed.Scheme.ToLowerInvariant() switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" => "ws",
            "wss" => "wss",
            _ => null,
        };

        if (scheme is null)
        {
            problem = "The server URL needs to start with wss:// (or ws:// for a LAN).";
            return false;
        }

        if (scheme=="ws" && !allowPlaintext)
        { problem="Use wss:// for encrypted sharing, or explicitly enable plaintext development connections.";return false; }
        if (!string.IsNullOrEmpty(parsed.UserInfo)) { problem="Do not put credentials in the server URL.";return false; }
        var builder = new UriBuilder(parsed) { Scheme = scheme };
        if (string.IsNullOrEmpty(builder.Path) || builder.Path == "/") builder.Path = "/ws";
        uri = builder.Uri;
        return true;
    }

}
