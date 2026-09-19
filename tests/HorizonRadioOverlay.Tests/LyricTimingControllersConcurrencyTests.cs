using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class LyricTimingControllersConcurrencyTests
{
    [Fact]
    public async Task SmtcLyricTimingController_handles_concurrent_updates_and_reads_safely()
    {
        SmtcLyricTimingController controller = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1));
        List<Task> tasks = new();

        // 模拟后台高频推进 SMTC 时钟
        tasks.Add(Task.Run(() =>
        {
            double pos = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                controller.Update(pos, isPlaying: true);
                pos = (pos + 0.1) % 200.0;
                Thread.Yield();
            }
        }));

        // 模拟前台/后台高频读取 displayPosition
        for (int i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    _ = controller.GetCurrentDisplayPositionSeconds();
                    _ = controller.GetCurrentRawPositionSeconds();
                    _ = controller.CurrentCompensationSeconds;
                    Thread.Yield();
                }
            }));
        }

        // 模拟前台切歌时的 Reset 和 Delay 覆盖
        tasks.Add(Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                controller.Reset();
                controller.SetDelayOverrideMilliseconds(50);
                controller.SetDelayOverrideMilliseconds(null);
                Thread.Yield();
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task NeteaseLyricTimingController_handles_concurrent_updates_and_reads_safely()
    {
        NeteaseLyricTimingController controller = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1));
        List<Task> tasks = new();

        // 模拟播放器更新
        tasks.Add(Task.Run(() =>
        {
            double pos = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                controller.UpdateFromPlayer(pos, isPlaying: true);
                pos = (pos + 0.2) % 200.0;
                Thread.Yield();
            }
        }));

        // 模拟高频位置读取
        for (int i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    _ = controller.GetCurrentPositionSeconds();
                    _ = controller.HasState;
                    _ = controller.HasPlayerState;
                    Thread.Yield();
                }
            }));
        }

        // 模拟切歌时的 Reset 和 Start
        tasks.Add(Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                controller.Reset();
                controller.Start();
                Thread.Yield();
            }
        }));

        await Task.WhenAll(tasks);
    }
}
