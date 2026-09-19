using System.Net.Http;

namespace HorizonRadioOverlay.Services;

public static class AppHttpClientProvider
{
    public static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        EnableMultipleHttp2Connections = true
    };

    public static HttpClient CreateClient(TimeSpan timeout, string? defaultUserAgent = null)
    {
        HttpClient client = new(SharedHandler, disposeHandler: false)
        {
            Timeout = timeout
        };

        if (!string.IsNullOrWhiteSpace(defaultUserAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(defaultUserAgent);
        }

        return client;
    }
}
