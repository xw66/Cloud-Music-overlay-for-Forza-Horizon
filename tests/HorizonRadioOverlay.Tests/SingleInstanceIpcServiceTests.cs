using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SingleInstanceIpcServiceTests
{
    [Fact]
    public async Task NotifyExistingInstanceAsync_returns_false_when_no_server_running()
    {
        string uniquePipe = $"TestPipe_NoServer_{Guid.NewGuid():N}";
        bool notified = await SingleInstanceIpcService.NotifyExistingInstanceAsync("HELLO", uniquePipe, timeoutMs: 200);

        Assert.False(notified);
    }

    [Fact]
    public async Task Server_receives_message_from_client()
    {
        string uniquePipe = $"TestPipe_Echo_{Guid.NewGuid():N}";
        using var server = new SingleInstanceIpcService(uniquePipe);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += msg => tcs.TrySetResult(msg);
        server.StartServer();

        // Allow pipe server to start listening
        await Task.Delay(50);

        bool sent = await SingleInstanceIpcService.NotifyExistingInstanceAsync("ACTIVATE", uniquePipe, timeoutMs: 2000);
        Assert.True(sent);

        var received = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, received);
        Assert.Equal("ACTIVATE", await tcs.Task);
    }
}
