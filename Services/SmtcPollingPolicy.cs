namespace HorizonRadioOverlay.Services;

public static class SmtcPollingPolicy
{
    public const int TimelineSampleIntervalMs = 800;
    public const int TrackRefreshIntervalMs = 800;

    public static bool ShouldSample(long now, long lastSampleAt, int intervalMs)
    {
        return lastSampleAt == 0 || now - lastSampleAt >= intervalMs;
    }
}
