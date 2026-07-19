using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class NeteaseLyricTimingControllerTests
{
    [Fact]
    public void Start_uses_wall_clock_when_no_player_sample_is_available()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();

        controller.Start(start);

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(3)), 2.99, 3.01);
    }

    [Fact]
    public void UpdateFromPlayer_anchors_and_freezes_when_player_is_paused()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(12, isPlaying: false, start.AddSeconds(1));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(5)), 11.99, 12.01);
    }

    [Fact]
    public void UpdateFromPlayer_freezes_immediately_when_paused_position_is_stale()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);
        controller.UpdateFromPlayer(120, isPlaying: true, start.AddSeconds(1));

        controller.UpdateFromPlayer(20, isPlaying: false, start.AddSeconds(3));

        Assert.InRange(
            controller.GetCurrentPositionSeconds(start.AddSeconds(10)),
            121.99,
            122.01);
    }

    [Fact]
    public void UpdateFromPlayer_anchors_and_advances_when_player_is_playing()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(45, isPlaying: true, start.AddSeconds(2));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(3)), 45.99, 46.01);
    }

    [Fact]
    public void Suspend_freezes_wall_clock_when_player_timeline_is_unavailable()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);
        controller.UpdateFromPlayer(30, isPlaying: true, start.AddSeconds(1));

        controller.Suspend(start.AddSeconds(2));

        Assert.InRange(
            controller.GetCurrentPositionSeconds(start.AddSeconds(8)),
            30.99,
            31.01);
    }

    [Fact]
    public void Suspend_keeps_wall_clock_running_before_first_player_sample()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.Suspend(start.AddSeconds(1));

        Assert.False(controller.HasPlayerState);
        Assert.InRange(
            controller.GetCurrentPositionSeconds(start.AddSeconds(8)),
            7.99,
            8.01);
    }

    [Fact]
    public void UpdateFromPlayer_overrides_the_previous_player_position()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(10.0, isPlaying: true, start.AddSeconds(2));
        controller.UpdateFromPlayer(28.0, isPlaying: false, start.AddSeconds(5));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(6)), 27.99, 28.01);
    }

    [Fact]
    public void UpdateFromPlayer_ignores_invalid_position()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(double.NaN, isPlaying: false, start.AddSeconds(1));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(3)), 2.99, 3.01);
    }

    [Fact]
    public void UpdateFromPlayer_rejects_temporary_jump_back_after_forward_seek()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(220.0, isPlaying: true, start.AddSeconds(1));
        controller.UpdateFromPlayer(0.8, isPlaying: true, start.AddSeconds(1.25));
        controller.UpdateFromPlayer(220.5, isPlaying: true, start.AddSeconds(1.5));

        Assert.InRange(
            controller.GetCurrentPositionSeconds(start.AddSeconds(2)),
            220.99,
            221.01);
    }

    [Fact]
    public void UpdateFromPlayer_accepts_consistent_confirmed_backward_seek()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(120.0, isPlaying: true, start.AddSeconds(1));
        controller.UpdateFromPlayer(20.0, isPlaying: true, start.AddSeconds(2));
        controller.UpdateFromPlayer(20.4, isPlaying: true, start.AddSeconds(2.4));
        controller.UpdateFromPlayer(20.8, isPlaying: true, start.AddSeconds(2.8));

        Assert.InRange(
            controller.GetCurrentPositionSeconds(start.AddSeconds(3)),
            20.99,
            21.01);
    }
}
