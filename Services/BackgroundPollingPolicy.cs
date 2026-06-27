namespace HorizonRadioOverlay.Services;

public static class BackgroundPollingPolicy
{
    public const int FastPollMs = 200;
    public const int WarmPollMs = 800;
    public const int IdlePollMs = 2000;

    public static int GetPollIntervalMs(bool enableLyrics, bool isMainWindowVisible, bool isOverlayVisible, bool isBoosted)
    {
        if (isBoosted)
        {
            return FastPollMs;
        }

        if (!isMainWindowVisible && !isOverlayVisible)
        {
            return IdlePollMs;
        }

        return enableLyrics ? FastPollMs : WarmPollMs;
    }
}
