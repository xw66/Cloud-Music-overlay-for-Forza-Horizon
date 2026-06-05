using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class LyricsServiceTests
{
    [Fact]
    public void Reset_clears_cached_state()
    {
        using LyricsService service = new(new DiagnosticService());

        service.SetPlaybackPosition(42);
        service.Reset();

        Assert.False(service.HasLyrics);
        Assert.Null(service.GetCurrentLine());
        Assert.Null(service.UpdateCurrentLine());
    }

    [Fact]
    public void Dispose_clears_cached_state()
    {
        LyricsService service = new(new DiagnosticService());

        service.SetPlaybackPosition(18);
        service.Dispose();

        Assert.False(service.HasLyrics);
        Assert.Null(service.GetCurrentLine());
        Assert.Null(service.UpdateCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_falls_back_to_basic_search_when_resolver_rejects_match()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]fallback line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("fallback line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_tries_next_candidate_when_first_lyrics_is_empty()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111", "222" }),
            fetchLyricPayloadAsync: songId => Task.FromResult(songId == "111"
                ? "{}"
                : """
{
  "lrc": {
    "lyric": "[00:00.00]second line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("second line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_continues_when_candidate_fetch_throws()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111", "222" }),
            fetchLyricPayloadAsync: songId => songId == "111"
                ? Task.FromException<string>(new HttpRequestException("timeout"))
                : Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]second line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("second line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_uses_basic_search_for_smtc_style_lookup()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]fallback line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("fallback line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_supports_yrc_when_plain_lrc_is_missing()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "yrc": {
    "lyric": "[0,2000](0,500,0)Hello(500,500,0) world"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("Hello world", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_retries_same_track_after_previous_miss()
    {
        long now = 0;
        int fetchCount = 0;

        using LyricsService service = new(
            searchSongIdsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ =>
            {
                fetchCount++;
                return Task.FromResult(fetchCount == 1
                    ? "{}"
                    : """
{
  "lrc": {
    "lyric": "[00:00.00]retried line"
  }
}
""");
            },
            nowProvider: () => now);

        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);
        Assert.False(service.HasLyrics);

        now = 2000;
        await service.FetchLyricsAsync("Some Song", "Some Artist", 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("retried line", service.GetCurrentLine());
    }

    [Fact]
    public void BuildSearchQueries_removes_smtc_noise_and_keeps_primary_artist_query()
    {
        IReadOnlyList<string> queries = LyricsService.BuildSearchQueries("Some Song (Live) VIP", "Artist A / Artist B");

        Assert.Contains("Some Song Artist A / Artist B", queries);
        Assert.Contains("Some Song (Live) VIP", queries);
        Assert.Contains("Some Song", queries);
    }

    [Fact]
    public void BuildSearchQueries_normalizes_smart_punctuation_and_feat_artist()
    {
        IReadOnlyList<string> queries = LyricsService.BuildSearchQueries("Don\u2019t Say \u201cGoodbye\u201d", "Artist A feat. Artist B");

        Assert.Contains("Don’t Say “Goodbye”", queries);
        Assert.Contains("Don't Say \"Goodbye\" Artist A Artist B", queries);
    }
}
