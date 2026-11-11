using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class LyricsServiceConcurrencyTests
{
    [Fact]
    public async Task Reset_and_refetch_of_same_song_rejects_the_older_response()
    {
        TaskCompletionSource<string> oldPayload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(["123"]),
            fetchLyricPayloadAsync: _ => Interlocked.Increment(ref fetchCount) == 1
                ? oldPayload.Task
                : Task.FromResult("""{"lrc":{"lyric":"[00:00.00]current lyric"}}"""));

        Task oldFetch = service.FetchLyricsAsync("Same song", "Artist", null, 0);
        service.Reset();
        await service.FetchLyricsAsync("Same song", "Artist", null, 0);
        oldPayload.SetResult("""{"lrc":{"lyric":"[00:00.00]stale lyric"}}""");
        await oldFetch;

        Assert.Equal("current lyric", service.GetCurrentLine());
        service.Reset();
        await service.FetchLyricsAsync("Same song", "Artist", null, 0);
        Assert.Equal("current lyric", service.GetCurrentLine());
        Assert.Equal(2, fetchCount);
    }

    [Fact]
    public async Task LyricsService_handles_concurrent_state_updates_and_resets_safely()
    {
        using LyricsService service = new(
            searchSongIdsAsync: (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "123" }),
            fetchLyricPayloadAsync: _ => Task.FromResult("""
            {
              "lrc": {
                "lyric": "[00:00.00]Line 1\n[00:02.00]Line 2\n[00:05.00]Line 3"
              }
            }
            """));

        await service.FetchLyricsAsync("Test Song", "Test Artist", null, 0, 180);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
        List<Task> tasks = new();

        // 模拟后台高频推进播放时钟与行更新
        tasks.Add(Task.Run(async () =>
        {
            double position = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                service.SetPlaybackPosition(position);
                _ = service.UpdateCurrentLine();
                _ = service.GetCurrentLine();
                _ = service.HasLyrics;
                position = (position + 0.1) % 10.0;
                await Task.Yield();
            }
        }));

        // 模拟前台切歌/刷新时的重置
        tasks.Add(Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                service.Reset();
                await Task.Yield();
                await service.FetchLyricsAsync("Test Song", "Test Artist", null, 0, 180);
                await Task.Yield();
            }
        }));

        // 模拟前台多处状态查询
        for (int i = 0; i < 3; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    _ = service.HasLyrics;
                    _ = service.GetCurrentLine();
                    await Task.Yield();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }
}
