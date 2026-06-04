namespace HorizonRadioOverlay.Services;

public static class SmtcReadPolicy
{
    public const int MaxTrackReadAttempts = 3;

    public static string NormalizeTitle(string? title)
    {
        return title?.Trim() ?? string.Empty;
    }

    public static bool ShouldRetryMissingTrackMetadata(int attempt, string normalizedTitle)
    {
        return attempt < MaxTrackReadAttempts && string.IsNullOrWhiteSpace(normalizedTitle);
    }

    public static int GetRetryDelayMilliseconds(int attempt)
    {
        return 80 * attempt;
    }

    public static string FormatSourceAppId(string? sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return "SMTC";
        }

        return $"SMTC({sourceAppId.Trim()})";
    }

    public static string GetCoverSource(bool hasThumbnail)
    {
        return hasThumbnail ? "smtc-thumbnail" : "smtc-no-thumbnail";
    }
}
