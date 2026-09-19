using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class TrackDisplayPolicyTests
{
    [Fact]
    public void SameTrack_NeteaseCoverArrivesLater_ShouldRefresh()
    {
        bool shouldRefresh = TrackDisplayPolicy.ShouldRefreshCoverForSameTrack(
            useSmtc: false,
            displayChanged: false,
            previousDisplayedCoverBytes: null,
            currentCoverBytes: [1, 2, 3]);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void SameTrack_NeteaseSameCoverBytes_ShouldNotRefresh()
    {
        bool shouldRefresh = TrackDisplayPolicy.ShouldRefreshCoverForSameTrack(
            useSmtc: false,
            displayChanged: false,
            previousDisplayedCoverBytes: [1, 2, 3],
            currentCoverBytes: [1, 2, 3]);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void SameTrack_SmtcCoverChange_ShouldNotUseThisPath()
    {
        bool shouldRefresh = TrackDisplayPolicy.ShouldRefreshCoverForSameTrack(
            useSmtc: true,
            displayChanged: false,
            previousDisplayedCoverBytes: null,
            currentCoverBytes: [1, 2, 3]);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void SameTrack_TransientMissingCover_ShouldKeepExistingCover()
    {
        bool shouldRefresh = TrackDisplayPolicy.ShouldRefreshCoverForSameTrack(
            useSmtc: false,
            displayChanged: false,
            previousDisplayedCoverBytes: [1, 2, 3],
            currentCoverBytes: null);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ChangedTrack_ShouldNotUseSameTrackRefreshPath()
    {
        bool shouldRefresh = TrackDisplayPolicy.ShouldRefreshCoverForSameTrack(
            useSmtc: false,
            displayChanged: true,
            previousDisplayedCoverBytes: null,
            currentCoverBytes: [1, 2, 3]);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void SmtcCoverRefreshPolicy_SameCoverBytes_ShouldNotUpdateDisplayedCover()
    {
        byte[] cover = [10, 20, 30, 40];
        byte[] duplicate = [10, 20, 30, 40];

        bool shouldUpdate = SmtcCoverRefreshPolicy.ShouldUpdateDisplayedCover(cover, duplicate);
        Assert.False(shouldUpdate);
    }
}
