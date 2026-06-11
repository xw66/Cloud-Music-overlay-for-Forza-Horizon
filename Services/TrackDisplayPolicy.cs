namespace HorizonRadioOverlay.Services;

public static class TrackDisplayPolicy
{
    public static bool ShouldRefreshCoverForSameTrack(
        bool useSmtc,
        bool displayChanged,
        byte[]? previousDisplayedCoverBytes,
        byte[]? currentCoverBytes)
    {
        if (useSmtc || displayChanged)
        {
            return false;
        }

        return HaveCoverBytesChanged(previousDisplayedCoverBytes, currentCoverBytes);
    }

    private static bool HaveCoverBytesChanged(byte[]? previousDisplayedCoverBytes, byte[]? currentCoverBytes)
    {
        bool previousHasCover = previousDisplayedCoverBytes is { Length: > 0 };
        bool currentHasCover = currentCoverBytes is { Length: > 0 };

        if (!currentHasCover)
        {
            return false;
        }

        if (!previousHasCover)
        {
            return true;
        }

        return !previousDisplayedCoverBytes!.AsSpan().SequenceEqual(currentCoverBytes);
    }
}
