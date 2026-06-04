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
}
