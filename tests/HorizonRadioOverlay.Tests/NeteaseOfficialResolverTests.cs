using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.Models;
using System.Net;

namespace HorizonRadioOverlay.Tests;

public class NeteaseOfficialResolverTests
{
    [Fact]
    public void NormalizeTitle_RemovesCommonNoise()
    {
        string normalized = NeteaseOfficialResolver.NormalizeTitle("\u5927\u7720 (Live\u7248)");

        Assert.Equal("\u5927\u7720", normalized);
    }

    [Fact]
    public void NormalizeArtistToken_SupportsChineseSeparators()
    {
        string normalized = NeteaseOfficialResolver.NormalizeArtistToken("\u6b4c\u624bA\u3001\u6b4c\u624bB\uFF0C\u6b4c\u624bC");

        Assert.Equal("\u6b4c\u624ba|\u6b4c\u624bb|\u6b4c\u624bc", normalized);
    }

    [Fact]
    public void ScoreCandidate_PrefersExactTitleAndArtist()
    {
        double exact = NeteaseOfficialResolver.ScoreCandidate("\u5927\u7720", "\u738b\u5fc3\u51cc", "\u5927\u7720", "\u738b\u5fc3\u51cc");
        double mismatch = NeteaseOfficialResolver.ScoreCandidate("\u5927\u7720", "\u738b\u5fc3\u51cc", "\u5927\u7720 (Live)", "\u522b\u7684\u6b4c\u624b");

        Assert.True(exact > mismatch);
        Assert.True(exact >= 80);
    }

    [Fact]
    public void BuildTrackCacheKey_NormalizesWhitespaceAndArtistOrder()
    {
        string left = NeteaseOfficialResolver.BuildTrackCacheKey(" Thank U, Next ", "Ariana Grande");
        string right = NeteaseOfficialResolver.BuildTrackCacheKey("Thank U, Next", "Ariana Grande");

        Assert.Equal(left, right);
    }

    [Fact]
    public void NormalizeTitle_RemovesQqMusicBadges()
    {
        string normalized = NeteaseOfficialResolver.NormalizeTitle("\u53e5\u53f7 VIP \u81fb\u54c1\u6bcd\u5e26");

        Assert.Equal("\u53e5\u53f7", normalized);
    }

    [Fact]
    public void SplitArtists_SupportsFeatAndCrossSeparators()
    {
        string[] artists = NeteaseOfficialResolver.SplitArtists("Artist A feat. Artist B x Artist C");

        Assert.Equal(new[] { "artista", "artistb", "artistc" }, artists);
    }

    [Fact]
    public void SplitArtists_IgnoresInternalWhitespaceWithinArtistToken()
    {
        string[] artists = NeteaseOfficialResolver.SplitArtists("G.E.M. 邓紫棋 / MissGoog");

        Assert.Equal(new[] { "g.e.m.邓紫棋", "missgoog" }, artists);
    }

    [Fact]
    public void ScoreCandidate_PrefersCanonicalArtistOverExtraFeaturingCandidate()
    {
        double canonical = NeteaseOfficialResolver.ScoreCandidate("多远都要在一起", "G.E.M. 邓紫棋", "多远都要在一起", "G.E.M.邓紫棋");
        double noisy = NeteaseOfficialResolver.ScoreCandidate("多远都要在一起", "G.E.M. 邓紫棋", "多远都要在一起", "G.E.M. 邓紫棋 / MissGoog");

        Assert.True(canonical >= noisy);
    }

    [Fact]
    public void ShouldAcceptPreferredIdResult_ReturnsFalse_WhenCandidateClearlyMismatches()
    {
        ResolvedSong candidate = new()
        {
            SongId = "401722144",
            Title = "1954",
            Artist = "Michaela May",
            Confidence = 100,
            ResolveSource = "netease-id"
        };

        Assert.False(NeteaseOfficialResolver.ShouldAcceptPreferredIdResult("多远都要在一起", "G.E.M. 邓紫棋", candidate));
    }

    [Fact]
    public void ShouldAcceptPreferredIdResult_ReturnsTrue_WhenCandidateMatches()
    {
        ResolvedSong candidate = new()
        {
            SongId = "30612793",
            Title = "多远都要在一起",
            Artist = "G.E.M. 邓紫棋",
            Confidence = 100,
            ResolveSource = "netease-id"
        };

        Assert.True(NeteaseOfficialResolver.ShouldAcceptPreferredIdResult("多远都要在一起", "G.E.M. 邓紫棋", candidate));
    }
    [Fact]
    public async Task ResolveAsync_FillsCoverUrlFromSongDetail_WhenSearchResultHasNoCover()
    {
        FakeHttpMessageHandler handler = new(request =>
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/api/search/get", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "result": {
                        "songs": [
                          {
                            "id": 93213,
                            "name": "\u7ea2\u989c",
                            "artists": [{ "name": "\u80e1\u5f66\u658c" }],
                            "album": { "id": 9012 },
                            "duration": 230000
                          }
                        ]
                      }
                    }
                    """)
                };
            }

            if (url.Contains("/api/song/detail", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "songs": [
                        {
                          "id": 93213,
                          "name": "\u7ea2\u989c",
                          "ar": [{ "name": "\u80e1\u5f66\u658c" }],
                          "al": {
                            "id": 9012,
                            "picUrl": "https://p1.music.126.net/cover.jpg"
                          },
                          "dt": 230000
                        }
                      ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        NeteaseOfficialResolver resolver = new(new DiagnosticService(), new HttpClient(handler));

        ResolvedSong? resolved = await resolver.ResolveAsync("\u7ea2\u989c", "\u80e1\u5f66\u658c", traceId: "TEST0001");

        Assert.NotNull(resolved);
        Assert.Equal("93213", resolved.SongId);
        Assert.Equal("https://p1.music.126.net/cover.jpg", resolved.CoverUrl);
    }

    [Fact]
    public async Task ResolveAsync_RetriesDetail_WhenPreviousDetailRequestDidNotReturnCover()
    {
        int detailRequests = 0;
        FakeHttpMessageHandler handler = new(request =>
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/api/search/get", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {"result":{"songs":[{"id":93213,"name":"\u7ea2\u989c","artists":[{"name":"\u80e1\u5f66\u658c"}],"album":{"id":9012},"duration":230000}]}}
                    """)
                };
            }

            detailRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(detailRequests == 1
                    ? """{"songs":[{"id":93213,"name":"\u7ea2\u989c","ar":[{"name":"\u80e1\u5f66\u658c"}],"al":{"id":9012},"dt":230000}]}"""
                    : """{"songs":[{"id":93213,"name":"\u7ea2\u989c","ar":[{"name":"\u80e1\u5f66\u658c"}],"al":{"id":9012,"picUrl":"https://p1.music.126.net/recovered.jpg"},"dt":230000}]}""")
            };
        });

        NeteaseOfficialResolver resolver = new(new DiagnosticService(), new HttpClient(handler));

        ResolvedSong? first = await resolver.ResolveAsync("\u7ea2\u989c", "\u80e1\u5f66\u658c", traceId: "TEST0002");
        ResolvedSong? second = await resolver.ResolveAsync("\u7ea2\u989c", "\u80e1\u5f66\u658c", traceId: "TEST0003");

        Assert.Null(first?.CoverUrl);
        Assert.Equal("https://p1.music.126.net/recovered.jpg", second?.CoverUrl);
        Assert.Equal(2, detailRequests);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
