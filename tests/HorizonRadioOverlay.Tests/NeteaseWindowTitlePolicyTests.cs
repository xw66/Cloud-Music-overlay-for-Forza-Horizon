using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public class NeteaseWindowTitlePolicyTests
{
    [Fact]
    public void SelectBestTitle_SkipsSmtcInternalWindow()
    {
        NeteaseWindowCandidate[] windows =
        [
            new("MediaPlayer SMTC window", true),
            new("Thank U, Next - Ariana Grande", true)
        ];

        string? title = NeteaseWindowTitlePolicy.SelectBestTitle(windows);

        Assert.Equal("Thank U, Next - Ariana Grande", title);
    }

    [Fact]
    public void SelectBestTitle_ReturnsNull_WhenOnlyInvalidTitlesExist()
    {
        NeteaseWindowCandidate[] windows =
        [
            new("MediaPlayer SMTC window", true),
            new("网易云音乐", true),
            new("CloudMusic", false)
        ];

        string? title = NeteaseWindowTitlePolicy.SelectBestTitle(windows);

        Assert.Null(title);
    }

    [Fact]
    public void SelectBestTitle_PrefersVisibleSongWindow()
    {
        NeteaseWindowCandidate[] windows =
        [
            new("青花瓷 - 周杰伦", false),
            new("晴天 - 周杰伦", true)
        ];

        string? title = NeteaseWindowTitlePolicy.SelectBestTitle(windows);

        Assert.Equal("晴天 - 周杰伦", title);
    }
}
