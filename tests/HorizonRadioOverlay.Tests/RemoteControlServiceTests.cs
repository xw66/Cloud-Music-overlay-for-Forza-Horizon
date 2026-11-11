using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class RemoteControlServiceTests
{
    private const string Token = "remote-test-token";

    [Fact]
    public async Task Listener_started_on_ui_context_accepts_connections_without_ui_dispatch()
    {
        RemoteControlService service;
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new UnpumpedSynchronizationContext());
            service = StartService();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (service)
        using (HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(service.LocalUrl)).StatusCode);
        }
    }

    [Fact]
    public async Task Homepage_does_not_disclose_token_and_apis_require_authorization()
    {
        using RemoteControlService service = StartService();
        using HttpClient client = new() { BaseAddress = new Uri(service.LocalUrl) };

        string page = await client.GetStringAsync("/");
        Assert.DoesNotContain(Token, page);
        Assert.Contains("new URLSearchParams(location.search)", page);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status?token=wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/control", new StringContent("{\"action\":\"next\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/status?token={Token}")).StatusCode);
    }

    [Theory]
    [InlineData(PlaybackCommandStatus.Succeeded, HttpStatusCode.OK)]
    [InlineData(PlaybackCommandStatus.Unsupported, HttpStatusCode.Conflict)]
    [InlineData(PlaybackCommandStatus.Failed, HttpStatusCode.BadGateway)]
    public async Task Control_reports_the_actual_playback_result(PlaybackCommandStatus result, HttpStatusCode expected)
    {
        string? receivedAction = null;
        using RemoteControlService service = StartService(action =>
        {
            receivedAction = action;
            return Task.FromResult(new PlaybackCommandResult(result, PlaybackSourceIds.Smtc));
        });
        using HttpClient client = new() { BaseAddress = new Uri(service.LocalUrl) };

        using HttpResponseMessage response = await client.PostAsync($"/api/control?token={Token}",
            new StringContent("{\"action\":\"next\"}", Encoding.UTF8, "application/json"));

        Assert.Equal("next", receivedAction);
        Assert.Equal(expected, response.StatusCode);
        Assert.Contains(result.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("65537")]
    public async Task Incomplete_or_invalid_control_body_never_executes_a_command(string contentLength)
    {
        bool executed = false;
        using RemoteControlService service = StartService(_ =>
        {
            executed = true;
            return Task.FromResult(new PlaybackCommandResult(PlaybackCommandStatus.Succeeded, PlaybackSourceIds.Smtc));
        });
        using TcpClient connection = new();
        await connection.ConnectAsync(IPAddress.Loopback, new Uri(service.LocalUrl).Port);
        NetworkStream stream = connection.GetStream();
        byte[] request = Encoding.ASCII.GetBytes(
            $"POST /api/control?token={Token} HTTP/1.1\r\nHost: localhost\r\nContent-Length: {contentLength}\r\n\r\n{{\"action\":\"next\"}}");
        await stream.WriteAsync(request);
        connection.Client.Shutdown(SocketShutdown.Send);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], deadline.Token));
        Assert.False(executed);
    }

    [Fact]
    public async Task Connections_above_limit_are_closed()
    {
        Channel<bool> entered = Channel.CreateUnbounded<bool>();
        TaskCompletionSource<RemoteControlStatus> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using RemoteControlService service = StartService(statusProvider: () =>
        {
            entered.Writer.TryWrite(true);
            return pending.Task;
        });
        int port = new Uri(service.LocalUrl).Port;
        List<TcpClient> connections = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
        try
        {
            for (int i = 0; i < RemoteControlService.MaxConnections; i++)
            {
                TcpClient connection = new();
                connections.Add(connection);
                await connection.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
                await connection.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                    $"GET /api/status?token={Token} HTTP/1.1\r\nHost: localhost\r\n\r\n"), deadline.Token);
                await entered.Reader.ReadAsync(deadline.Token);
            }

            using TcpClient extra = new();
            await extra.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            Assert.Equal(0, await extra.GetStream().ReadAsync(new byte[1], deadline.Token));
        }
        finally
        {
            pending.SetResult(new RemoteControlStatus());
            foreach (TcpClient connection in connections)
            {
                connection.Dispose();
            }
        }
    }

    [Fact]
    public async Task Partial_request_times_out_and_service_stays_available()
    {
        using RemoteControlService service = StartService();
        using TcpClient connection = new();
        await connection.ConnectAsync(IPAddress.Loopback, new Uri(service.LocalUrl).Port);
        NetworkStream stream = connection.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\n"));

        using CancellationTokenSource deadline = new(RemoteControlService.RequestTimeout + TimeSpan.FromSeconds(3));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], deadline.Token));
        using HttpClient client = new();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(service.LocalUrl)).StatusCode);
    }

    [Fact]
    public async Task Stop_closes_pending_connections_and_restart_uses_new_token()
    {
        using RemoteControlService service = StartService();
        int port = new Uri(service.LocalUrl).Port;
        using TcpClient connection = new();
        await connection.ConnectAsync(IPAddress.Loopback, port);
        NetworkStream stream = connection.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));
        using HttpClient client = new();
        await client.GetStringAsync(service.LocalUrl);

        service.Stop();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], deadline.Token));
        service.ApplySettings(new OverlaySettings
        {
            EnableRemoteControl = true,
            RemoteControlAllowLan = false,
            RemoteControlPort = port,
            RemoteControlToken = "new-token"
        });
        Assert.True(service.IsRunning, service.LastError);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"http://127.0.0.1:{port}/api/status?token={Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"http://127.0.0.1:{port}/api/status?token=new-token")).StatusCode);
    }

    private sealed class UnpumpedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // 模拟尚未处理消息的 UI 线程。
        }
    }

    private static RemoteControlService StartService(
        Func<string, Task<PlaybackCommandResult>>? handler = null,
        Func<Task<RemoteControlStatus>>? statusProvider = null)
    {
        TcpListener reservation = new(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        RemoteControlService service = new(
            statusProvider ?? (() => Task.FromResult(new RemoteControlStatus { Title = "Test track" })),
            handler ?? (_ => Task.FromResult(new PlaybackCommandResult(PlaybackCommandStatus.Succeeded, PlaybackSourceIds.Smtc))));
        service.ApplySettings(new OverlaySettings
        {
            EnableRemoteControl = true,
            RemoteControlAllowLan = false,
            RemoteControlPort = port,
            RemoteControlToken = Token
        });
        Assert.True(service.IsRunning, service.LastError);
        return service;
    }
}
