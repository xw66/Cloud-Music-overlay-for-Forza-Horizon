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
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]fallback line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("fallback line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_tries_next_candidate_when_first_lyrics_is_empty()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111", "222" }),
            fetchLyricPayloadAsync: songId => Task.FromResult(songId == "111"
                ? "{}"
                : """
{
  "lrc": {
    "lyric": "[00:00.00]second line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("second line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_continues_when_candidate_fetch_throws()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111", "222" }),
            fetchLyricPayloadAsync: songId => songId == "111"
                ? Task.FromException<string>(new HttpRequestException("timeout"))
                : Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]second line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("second line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_uses_basic_search_for_smtc_style_lookup()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]fallback line"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("fallback line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_supports_yrc_when_plain_lrc_is_missing()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "yrc": {
    "lyric": "[0,2000](0,500,0)Hello(500,500,0) world"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("Hello world", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_uses_preferred_song_id_before_fuzzy_search()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => throw new InvalidOperationException("should not search"),
            fetchLyricPayloadAsync: songId => Task.FromResult(songId == "247821"
                ? """
{
  "lrc": {
    "lyric": "[00:00.00]preferred line"
  }
}
"""
                : "{}"));

        await service.FetchLyricsAsync("阳光下的星星", "金海心", null, 0, preferredSongId: "247821");

        Assert.True(service.HasLyrics);
        Assert.Equal("preferred line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_retries_same_track_after_previous_miss()
    {
        long now = 0;
        int fetchCount = 0;

        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
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

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);
        Assert.False(service.HasLyrics);

        now = 2000;
        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("retried line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_ignores_stale_result_from_previous_track()
    {
        TaskCompletionSource<string> slowSongPayload = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using LyricsService service = new(
            searchSongIdsAsync: (songName, _, _, _) => Task.FromResult<IReadOnlyList<string>>(
                new[] { songName == "Song A" ? "111" : "222" }),
            fetchLyricPayloadAsync: songId => songId switch
            {
                "111" => slowSongPayload.Task,
                "222" => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]song b line"
  }
}
"""),
                _ => Task.FromResult("{}")
            });

        Task slowFetch = service.FetchLyricsAsync("Song A", "Artist", null, 0);
        await service.FetchLyricsAsync("Song B", "Artist", null, 0);

        Assert.True(service.HasLyrics);
        Assert.Equal("song b line", service.GetCurrentLine());

        slowSongPayload.SetResult("""
{
  "lrc": {
    "lyric": "[00:00.00]song a line"
  }
}
""");
        await slowFetch;

        Assert.True(service.HasLyrics);
        Assert.Equal("song b line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_refetches_when_album_version_changes()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, albumTitle, _) => Task.FromResult<IReadOnlyList<string>>(
                new[] { albumTitle == "Album A" ? "111" : "222" }),
            fetchLyricPayloadAsync: songId => Task.FromResult(songId == "111"
                ? """
{
  "lrc": {
    "lyric": "[00:00.00]album a line"
  }
}
"""
                : """
{
  "lrc": {
    "lyric": "[00:00.00]album b line"
  }
}
"""));

        await service.FetchLyricsAsync("Same Song", "Same Artist", "Album A", 0, 240);
        Assert.Equal("album a line", service.GetCurrentLine());

        await service.FetchLyricsAsync("Same Song", "Same Artist", "Album B", 0, 260);
        Assert.Equal("album b line", service.GetCurrentLine());
    }

    [Fact]
    public async Task FetchLyricsAsync_reuses_cached_lyrics_for_same_version()
    {
        int fetchCount = 0;

        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "111" }),
            fetchLyricPayloadAsync: _ =>
            {
                fetchCount++;
                return Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]cached line"
  }
}
""");
            });

        await service.FetchLyricsAsync("Same Song", "Same Artist", "Album A", 0, 240);
        service.Reset();
        await service.FetchLyricsAsync("Same Song", "Same Artist", "Album A", 10, 240);

        Assert.Equal(1, fetchCount);
        Assert.Equal("cached line", service.GetCurrentLine());
    }

    [Fact]
    public async Task UpdateCurrentLine_uses_explicit_playback_position_for_lrc_matching()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]line one\n[00:05.00]line two"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        service.SetPlaybackPosition(1);
        Assert.Equal("line one", service.UpdateCurrentLine());

        service.SetPlaybackPosition(6);
        Assert.Equal("line two", service.UpdateCurrentLine());
    }

    [Fact]
    public async Task UpdateCurrentLine_applies_lrc_offset_metadata()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[offset:2000]\n[00:00.00]line one\n[00:05.00]line two"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        service.SetPlaybackPosition(1.0);
        Assert.Null(service.UpdateCurrentLine());

        service.SetPlaybackPosition(2.1);
        Assert.Equal("line one", service.UpdateCurrentLine());
    }

    [Fact]
    public async Task UpdateCurrentLine_does_not_jump_back_near_line_boundary()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "12345" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
{
  "lrc": {
    "lyric": "[00:00.00]line one\n[00:05.00]line two"
  }
}
"""));

        await service.FetchLyricsAsync("Some Song", "Some Artist", null, 0);

        service.SetPlaybackPosition(5.2);
        Assert.Equal("line two", service.UpdateCurrentLine());

        service.SetPlaybackPosition(5.0);
        Assert.Null(service.UpdateCurrentLine());
        Assert.Equal("line two", service.GetCurrentLine());
    }

    [Fact]
    public void BuildSearchQueries_removes_smtc_noise_and_keeps_primary_artist_query()
    {
        IReadOnlyList<string> queries = LyricsService.BuildSearchQueries("Some Song (Live) VIP", "Artist A / Artist B", "Album X");

        Assert.Contains("Some Song Artist A / Artist B", queries);
        Assert.Contains("Some Song (Live) VIP", queries);
        Assert.Contains("Some Song", queries);
        Assert.Contains("Some Song Album X Artist A / Artist B", queries);
    }

    [Fact]
    public void BuildSearchQueries_normalizes_smart_punctuation_and_feat_artist()
    {
        IReadOnlyList<string> queries = LyricsService.BuildSearchQueries("Don\u2019t Say \u201cGoodbye\u201d", "Artist A feat. Artist B");

        Assert.Contains("Don\u2019t Say \u201cGoodbye\u201d", queries);
        Assert.Contains("Don't Say \"Goodbye\" Artist A Artist B", queries);
    }

    [Fact]
    public void RankSongIdsFromSearchPayload_prefers_exact_artist_match_over_noisy_candidate()
    {
        string json = """
{
  "result": {
    "songs": [
      {
        "id": 3371105847,
        "name": "多远都要在一起",
        "artists": [
          { "name": "G.E.M. 邓紫棋" },
          { "name": "MissGoog" }
        ]
      },
      {
        "id": 30612793,
        "name": "多远都要在一起",
        "artists": [
          { "name": "G.E.M.邓紫棋" }
        ]
      }
    ]
  }
}
""";

        IReadOnlyList<string> ranked = LyricsService.RankSongIdsFromSearchPayload(
            json,
            "多远都要在一起",
            "G.E.M. 邓紫棋",
            null,
            0);

        Assert.Equal(new[] { "30612793", "3371105847" }, ranked);
    }

    [Fact]
    public void RankSongIdsFromSearchPayload_prefers_duration_closer_candidate_when_title_artist_tie()
    {
        string json = """
{
  "result": {
    "songs": [
      {
        "id": 111,
        "name": "句号",
        "duration": 281000,
        "artists": [
          { "name": "G.E.M. 邓紫棋" }
        ]
      },
      {
        "id": 222,
        "name": "句号",
        "duration": 261000,
        "artists": [
          { "name": "G.E.M. 邓紫棋" }
        ]
      }
    ]
  }
}
""";

        IReadOnlyList<string> ranked = LyricsService.RankSongIdsFromSearchPayload(
            json,
            "句号",
            "G.E.M. 邓紫棋",
            null,
            262);

        Assert.Equal(new[] { "222", "111" }, ranked);
    }

    [Fact]
    public void RankSongIdsFromSearchPayload_rejects_low_confidence_candidates()
    {
        string json = """
{
  "result": {
    "songs": [
      {
        "id": 111,
        "name": "Completely Different",
        "duration": 180000,
        "artists": [
          { "name": "Another Artist" }
        ]
      }
    ]
  }
}
""";

        IReadOnlyList<string> ranked = LyricsService.RankSongIdsFromSearchPayload(
            json,
            "句号",
            "G.E.M. 邓紫棋",
            "摩天动物园",
            262);

        Assert.Empty(ranked);
    }

    [Fact]
    public void RankSongIdsFromSearchPayload_rejects_ambiguous_top_candidates()
    {
        string json = """
{
  "result": {
    "songs": [
      {
        "id": 111,
        "name": "句号",
        "duration": 262000,
        "album": { "name": "新的心跳" },
        "artists": [
          { "name": "G.E.M. 邓紫棋" }
        ]
      },
      {
        "id": 222,
        "name": "句号",
        "duration": 262000,
        "album": { "name": "摩天动物园" },
        "artists": [
          { "name": "G.E.M. 邓紫棋" }
        ]
      }
    ]
  }
}
""";

        IReadOnlyList<string> ranked = LyricsService.RankSongIdsFromSearchPayload(
            json,
            "句号",
            "G.E.M. 邓紫棋",
            null,
            262);

        Assert.Empty(ranked);
    }

    [Fact]
    public void ExtractBestLyricFromPayload_reads_plain_lrc()
    {
        string lyric = LyricsService.ExtractBestLyricFromPayload("""
{
  "lrc": {
    "lyric": "[00:00.00]fallback line"
  }
}
""");

        Assert.Equal("[00:00.00]fallback line", lyric);
    }

    [Fact]
    public void ShouldSkipLookup_returns_true_for_placeholder_metadata()
    {
        Assert.True(LyricsService.ShouldSkipLookup("正在连接…", "Unknown Artist"));
        Assert.True(LyricsService.ShouldSkipLookup("未检测到系统媒体会话", "Unknown Artist"));
        Assert.False(LyricsService.ShouldSkipLookup("句号", "G.E.M. 邓紫棋"));
    }
}
