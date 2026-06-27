using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class UiRefreshPolicyTests
{
    [Fact]
    public void ShouldRefreshTrackMetadata_returns_true_when_track_changed()
    {
        Assert.True(UiRefreshPolicy.ShouldRefreshTrackMetadata(true, false, false));
    }

    [Fact]
    public void ShouldRefreshTrackMetadata_returns_true_when_display_changed()
    {
        Assert.True(UiRefreshPolicy.ShouldRefreshTrackMetadata(false, true, false));
    }

    [Fact]
    public void ShouldRefreshTrackMetadata_returns_true_when_forced_overlay_show()
    {
        Assert.True(UiRefreshPolicy.ShouldRefreshTrackMetadata(false, false, true));
    }

    [Fact]
    public void ShouldRefreshTrackMetadata_returns_false_when_nothing_changed()
    {
        Assert.False(UiRefreshPolicy.ShouldRefreshTrackMetadata(false, false, false));
    }
}
