using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class NeteasePlaybackProbePolicyTests
{
    [Theory]
    [InlineData("CloudMusic", true)]
    [InlineData("cloudmusic.exe", true)]
    [InlineData("NetEase.CloudMusic", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData("", false)]
    public void IsNeteaseSession_only_accepts_cloudmusic_sessions(string sourceAppId, bool expected)
    {
        Assert.Equal(expected, NeteasePlaybackProbePolicy.IsNeteaseSession(sourceAppId));
    }

    [Fact]
    public void IsMatchingTrack_rejects_stale_netease_session_metadata()
    {
        bool result = NeteasePlaybackProbePolicy.IsMatchingTrack(
            expectedTitle: "Big Sleep",
            expectedArtist: "Singer A",
            actualTitle: "Another Song",
            actualArtist: "Singer B");

        Assert.False(result);
    }

    [Fact]
    public void SelectBestSample_prefers_sample_closest_to_predicted_position()
    {
        NeteasePlaybackSample[] samples =
        [
            new(6, true, "Track", "Artist"),
            new(48, true, "Track", "Artist")
        ];

        NeteasePlaybackSample selected =
            NeteasePlaybackProbePolicy.SelectBestSample(samples, predictedPositionSeconds: 50);

        Assert.Equal(48, selected.PositionSeconds);
    }

    [Fact]
    public void SelectBestSample_keeps_single_far_sample_for_real_seek_recovery()
    {
        NeteasePlaybackSample[] samples =
        [
            new(92, true, "Track", "Artist")
        ];

        NeteasePlaybackSample selected =
            NeteasePlaybackProbePolicy.SelectBestSample(samples, predictedPositionSeconds: 8);

        Assert.Equal(92, selected.PositionSeconds);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 240, true)]
    [InlineData(12, 240, true)]
    public void HasUsableTimeline_rejects_zero_length_timeline(double positionSeconds, double endSeconds, bool expected)
    {
        Assert.Equal(expected, NeteasePlaybackProbePolicy.HasUsableTimeline(positionSeconds, endSeconds));
    }
}
