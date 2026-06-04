using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public class NeteaseOfficialResolverTests
{
    [Fact]
    public void NormalizeTitle_RemovesCommonNoise()
    {
        string normalized = NeteaseOfficialResolver.NormalizeTitle("\u5927\u7720 (Live\u7248)");

        Assert.Equal("\u5927\u7720", normalized);
    }

    [Fact]
    public void NormalizeArtistToken_SupportsChineseSeparators()
    {
        string normalized = NeteaseOfficialResolver.NormalizeArtistToken("\u6b4c\u624bA\u3001\u6b4c\u624bB\uFF0C\u6b4c\u624bC");

        Assert.Equal("\u6b4c\u624ba|\u6b4c\u624bb|\u6b4c\u624bc", normalized);
    }

    [Fact]
    public void ScoreCandidate_PrefersExactTitleAndArtist()
    {
        double exact = NeteaseOfficialResolver.ScoreCandidate("\u5927\u7720", "\u738b\u5fc3\u51cc", "\u5927\u7720", "\u738b\u5fc3\u51cc");
        double mismatch = NeteaseOfficialResolver.ScoreCandidate("\u5927\u7720", "\u738b\u5fc3\u51cc", "\u5927\u7720 (Live)", "\u522b\u7684\u6b4c\u624b");

        Assert.True(exact > mismatch);
        Assert.True(exact >= 80);
    }

    [Fact]
    public void BuildTrackCacheKey_NormalizesWhitespaceAndArtistOrder()
    {
        string left = NeteaseOfficialResolver.BuildTrackCacheKey(" Thank U, Next ", "Ariana Grande");
        string right = NeteaseOfficialResolver.BuildTrackCacheKey("Thank U, Next", "Ariana Grande");

        Assert.Equal(left, right);
    }
}
