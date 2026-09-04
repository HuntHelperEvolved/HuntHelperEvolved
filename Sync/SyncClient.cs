using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HuntHelperEvolved.Sync;

/// <summary>
/// The socket to the sync server, and nothing else: connect, say hello,
/// keep a queue in each direction, reconnect when it drops.
///
/// Everything game-side happens on the framework thread, and this class
/// never touches the game. Received frames are parked in a queue for the
/// coordinator to drain on that thread; outgoing frames are queued here
/// from it and written by one writer task. The hello is built by the
/// coordinator too, ahead of time, so the socket thread never asks the
/// game anything.
/// </summary>
public sealed class SyncClient : IDisposable
{
    public enum ConnectionState { Off, Connecting, Connected, Failed }

    private const int OutboxCapacity = 2048;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly IPluginLog _log;
    private readonly ConcurrentQueue<(string Type, JObject Payload)> _inbox = new();

    private Channel<string>? _outbox;
    private CancellationTokenSource? _cts;
    private Task? _runner;
    private Uri? _uri;
    private Func<HelloMessage>? _hello;
    private int _generation;

    public ConnectionState State { get; private set; } = ConnectionState.Off;
    public string StatusText { get; private set; } = "Off.";

    /// <summary>
    /// Set when the server refused us for a reason retrying will not fix —
    /// wrong password, protocol mismatch. Cleared by Start.
    /// </summary>
    public bool FatalError { get; private set; }

    public bool IsConnected => State == ConnectionState.Connected;
    public DateTime? ConnectedAtUtc { get; private set; }

    public SyncClient(IPluginLog log)
    {
        _log = log;
    }

    /// <summary>Begins connecting, and keeps trying until Stop. Safe to call again with new settings.</summary>
    public void Start(Uri uri, Func<HelloMessage> hello)
    {
        Stop();
        _uri = uri;
        _hello = hello;
        FatalError = false;
        _cts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _generation);
        _runner = Task.Run(() => RunAsync(uri, hello, generation, _cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;

        try { cts.Cancel(); } catch { /* already gone */ }
        cts.Dispose();
        _outbox?.Writer.TryComplete();
        _outbox = null;
        State = ConnectionState.Off;
        StatusText = "Off.";
        ConnectedAtUtc = null;
        while (_inbox.TryDequeue(out _)) { }
    }

    public bool TryDequeue(out string type, out JObject payload)
    {
        if (_inbox.TryDequeue(out var item))
        {
            type = item.Type;
            payload = item.Payload;
            return true;
        }

        type = string.Empty;
        payload = null!;
        return false;
    }

    /// <summary>
    /// Queues a message. Dropped silently when not connected: the coordinator
    /// resends everything that matters from scratch after each welcome, so
    /// nothing queued before then would be worth delivering.
    /// </summary>
    public void Send(object message)
    {
        if (State != ConnectionState.Connected) return;
        var outbox = _outbox;
        if (outbox is null) return;

        if (!outbox.Writer.TryWrite(SyncProtocol.Serialize(message)))
        {
            // The server is not draining us. Drop the connection; the
            // reconnect brings a fresh snapshot, which is the honest fix.
            _log.Warning("Sync outbox is full; reconnecting.");
            outbox.Writer.TryComplete();
        }
    }

    private async Task RunAsync(Uri uri, Func<HelloMessage> hello, int generation, CancellationToken ct)
    {
        var backoffSeconds = 2;

        while (!ct.IsCancellationRequested)
        {
            if (generation != _generation) return;

            State = ConnectionState.Connecting;
            StatusText = $"Connecting to {uri.Host}…";

            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            var outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(OutboxCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });

            var welcomed = false;
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    await socket.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                }

                var helloJson = SyncProtocol.Serialize(hello());
                await socket.SendAsync(Encoding.UTF8.GetBytes(helloJson), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

                _outbox = outbox;
                var writer = WriteLoopAsync(socket, outbox, ct);
                welcomed = await ReadLoopAsync(socket, ct).ConfigureAwait(false);

                outbox.Writer.TryComplete();
                try { await writer.ConfigureAwait(false); } catch { /* closing */ }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                State = ConnectionState.Failed;
                StatusText = $"Connection failed: {Shorten(ex.Message)}";
                _log.Debug(ex, "Sync connection failed.");
            }
            finally
            {
                _outbox = null;
                ConnectedAtUtc = null;
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token).ConfigureAwait(false);
                    }
                    catch { /* already gone */ }
                }
            }

            if (ct.IsCancellationRequested) break;

            if (FatalError)
            {
                State = ConnectionState.Failed;
                break;
            }

            if (State == ConnectionState.Connected || welcomed)
            {
                State = ConnectionState.Failed;
                StatusText = "Disconnected; reconnecting…";
                backoffSeconds = 2;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            backoffSeconds = Math.Min(backoffSeconds * 2, 60);
        }

        if (!FatalError)
        {
            State = ConnectionState.Off;
            StatusText = "Off.";
        }
    }

    private static async Task WriteLoopAsync(ClientWebSocket socket, Channel<string> outbox, CancellationToken ct)
    {
        try
        {
            await foreach (var json in outbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Reads until the socket closes. True if a welcome arrived at any point.</summary>
    private async Task<bool> ReadLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        using var message = new System.IO.MemoryStream();
        var welcomed = false;

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    StatusText = $"Server closed the connection ({result.CloseStatusDescription ?? "no reason"}).";
                    return welcomed;
                }
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);

            JObject payload;
            try
            {
                payload = JObject.Parse(text);
            }
            catch (Exception ex)
            {
                _log.Warning($"Sync: unreadable frame from the server: {Shorten(ex.Message)}");
                continue;
            }

            var type = payload.Value<string>("type") ?? string.Empty;

            switch (type)
            {
                case ServerMessageTypes.Welcome:
                    welcomed = true;
                    State = ConnectionState.Connected;
                    ConnectedAtUtc = DateTime.UtcNow;
                    StatusText = "Connected.";
                    break;

                case ServerMessageTypes.Error:
                {
                    var code = payload.Value<string>("code") ?? string.Empty;
                    var reason = payload.Value<string>("message") ?? "The server refused the connection.";
                    if (code is "auth" or "protocol")
                    {
                        FatalError = true;
                        StatusText = reason;
                    }
                    break;
                }
            }

            _inbox.Enqueue((type, payload));
        }

        return welcomed;
    }

    private static string Shorten(string message) =>
        message.Length > 160 ? message[..160] + "…" : message;

    public void Dispose() => Stop();
}
