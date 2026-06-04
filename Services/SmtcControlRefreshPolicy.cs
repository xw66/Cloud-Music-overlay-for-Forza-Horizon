namespace HorizonRadioOverlay.Services;

public static class SmtcControlRefreshPolicy
{
    public const int MaxRefreshAttempts = 6;

    public static int GetDelayMilliseconds(int attempt)
    {
        return 140 * attempt;
    }

    public static bool ShouldStopWaiting(string previousTrackKey, string currentTrackKey, int attempt)
    {
        if (!string.IsNullOrWhiteSpace(previousTrackKey)
            && !string.IsNullOrWhiteSpace(currentTrackKey)
            && !string.Equals(previousTrackKey, currentTrackKey, StringComparison.Ordinal))
        {
            return true;
        }

        return attempt >= MaxRefreshAttempts;
    }
}
