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
    private readonly ByteBudgetQueue<(string Type, JObject Payload)> _inbox = new(128, 8 * 1024 * 1024);
    private sealed class OutgoingQueue
    {
        private readonly Channel<string> _channel=Channel.CreateBounded<string>(128);
        private long _bytes;
        public ChannelWriter<string> Writer=>_channel.Writer;
        public ChannelReader<string> Reader=>_channel.Reader;
        public bool TryWrite(string json)
        {
            var bytes=Encoding.UTF8.GetByteCount(json);
            if(bytes>1024*1024)return false;
            if(Interlocked.Add(ref _bytes,bytes)>8*1024*1024 || !Writer.TryWrite(json))
            { Interlocked.Add(ref _bytes,-bytes);return false; }
            return true;
        }
        public void Received(string json)=>Interlocked.Add(ref _bytes,-Encoding.UTF8.GetByteCount(json));
    }
    private OutgoingQueue? _outbox;
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
            _inbox.Clear();
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
            var json=SyncProtocol.Serialize(message);
            if (!_outbox.TryWrite(json)) _connection?.Cancel();
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
            var queue = new OutgoingQueue();
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
                    var received = await ReadAsync(socket, timeout.Token).ConfigureAwait(false);
                    if (received is null) break;
                    var (payload, receivedBytes)=received.Value;
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
                        if (!_inbox.TryEnqueue((type,payload),receivedBytes))
                        { _inbox.Clear(); StatusText="Sync backlog exceeded; reconnecting for a fresh snapshot…"; connection.Cancel(); }
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
                Publish(generation, () => { _outbox = null; _connection = null; ConnectedAtUtc = null; State = ConnectionState.Failed; _inbox.Clear(); });
            }
            if (fatal || ct.IsCancellationRequested) return;
            Publish(generation, () => StatusText = "Disconnected; reconnecting…");
            try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            backoff = Math.Min(backoff * 2, 60);
        }
    }

    private static async Task WriteAsync(ClientWebSocket socket, OutgoingQueue queue, CancellationTokenSource connection)
    {
        try
        {
            await foreach (var json in queue.Reader.ReadAllAsync(connection.Token).ConfigureAwait(false))
            {
                queue.Received(json);
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, connection.Token).ConfigureAwait(false);
            }
        }
        finally { connection.Cancel(); }
    }

    private static async Task<(JObject Payload,int Bytes)?> ReadAsync(ClientWebSocket socket, CancellationToken ct)
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
        using var reader = new LimitedJsonReader(new System.IO.StringReader(Encoding.UTF8.GetString(message.GetBuffer(),0,(int)message.Length))) { MaxDepth=32 };
        return (JObject.Load(reader), (int)message.Length);
    }
    public void Dispose() => Stop();
}
