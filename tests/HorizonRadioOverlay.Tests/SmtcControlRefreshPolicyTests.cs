using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class SmtcControlRefreshPolicyTests
{
    [Fact]
    public void Stops_waiting_when_track_key_changes()
    {
        bool shouldStop = SmtcControlRefreshPolicy.ShouldStopWaiting(
            previousTrackKey: "SMTC(AppA)|Song A|Artist",
            currentTrackKey: "SMTC(AppA)|Song B|Artist",
            attempt: 1);

        Assert.True(shouldStop);
    }

    [Fact]
    public void Stops_waiting_on_last_attempt_even_without_change()
    {
        bool shouldStop = SmtcControlRefreshPolicy.ShouldStopWaiting(
            previousTrackKey: "SMTC(AppA)|Song A|Artist",
            currentTrackKey: "SMTC(AppA)|Song A|Artist",
            attempt: SmtcControlRefreshPolicy.MaxRefreshAttempts);

        Assert.True(shouldStop);
    }

    [Fact]
    public void Keeps_waiting_before_timeout_when_track_is_unchanged()
    {
        bool shouldStop = SmtcControlRefreshPolicy.ShouldStopWaiting(
            previousTrackKey: "SMTC(AppA)|Song A|Artist",
            currentTrackKey: "SMTC(AppA)|Song A|Artist",
            attempt: 2);

        Assert.False(shouldStop);
    }
}
