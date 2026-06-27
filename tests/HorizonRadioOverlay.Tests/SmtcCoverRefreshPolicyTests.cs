using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcCoverRefreshPolicyTests
{
    [Fact]
    public void Keeps_displayed_cover_when_changed_track_temporarily_has_no_cover()
    {
        byte[] displayedCover = [1, 2, 3];

        byte[]? immediateCover = SmtcCoverRefreshPolicy.SelectImmediateCover(
            trackChanged: true,
            displayedCoverBytes: displayedCover,
            currentCoverBytes: null);

        Assert.Same(displayedCover, immediateCover);
    }

    [Fact]
    public void Keeps_displayed_cover_while_identical_smtc_cover_is_being_verified()
    {
        byte[] displayedCover = [1, 2, 3];

        byte[]? immediateCover = SmtcCoverRefreshPolicy.SelectImmediateCover(
            trackChanged: true,
            displayedCoverBytes: displayedCover,
            currentCoverBytes: [1, 2, 3]);

        Assert.Same(displayedCover, immediateCover);
    }

    [Fact]
    public void Uses_new_cover_immediately_when_it_differs()
    {
        byte[] currentCover = [4, 5, 6];

        byte[]? immediateCover = SmtcCoverRefreshPolicy.SelectImmediateCover(
            trackChanged: true,
            displayedCoverBytes: [1, 2, 3],
            currentCoverBytes: currentCover);

        Assert.Same(currentCover, immediateCover);
    }

    [Fact]
    public void Does_not_update_when_retried_cover_is_already_displayed()
    {
        byte[] displayedCover = [1, 2, 3];
        byte[] retriedCover = [1, 2, 3];

        bool shouldUpdate = SmtcCoverRefreshPolicy.ShouldUpdateDisplayedCover(
            displayedCover,
            retriedCover);

        Assert.False(shouldUpdate);
    }

    [Fact]
    public void Updates_when_missing_cover_becomes_available()
    {
        bool shouldUpdate = SmtcCoverRefreshPolicy.ShouldUpdateDisplayedCover(
            displayedCoverBytes: null,
            candidateCoverBytes: [1, 2, 3]);

        Assert.True(shouldUpdate);
    }

    [Fact]
    public void Detects_stale_cover_when_new_track_initially_reuses_previous_cover_bytes()
    {
        byte[] previousCover = [1, 2, 3];
        byte[] currentCover = [1, 2, 3];

        bool shouldDelay = SmtcCoverRefreshPolicy.ShouldDelayImmediateCoverUpdate(
            trackChanged: true,
            previousDisplayedCoverBytes: previousCover,
            currentCoverBytes: currentCover);

        Assert.True(shouldDelay);
    }

    [Fact]
    public void Accepts_same_cover_after_it_is_confirmed_multiple_times()
    {
        byte[] confirmedCover = [1, 2, 3];

        bool shouldApply = SmtcCoverRefreshPolicy.ShouldApplyRetriedCover(
            expectedTrackKey: "Song B|Artist",
            candidateTrackKey: "Song B|Artist",
            pendingCoverBytes: confirmedCover,
            candidateCoverBytes: confirmedCover,
            matchingCoverObservationCount: 2);

        Assert.True(shouldApply);
    }

    [Fact]
    public void Rejects_retried_cover_for_a_different_track()
    {
        byte[] confirmedCover = [1, 2, 3];

        bool shouldApply = SmtcCoverRefreshPolicy.ShouldApplyRetriedCover(
            expectedTrackKey: "Song B|Artist",
            candidateTrackKey: "Song C|Artist",
            pendingCoverBytes: confirmedCover,
            candidateCoverBytes: confirmedCover,
            matchingCoverObservationCount: 3);

        Assert.False(shouldApply);
    }
}
