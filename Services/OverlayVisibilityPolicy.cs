namespace HorizonRadioOverlay.Services;

public static class OverlayVisibilityPolicy
{
    public static bool ShouldHideWhenPlaybackPaused(bool hideWhenPaused, bool isSmtcSource, bool? isPlaying)
    {
        return hideWhenPaused && isSmtcSource && isPlaying == false;
    }

    public static bool ShouldRestoreWhenPlaybackResumed(bool hideWhenPaused, bool isSmtcSource, bool? isPlaying, bool wasHiddenByPause)
    {
        return hideWhenPaused && isSmtcSource && isPlaying == true && wasHiddenByPause;
    }
}
