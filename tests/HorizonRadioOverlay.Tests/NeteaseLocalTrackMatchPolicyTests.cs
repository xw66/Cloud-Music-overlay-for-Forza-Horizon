using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public class NeteaseLocalTrackMatchPolicyTests
{
    [Fact]
    public void SelectSongId_PrefersCurrentHintWhenTitlesAreClose()
    {
        NeteaseLocalTrackCandidate[] candidates =
        [
            new("101", "\u5927\u7720", "\u738b\u5fc3\u51cc", false),
            new("202", "\u5927\u7720 (Live\u7248)", "\u738b\u5fc3\u51cc", true)
        ];

        string? songId = NeteaseLocalTrackMatchPolicy.SelectSongId(candidates, "\u5927\u7720", "\u738b\u5fc3\u51cc");

        Assert.Equal("202", songId);
    }

    [Fact]
    public void SelectSongId_RejectsLowConfidenceCandidates()
    {
        NeteaseLocalTrackCandidate[] candidates =
        [
            new("101", "\u522b\u7684\u6b4c", "\u522b\u7684\u6b4c\u624b", false),
            new("202", "\u53e6\u4e00\u9996", "\u53e6\u4e00\u4f4d", false)
        ];

        string? songId = NeteaseLocalTrackMatchPolicy.SelectSongId(candidates, "\u5927\u7720", "\u738b\u5fc3\u51cc");

        Assert.Null(songId);
    }
}
