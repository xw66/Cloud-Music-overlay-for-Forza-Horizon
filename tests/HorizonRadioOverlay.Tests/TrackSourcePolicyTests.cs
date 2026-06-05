using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class TrackSourcePolicyTests
{
    [Fact]
    public void GetLyricsTooltip_for_smtc_does_not_mention_netease()
    {
        string tooltip = TrackSourcePolicy.GetLyricsTooltip(true);

        Assert.DoesNotContain("网易云", tooltip);
        Assert.Contains("SMTC", tooltip);
    }
}
