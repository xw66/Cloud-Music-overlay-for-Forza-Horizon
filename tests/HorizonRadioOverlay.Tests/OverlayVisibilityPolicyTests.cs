using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class OverlayVisibilityPolicyTests
{
    [Fact]
    public void ShouldHideWhenPlaybackPaused_returns_true_only_for_smtc_paused_state()
    {
        Assert.True(OverlayVisibilityPolicy.ShouldHideWhenPlaybackPaused(true, true, false));
        Assert.False(OverlayVisibilityPolicy.ShouldHideWhenPlaybackPaused(true, true, true));
        Assert.False(OverlayVisibilityPolicy.ShouldHideWhenPlaybackPaused(true, false, false));
        Assert.False(OverlayVisibilityPolicy.ShouldHideWhenPlaybackPaused(false, true, false));
        Assert.False(OverlayVisibilityPolicy.ShouldHideWhenPlaybackPaused(true, true, null));
    }

    [Fact]
    public void ShouldRestoreWhenPlaybackResumed_returns_true_only_after_pause_hidden_overlay()
    {
        Assert.True(OverlayVisibilityPolicy.ShouldRestoreWhenPlaybackResumed(true, true, true, true));
        Assert.False(OverlayVisibilityPolicy.ShouldRestoreWhenPlaybackResumed(true, true, false, true));
        Assert.False(OverlayVisibilityPolicy.ShouldRestoreWhenPlaybackResumed(true, true, true, false));
        Assert.False(OverlayVisibilityPolicy.ShouldRestoreWhenPlaybackResumed(true, false, true, true));
        Assert.False(OverlayVisibilityPolicy.ShouldRestoreWhenPlaybackResumed(false, true, true, true));
    }
}
