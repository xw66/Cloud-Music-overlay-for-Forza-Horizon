using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcReadPolicyTests
{
    [Fact]
    public void Missing_title_is_retried_before_last_attempt()
    {
        bool shouldRetry = SmtcReadPolicy.ShouldRetryMissingTrackMetadata(1, string.Empty);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void Missing_title_is_not_retried_on_last_attempt()
    {
        bool shouldRetry = SmtcReadPolicy.ShouldRetryMissingTrackMetadata(SmtcReadPolicy.MaxTrackReadAttempts, string.Empty);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void Source_app_id_is_formatted_consistently()
    {
        Assert.Equal("SMTC(CloudMusic)", SmtcReadPolicy.FormatSourceAppId(" CloudMusic "));
        Assert.Equal("SMTC", SmtcReadPolicy.FormatSourceAppId(" "));
    }

    [Fact]
    public void Cover_source_reflects_thumbnail_presence()
    {
        Assert.Equal("smtc-thumbnail", SmtcReadPolicy.GetCoverSource(true));
        Assert.Equal("smtc-no-thumbnail", SmtcReadPolicy.GetCoverSource(false));
    }

    [Fact]
    public void NormalizeArtist_removes_album_suffix_from_apple_music_style_metadata()
    {
        Assert.Equal("\u7530\u99a5\u7504", SmtcReadPolicy.NormalizeArtist("\u7530\u99a5\u7504 \u2014 \u6e3a\u5c0f", "\u6e3a\u5c0f"));
    }

    [Fact]
    public void NormalizeArtist_removes_album_suffix_even_when_album_field_is_empty()
    {
        Assert.Equal("\u7530\u99a5\u7504", SmtcReadPolicy.NormalizeArtist("\u7530\u99a5\u7504 \u2014 \u6e3a\u5c0f", ""));
    }

    [Fact]
    public void NormalizeAlbumTitle_recovers_album_from_apple_music_artist_field()
    {
        Assert.Equal("\u6e3a\u5c0f", SmtcReadPolicy.NormalizeAlbumTitle("", "\u7530\u99a5\u7504 \u2014 \u6e3a\u5c0f"));
    }

    [Fact]
    public void NormalizeArtist_keeps_regular_artist_text()
    {
        Assert.Equal("G.E.M. \u9093\u7d2b\u68cb", SmtcReadPolicy.NormalizeArtist(" G.E.M. \u9093\u7d2b\u68cb ", "\u65b0\u7684\u5fc3\u8df3"));
    }
}
