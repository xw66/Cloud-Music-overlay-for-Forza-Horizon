using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class PlaybackSyncStateTests
{
    [Fact]
    public void GetCurrentPositionSeconds_advances_with_wall_clock_when_playing()
    {
        PlaybackSyncState state = new();
        DateTime start = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateFromSmtc(10, isPlaying: true, start);

        Assert.Equal(11.5, state.GetCurrentPositionSeconds(start.AddSeconds(1.5)));
    }

    [Fact]
    public void GetCurrentPositionSeconds_stays_fixed_when_paused()
    {
        PlaybackSyncState state = new();
        DateTime start = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateFromSmtc(24, isPlaying: false, start);

        Assert.Equal(24, state.GetCurrentPositionSeconds(start.AddSeconds(3)));
    }

    [Fact]
    public void UpdateFromSmtc_resyncs_when_seek_happens()
    {
        PlaybackSyncState state = new();
        DateTime start = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateFromSmtc(10, isPlaying: true, start);
        state.UpdateFromSmtc(30, isPlaying: true, start.AddSeconds(1));

        Assert.Equal(30.5, state.GetCurrentPositionSeconds(start.AddSeconds(1.5)));
    }

    [Fact]
    public void UpdateFromSmtc_keeps_wall_clock_continuity_when_smtc_is_slightly_behind()
    {
        PlaybackSyncState state = new();
        DateTime start = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateFromSmtc(10, isPlaying: true, start);
        state.UpdateFromSmtc(10.7, isPlaying: true, start.AddSeconds(1));

        double? position = state.GetCurrentPositionSeconds(start.AddSeconds(1.2));
        Assert.NotNull(position);
        Assert.InRange(position.Value, 11.19, 11.21);
    }

    [Fact]
    public void UpdateFromSmtc_does_not_accumulate_drift_when_each_read_is_slightly_behind()
    {
        PlaybackSyncState state = new();
        DateTime start = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateFromSmtc(10.0, isPlaying: true, start);
        state.UpdateFromSmtc(10.7, isPlaying: true, start.AddSeconds(1));
        state.UpdateFromSmtc(11.4, isPlaying: true, start.AddSeconds(2));
        state.UpdateFromSmtc(12.1, isPlaying: true, start.AddSeconds(3));

        double? position = state.GetCurrentPositionSeconds(start.AddSeconds(3.2));
        Assert.NotNull(position);
        Assert.InRange(position.Value, 13.19, 13.21);
    }

    [Fact]
    public void UpdateFromSmtc_keeps_wall_clock_progress_when_smtc_repeats_same_second()
    {
        PlaybackSyncState state = new();
        DateTime start = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        state.UpdateFromSmtc(10.0, isPlaying: true, start);
        state.UpdateFromSmtc(10.0, isPlaying: true, start.AddMilliseconds(250));

        double? position = state.GetCurrentPositionSeconds(start.AddMilliseconds(250));
        Assert.NotNull(position);
        Assert.InRange(position.Value, 10.24, 10.26);
    }
}
