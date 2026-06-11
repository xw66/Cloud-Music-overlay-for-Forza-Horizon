namespace HorizonRadioOverlay.Services;

public static class NeteasePlaybackProbePolicy
{
    private const double PlayingBiasSeconds = 0.25;

    public static bool IsNeteaseSession(string? sourceAppId)
    {
        return !string.IsNullOrWhiteSpace(sourceAppId) &&
               sourceAppId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMatchingTrack(
        string expectedTitle,
        string expectedArtist,
        string actualTitle,
        string actualArtist)
    {
        if (string.IsNullOrWhiteSpace(actualTitle))
        {
            return false;
        }

        double score = NeteaseOfficialResolver.ScoreCandidate(
            expectedTitle,
            expectedArtist,
            actualTitle,
            actualArtist);
        return score >= 55;
    }

    public static bool HasUsableTimeline(double positionSeconds, double endSeconds)
    {
        return endSeconds > 0;
    }

    public static NeteasePlaybackSample SelectBestSample(
        IReadOnlyList<NeteasePlaybackSample> samples,
        double predictedPositionSeconds)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        if (samples.Count == 1)
        {
            return samples[0];
        }

        return samples
            .OrderBy(sample => sample.IsPlaying ? 0 : 1)
            .ThenBy(sample => Math.Abs(sample.PositionSeconds - predictedPositionSeconds) -
                              (sample.IsPlaying ? PlayingBiasSeconds : 0))
            .ThenByDescending(sample => sample.PositionSeconds)
            .First();
    }
}
