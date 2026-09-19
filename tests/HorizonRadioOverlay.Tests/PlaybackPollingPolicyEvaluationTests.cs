using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class PlaybackPollingPolicyEvaluationTests
{
    [Theory]
    [InlineData(true, true, true, false, BackgroundPollingPolicy.FastPollMs)]
    [InlineData(true, false, false, false, BackgroundPollingPolicy.IdlePollMs)]
    [InlineData(false, false, false, false, BackgroundPollingPolicy.IdlePollMs)]
    [InlineData(false, true, false, false, BackgroundPollingPolicy.WarmPollMs)]
    [InlineData(false, false, true, false, BackgroundPollingPolicy.WarmPollMs)]
    [InlineData(false, false, false, true, BackgroundPollingPolicy.FastPollMs)]
    [InlineData(false, true, true, true, BackgroundPollingPolicy.FastPollMs)]
    public void PollingInterval_accurately_adapts_to_ui_and_boost_states(
        bool enableLyrics,
        bool isMainWindowVisible,
        bool isOverlayVisible,
        bool isBoosted,
        int expectedIntervalMs)
    {
        int interval = BackgroundPollingPolicy.GetPollIntervalMs(
            enableLyrics,
            isMainWindowVisible,
            isOverlayVisible,
            isBoosted);

        Assert.Equal(expectedIntervalMs, interval);
    }

    [Fact]
    public void PollingInterval_transitions_to_fast_when_boosted_even_in_background()
    {
        int backgroundIdle = BackgroundPollingPolicy.GetPollIntervalMs(
            enableLyrics: false,
            isMainWindowVisible: false,
            isOverlayVisible: false,
            isBoosted: false);

        int backgroundBoosted = BackgroundPollingPolicy.GetPollIntervalMs(
            enableLyrics: false,
            isMainWindowVisible: false,
            isOverlayVisible: false,
            isBoosted: true);

        Assert.Equal(BackgroundPollingPolicy.IdlePollMs, backgroundIdle);
        Assert.Equal(BackgroundPollingPolicy.FastPollMs, backgroundBoosted);
    }
}
