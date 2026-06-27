namespace HorizonRadioOverlay.Services;

public static class UiRefreshPolicy
{
    public static bool ShouldRefreshTrackMetadata(bool trackChanged, bool displayChanged, bool showOverlay)
    {
        return trackChanged || displayChanged || showOverlay;
    }
}
