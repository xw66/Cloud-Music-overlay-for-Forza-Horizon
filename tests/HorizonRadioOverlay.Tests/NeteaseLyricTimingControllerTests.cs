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
    public void UpdateFromPlayer_does_not_freeze_wall_clock_when_external_pause_sample_arrives()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(12, isPlaying: false, start.AddSeconds(1));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(5)), 4.99, 5.01);
    }

    [Fact]
    public void UpdateFromPlayer_does_not_override_wall_clock_timing()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(45, isPlaying: true, start.AddSeconds(2));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(3)), 2.99, 3.01);
    }

    [Fact]
    public void UpdateFromPlayer_ignores_external_pause_sample()
    {
        DateTime start = new(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc);
        NeteaseLyricTimingController controller = new();
        controller.Start(start);

        controller.UpdateFromPlayer(10.0, isPlaying: false, start.AddSeconds(5));

        Assert.InRange(controller.GetCurrentPositionSeconds(start.AddSeconds(6)), 5.99, 6.01);
    }
}
