using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class TrackSourcePolicyTests
{
    [Fact]
    public void GetLyricsTooltip_for_smtc_mentions_smtc()
    {
        Assert.Contains("SMTC", TrackSourcePolicy.GetLyricsTooltip(useSmtc: true));
    }

    [Fact]
    public void GetLyricsTooltip_for_netease_mentions_netease()
    {
        Assert.Contains("\u7f51\u6613\u4e91", TrackSourcePolicy.GetLyricsTooltip(useSmtc: false));
    }

    [Theory]
    [InlineData("SMTC")]
    [InlineData("NeteaseProcess")]
    public void Lyrics_are_enabled_for_both_supported_channels(string source)
    {
        Assert.True(TrackSourcePolicy.ShouldEnableLyrics(source));
    }
}
