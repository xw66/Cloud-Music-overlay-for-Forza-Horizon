namespace HorizonRadioOverlay.Services;

public static class SmtcCoverRefreshPolicy
{
    public static bool ShouldDelayImmediateCoverUpdate(bool trackChanged, byte[]? previousDisplayedCoverBytes, byte[]? currentCoverBytes)
    {
        return trackChanged
            && previousDisplayedCoverBytes is { Length: > 0 }
            && currentCoverBytes is { Length: > 0 }
            && previousDisplayedCoverBytes.AsSpan().SequenceEqual(currentCoverBytes);
    }

    public static bool ShouldApplyRetriedCover(
        string expectedTrackKey,
        string candidateTrackKey,
        byte[]? pendingCoverBytes,
        byte[]? candidateCoverBytes,
        int matchingCoverObservationCount)
    {
        if (string.IsNullOrWhiteSpace(expectedTrackKey)
            || !string.Equals(expectedTrackKey, candidateTrackKey, StringComparison.Ordinal)
            || candidateCoverBytes == null
            || candidateCoverBytes.Length == 0)
        {
            return false;
        }

        if (pendingCoverBytes == null || pendingCoverBytes.Length == 0)
        {
            return true;
        }

        if (!pendingCoverBytes.AsSpan().SequenceEqual(candidateCoverBytes))
        {
            return true;
        }

        return matchingCoverObservationCount >= 2;
    }
}
