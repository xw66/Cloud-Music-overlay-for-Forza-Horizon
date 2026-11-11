using System.Net;
using System.Net.Http;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task Update_links_to_release_page_for_architecture_selection()
    {
        using HttpClient client = new(new ReleaseHandler());
        using UpdateService service = new(client);

        var result = await service.CheckForUpdateAsync();

        Assert.True(result.HasUpdate);
        Assert.Empty(result.ErrorMessage);
        Assert.Equal("https://github.com/example/releases/v99.0.0", result.DownloadUrl);
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "tag_name": "v99.0.0",
                      "html_url": "https://github.com/example/releases/v99.0.0",
                      "assets": [{ "name": "app_win-x64.exe", "browser_download_url": "https://example.com/app_win-x64.exe" }]
                    }
                    """)
            });
        }
    }
}
