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

/// <summary>One socket worker per generation. Game state is only read by the coordinator.</summary>
public sealed class SyncClient : IDisposable
{
    public enum ConnectionState { Off, Connecting, Connected, Failed }
    private const int MaxMessageBytes = 4 * 1024 * 1024;
    private readonly IPluginLog _log;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<(string Type, JObject Payload)> _inbox = new();
    private Channel<string>? _outbox;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _connection;
    private int _generation;
    public ConnectionState State { get; private set; }
    public string StatusText { get; private set; } = "Off.";
    public bool FatalError { get; private set; }
    public bool IsConnected => State == ConnectionState.Connected;
    public DateTime? ConnectedAtUtc { get; private set; }
    public SyncClient(IPluginLog log) => _log = log;

    public void Start(Uri uri, Func<HelloMessage> hello)
    {
        Stop();
        lock (_gate)
        {
            var cts = new CancellationTokenSource();
            _cts = cts;
            var generation = ++_generation;
            FatalError = false;
            _ = Task.Run(async () =>
            {
                try { await RunAsync(uri, hello, generation, cts.Token).ConfigureAwait(false); }
                finally { lock (_gate) { if (_cts == cts) _cts = null; cts.Dispose(); } }
            });
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            ++_generation;
            _cts?.Cancel();
            _cts = null;
            _connection?.Cancel();
            _connection = null;
            _outbox?.Writer.TryComplete();
            _outbox = null;
            State = ConnectionState.Off;
            StatusText = "Off.";
            ConnectedAtUtc = null;
            while (_inbox.TryDequeue(out _)) { }
        }
    }

    public bool TryDequeue(out string type, out JObject payload)
    {
        if (_inbox.TryDequeue(out var item)) { type = item.Type; payload = item.Payload; return true; }
        type = string.Empty;
        payload = null!;
        return false;
    }

    public void Send(object message)
    {
        lock (_gate)
        {
            if (!IsConnected || _outbox is null) return;
            if (!_outbox.Writer.TryWrite(SyncProtocol.Serialize(message))) _connection?.Cancel();
        }
    }

    private void Publish(int generation, Action change)
    {
        lock (_gate) { if (generation == _generation) change(); }
    }

    private async Task RunAsync(Uri uri, Func<HelloMessage> hello, int generation, CancellationToken ct)
    {
        var backoff = 2;
        while (!ct.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = connection.Token;
            var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
            Task? writer = null;
            var fatal = false;
            Publish(generation, () => { State = ConnectionState.Connecting; StatusText = $"Connecting to {uri.Host}…"; _connection = connection; });
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                    await socket.SendAsync(Encoding.UTF8.GetBytes(SyncProtocol.Serialize(hello())), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
                }
                writer = WriteAsync(socket, queue, connection);
                var welcomed = false;
                while (!token.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(welcomed ? 90 : 15));
                    var payload = await ReadAsync(socket, timeout.Token).ConfigureAwait(false);
                    if (payload is null) break;
                    var type = payload.Value<string>("type") ?? string.Empty;
                    if (type == ServerMessageTypes.Error && payload.Value<string>("code") is "auth" or "protocol")
                    {
                        fatal = true;
                        Publish(generation, () => { FatalError = true; StatusText = payload.Value<string>("message") ?? "Connection refused."; });
                    }
                    if (type == ServerMessageTypes.Welcome)
                    {
                        if (welcomed || payload.Value<int>("protocol") != SyncProtocol.Version)
                            throw new InvalidOperationException("Unexpected sync protocol or duplicate welcome.");
                        welcomed = true;
                        backoff = 2;
                        Publish(generation, () => { _outbox = queue; State = ConnectionState.Connected; StatusText = "Connected."; ConnectedAtUtc = DateTime.UtcNow; });
                    }
                    else if (!welcomed && type != ServerMessageTypes.Error)
                        throw new InvalidOperationException("Server did not send a welcome.");
                    Publish(generation, () =>
                    {
                        if (_inbox.Count >= 4096) connection.Cancel();
                        else _inbox.Enqueue((type, payload));
                    });
                    if (fatal) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.Debug(ex, "Sync connection failed.");
                Publish(generation, () => StatusText = "Connection failed; reconnecting…");
            }
            finally
            {
                connection.Cancel();
                queue.Writer.TryComplete();
                if (writer is not null) { try { await writer.ConfigureAwait(false); } catch { } }
                socket.Abort();
                Publish(generation, () => { _outbox = null; _connection = null; ConnectedAtUtc = null; State = ConnectionState.Failed; });
            }
            if (fatal || ct.IsCancellationRequested) return;
            Publish(generation, () => StatusText = "Disconnected; reconnecting…");
            try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            backoff = Math.Min(backoff * 2, 60);
        }
    }

    private static async Task WriteAsync(ClientWebSocket socket, Channel<string> queue, CancellationTokenSource connection)
    {
        try
        {
            await foreach (var json in queue.Reader.ReadAllAsync(connection.Token).ConfigureAwait(false))
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, connection.Token).ConfigureAwait(false);
        }
        finally { connection.Cancel(); }
    }

    private static async Task<JObject?> ReadAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16384];
        using var message = new System.IO.MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("Expected JSON text.");
            if (message.Length + result.Count > MaxMessageBytes) throw new InvalidOperationException("Sync message too large.");
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return JObject.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
    }
    public void Dispose() => Stop();
}
