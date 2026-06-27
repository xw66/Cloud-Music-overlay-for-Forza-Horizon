using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class BackgroundPollingPolicyTests
{
    [Fact]
    public void GetPollIntervalMs_returns_fast_when_boosted()
    {
        Assert.Equal(BackgroundPollingPolicy.FastPollMs,
            BackgroundPollingPolicy.GetPollIntervalMs(false, false, false, true));
    }

    [Fact]
    public void GetPollIntervalMs_returns_fast_when_visible_and_lyrics_enabled()
    {
        Assert.Equal(BackgroundPollingPolicy.FastPollMs,
            BackgroundPollingPolicy.GetPollIntervalMs(true, true, false, false));
    }

    [Fact]
    public void GetPollIntervalMs_returns_warm_when_visible_without_lyrics()
    {
        Assert.Equal(BackgroundPollingPolicy.WarmPollMs,
            BackgroundPollingPolicy.GetPollIntervalMs(false, true, false, false));
    }

    [Fact]
    public void GetPollIntervalMs_returns_idle_when_hidden()
    {
        Assert.Equal(BackgroundPollingPolicy.IdlePollMs,
            BackgroundPollingPolicy.GetPollIntervalMs(true, false, false, false));
    }
}
