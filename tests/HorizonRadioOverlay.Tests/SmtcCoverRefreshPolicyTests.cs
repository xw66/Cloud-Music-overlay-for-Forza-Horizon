using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcCoverRefreshPolicyTests
{
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
