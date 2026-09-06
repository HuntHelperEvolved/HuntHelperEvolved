using System.Net.WebSockets;
using System.Text;
using Dalamud.Plugin.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HuntHelperEvolved.Sync.Tests;
public class TransportTests
{
    private static async Task WaitFor(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
    [Fact]
    public async Task RapidServerSwitchCannotDeliverOldFramesOrStopNewConnection()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        await using var app = builder.Build(); app.UseWebSockets();
        app.Map("/ws", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                var buffer = new byte[8192];
                await socket.ReceiveAsync(buffer, context.RequestAborted);
                var version = context.Request.Query["v"].ToString();
                if (version == "old") await Task.Delay(150, context.RequestAborted);
                var json = "{\"type\":\"welcome\",\"protocol\":2,\"serverVersion\":\"" + version + "\"}";
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, context.RequestAborted);
                while (socket.State == WebSocketState.Open) await socket.ReceiveAsync(buffer, context.RequestAborted);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        });
        await app.StartAsync();
        using var client = new SyncClient(new TestLog());
        var url = app.Urls.Single().Replace("http://", "ws://") + "/ws?v=";
        client.Start(new Uri(url + "old"), () => new());
        await Task.Delay(50);
        client.Start(new Uri(url + "new"), () => new());
        await WaitFor(() => client.IsConnected);
        await Task.Delay(250);
        Assert.True(client.IsConnected);
        Assert.True(client.TryDequeue(out var type, out var payload));
        Assert.Equal("welcome", type); Assert.Equal("new", payload.Value<string>("serverVersion"));
        Assert.False(client.TryDequeue(out _, out _));
        client.Stop(); Assert.False(client.IsConnected);
        await app.StopAsync();
    }
}
