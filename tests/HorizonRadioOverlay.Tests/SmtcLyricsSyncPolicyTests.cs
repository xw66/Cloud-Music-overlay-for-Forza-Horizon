using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcLyricsSyncPolicyTests
{
    [Fact]
    public void ShouldUseExternalLyrics_only_for_smtc_with_lyrics_enabled()
    {
        Assert.True(SmtcLyricsSyncPolicy.ShouldUseExternalLyrics(enableLyrics: true, useSmtc: true));
        Assert.False(SmtcLyricsSyncPolicy.ShouldUseExternalLyrics(enableLyrics: false, useSmtc: true));
        Assert.False(SmtcLyricsSyncPolicy.ShouldUseExternalLyrics(enableLyrics: true, useSmtc: false));
    }

    [Fact]
    public void ShouldUseFastPolling_only_for_smtc_with_lyrics_enabled()
    {
        Assert.True(SmtcLyricsSyncPolicy.ShouldUseFastPolling(enableLyrics: true, useSmtc: true));
        Assert.False(SmtcLyricsSyncPolicy.ShouldUseFastPolling(enableLyrics: false, useSmtc: true));
        Assert.False(SmtcLyricsSyncPolicy.ShouldUseFastPolling(enableLyrics: true, useSmtc: false));
    }

    [Fact]
    public void ShouldPrioritizeTimelineSync_only_for_smtc_with_lyrics_enabled()
    {
        Assert.True(SmtcLyricsSyncPolicy.ShouldPrioritizeTimelineSync(enableLyrics: true, useSmtc: true));
        Assert.False(SmtcLyricsSyncPolicy.ShouldPrioritizeTimelineSync(enableLyrics: false, useSmtc: true));
        Assert.False(SmtcLyricsSyncPolicy.ShouldPrioritizeTimelineSync(enableLyrics: true, useSmtc: false));
    }

    [Fact]
    public void GetInitialPlaybackPositionSeconds_returns_known_smtc_position()
    {
        Assert.Equal(42.5, SmtcLyricsSyncPolicy.GetInitialPlaybackPositionSeconds(42.5));
        Assert.Equal(0, SmtcLyricsSyncPolicy.GetInitialPlaybackPositionSeconds(null));
        Assert.Equal(0, SmtcLyricsSyncPolicy.GetInitialPlaybackPositionSeconds(-1));
    }

    [Fact]
    public void GetDisplayPlaybackPositionSeconds_applies_fixed_delay_compensation()
    {
        Assert.Equal(1.1, SmtcLyricsSyncPolicy.GetDisplayPlaybackPositionSeconds(0.6));
        Assert.InRange(SmtcLyricsSyncPolicy.GetDisplayPlaybackPositionSeconds(1.5), 1.999999, 2.000001);
        Assert.InRange(SmtcLyricsSyncPolicy.GetDisplayPlaybackPositionSeconds(10.0), 10.499999, 10.500001);
    }

    [Fact]
    public void ShouldResyncFromSmtc_only_when_gap_exceeds_threshold()
    {
        Assert.False(SmtcLyricsSyncPolicy.ShouldResyncFromSmtc(10.8, 9.1));
        Assert.True(SmtcLyricsSyncPolicy.ShouldResyncFromSmtc(15.5, 10.0));
    }
}
