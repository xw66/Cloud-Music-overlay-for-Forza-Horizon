using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcPollingPolicyTests
{
    [Fact]
    public void ShouldSample_returns_true_for_first_sample()
    {
        Assert.True(SmtcPollingPolicy.ShouldSample(100, 0, 800));
    }

    [Fact]
    public void ShouldSample_returns_false_before_interval()
    {
        Assert.False(SmtcPollingPolicy.ShouldSample(999, 200, 800));
    }

    [Fact]
    public void ShouldSample_returns_true_at_interval()
    {
        Assert.True(SmtcPollingPolicy.ShouldSample(1000, 200, 800));
    }
}
