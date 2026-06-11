using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcLyricTimingControllerTests
{
    [Fact]
    public void Update_uses_default_compensation_when_no_override()
    {
        DateTime start = new(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
        SmtcLyricTimingController controller = new();

        SmtcLyricTimingSample sample = controller.Update(12.0, isPlaying: true, start);

        Assert.InRange(sample.DisplayPositionSeconds, 12.49, 12.51);
        Assert.InRange(sample.CompensationSeconds, -0.51, -0.49);
    }

    [Fact]
    public void Update_prefers_override_compensation_when_configured()
    {
        DateTime start = new(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
        SmtcLyricTimingController controller = new();
        controller.SetDelayOverrideMilliseconds(1800);

        SmtcLyricTimingSample sample = controller.Update(12.0, isPlaying: true, start);

        Assert.InRange(sample.DisplayPositionSeconds, 10.19, 10.21);
        Assert.InRange(sample.CompensationSeconds, 1.79, 1.81);
    }

    [Fact]
    public void Update_smoothly_increases_compensation_when_smtc_consistently_lags_prediction()
    {
        DateTime start = new(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
        SmtcLyricTimingController controller = new();

        controller.Update(10.0, isPlaying: true, start);
        controller.Update(10.7, isPlaying: true, start.AddSeconds(1));
        controller.Update(11.4, isPlaying: true, start.AddSeconds(2));
        controller.Update(12.1, isPlaying: true, start.AddSeconds(3));
        SmtcLyricTimingSample sample = controller.Update(12.8, isPlaying: true, start.AddSeconds(4));

        Assert.True(sample.CompensationSeconds > SmtcLyricsSyncPolicy.DisplayDelayCompensationSeconds);
        Assert.True(sample.CompensationSeconds < -0.1);
    }

    [Fact]
    public void Reset_clears_adaptive_state_back_to_default_compensation()
    {
        DateTime start = new(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
        SmtcLyricTimingController controller = new();

        controller.Update(10.0, isPlaying: true, start);
        controller.Update(10.7, isPlaying: true, start.AddSeconds(1));
        controller.Reset();

        SmtcLyricTimingSample sample = controller.Update(12.0, isPlaying: true, start.AddSeconds(5));

        Assert.InRange(sample.CompensationSeconds, -0.51, -0.49);
    }
}
