namespace HorizonRadioOverlay.Services;

public static class SmtcLyricsSyncPolicy
{
    public const double SeekThresholdSeconds = 2.0;
    public const double DisplayDelayCompensationSeconds = -0.5;

    public static bool ShouldUseExternalLyrics(bool enableLyrics, bool useSmtc)
    {
        return enableLyrics && useSmtc;
    }

    public static bool ShouldUseFastPolling(bool enableLyrics, bool useSmtc)
    {
        return enableLyrics && useSmtc;
    }

    public static bool ShouldPrioritizeTimelineSync(bool enableLyrics, bool useSmtc)
    {
        return enableLyrics && useSmtc;
    }

    public static double GetInitialPlaybackPositionSeconds(double? playbackPositionSeconds)
    {
        return playbackPositionSeconds is > 0 ? playbackPositionSeconds.Value : 0;
    }

    public static double GetDisplayPlaybackPositionSeconds(double playbackPositionSeconds)
    {
        return Math.Max(0, playbackPositionSeconds - DisplayDelayCompensationSeconds);
    }

    public static bool ShouldResyncFromSmtc(double smtcPositionSeconds, double wallClockPositionSeconds)
    {
        return Math.Abs(smtcPositionSeconds - wallClockPositionSeconds) > SeekThresholdSeconds;
    }
}
