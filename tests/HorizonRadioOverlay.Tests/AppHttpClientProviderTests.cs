using System.Net.Http;
using System.Runtime.Versioning;
using HorizonRadioOverlay.Services;
using Xunit;

namespace HorizonRadioOverlay.Tests;

[SupportedOSPlatform("windows")]
public class AppHttpClientProviderTests
{
    [Fact]
    public void SharedHandler_HasCorrectPooledConnectionPolicy()
    {
        var handler = AppHttpClientProvider.SharedHandler;
        Assert.NotNull(handler);
        Assert.Equal(TimeSpan.FromMinutes(15), handler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromMinutes(2), handler.PooledConnectionIdleTimeout);
        Assert.Equal(20, handler.MaxConnectionsPerServer);
        Assert.True(handler.EnableMultipleHttp2Connections);
    }

    [Fact]
    public void CreateClient_ConfiguresTimeoutAndUserAgentCorrectly()
    {
        using HttpClient client = AppHttpClientProvider.CreateClient(
            TimeSpan.FromSeconds(8),
            "CustomTestAgent/2.0");

        Assert.Equal(TimeSpan.FromSeconds(8), client.Timeout);
        Assert.Contains(client.DefaultRequestHeaders.UserAgent, p => p.Product?.Name == "CustomTestAgent");
    }

    [Fact]
    public void UpdateService_AcceptsCustomHttpClient_AndPreservesExternalClientOnDispose()
    {
        using HttpClient customClient = new();
        UpdateService updateService = new(customClient, disposeClient: false);

        // 调用 Dispose 不应销毁外部注入的 HttpClient
        updateService.Dispose();

        // 验证 customClient 依然处于可用状态（未被 ObjectDisposedException 标记）
        Assert.Equal(TimeSpan.FromSeconds(100), customClient.Timeout);
    }

    [Fact]
    public void LyricsService_AcceptsCustomHttpClient()
    {
        using HttpClient customClient = new();
        DiagnosticService diagnostic = new();
        LyricsService lyricsService = new(diagnostic, customClient);

        Assert.NotNull(lyricsService);
        Assert.False(lyricsService.HasLyrics);
    }

    [Fact]
    public void NeteaseLocalDataService_AcceptsCustomHttpClient()
    {
        using HttpClient customClient = new();
        CoverCacheService coverCache = new();
        DiagnosticService diagnostic = new();
        NeteaseLocalDataService localDataService = new(coverCache, diagnostic, null, customClient);

        Assert.NotNull(localDataService);
    }

    [Fact]
    public void NeteaseOfficialResolver_AcceptsCustomHttpClient()
    {
        using HttpClient customClient = new();
        DiagnosticService diagnostic = new();
        NeteaseOfficialResolver resolver = new(diagnostic, customClient);

        Assert.NotNull(resolver);
    }
}
