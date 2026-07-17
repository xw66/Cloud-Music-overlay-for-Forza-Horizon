using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class PlaybackSessionServiceTests
{
    [Theory]
    [InlineData(PlaybackSourceIds.Netease, 2000)]
    [InlineData(PlaybackSourceIds.Smtc, 800)]
    public void GetTrackRefreshInterval_keeps_smtc_responsive(
        string sourceId,
        int expectedMilliseconds)
    {
        TimeSpan interval = PlaybackSessionService.GetTrackRefreshInterval(
            sourceId,
            TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), interval);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(7, 32)]
    [InlineData(20, 32)]
    public void GetFailureCooldown_uses_bounded_exponential_backoff(
        int failures,
        int expectedSeconds)
    {
        TimeSpan cooldown = PlaybackSessionService.GetFailureCooldown(
            TimeSpan.FromSeconds(2),
            failures);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), cooldown);
    }

    [Fact]
    public async Task RefreshAsync_publishes_atomic_track_and_timeline_snapshot()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo { Name = "Track", Artist = "Artist" },
            Timeline = (TimeSpan.FromSeconds(42), true)
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(coordinator);
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        PlaybackSnapshot snapshot = await session.RefreshAsync(
            includeTrack: true,
            includeTimeline: true);

        Assert.Equal("Track", snapshot.Track?.Name);
        Assert.Equal(TimeSpan.FromSeconds(42), snapshot.Position);
        Assert.True(snapshot.IsPlaying);
        Assert.Equal(1, snapshot.TrackVersion);
        Assert.Equal(1, snapshot.TimelineVersion);
        Assert.Equal(PlaybackDataHealth.Healthy, snapshot.Health);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
    }

    [Fact]
    public async Task RefreshAsync_contains_provider_failure_and_degrades_after_threshold()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            ThrowOnTimeline = true
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(coordinator);
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        await session.RefreshTimelineAsync();
        await session.RefreshTimelineAsync();
        PlaybackSnapshot snapshot = await session.RefreshTimelineAsync();

        Assert.Equal(PlaybackDataHealth.Degraded, snapshot.Health);
        Assert.Equal(PlaybackSessionService.FailureThreshold, snapshot.ConsecutiveFailures);
        Assert.Contains("timeline failed", snapshot.LastError);
    }

    [Fact]
    public async Task RefreshAsync_resets_failure_count_after_recovery()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Timeline = null
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(coordinator);
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        await session.RefreshTimelineAsync();
        await session.RefreshTimelineAsync();
        PlaybackSnapshot failed = await session.RefreshTimelineAsync();
        source.Timeline = (TimeSpan.FromSeconds(15), true);
        PlaybackSnapshot recovered = await session.RefreshTimelineAsync();

        Assert.Equal(PlaybackSessionService.FailureThreshold, failed.ConsecutiveFailures);
        Assert.Equal(PlaybackDataHealth.Healthy, recovered.Health);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.Null(recovered.LastError);
    }

    [Fact]
    public async Task Configure_resets_snapshot_when_source_changes()
    {
        FakePlaybackSource netease = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo { Name = "Netease", Artist = "Artist" }
        };
        FakePlaybackSource smtc = new(PlaybackSourceIds.Smtc)
        {
            Track = new TrackInfo { Name = "SMTC", Artist = "Artist" }
        };
        PlaybackCoordinator coordinator = new([netease, smtc]);
        using PlaybackSessionService session = new(coordinator);

        await session.RefreshTrackAsync();
        session.Configure(PlaybackSourceIds.Smtc, timelineEnabled: false);
        PlaybackSnapshot starting = session.LatestSnapshot;
        PlaybackSnapshot refreshed = await session.RefreshTrackAsync();

        Assert.Equal(PlaybackSourceIds.Smtc, starting.SourceId);
        Assert.Null(starting.Track);
        Assert.Equal(0, starting.TrackVersion);
        Assert.Equal("SMTC", refreshed.Track?.Name);
    }

    [Fact]
    public async Task Start_refreshes_in_background_without_ui_polling_provider()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo { Name = "Background", Artist = "Artist" },
            Timeline = (TimeSpan.FromSeconds(8), true)
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(
            coordinator,
            trackInterval: TimeSpan.FromMilliseconds(25),
            timelineInterval: TimeSpan.FromMilliseconds(25));
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        session.Start();
        PlaybackSnapshot snapshot = await WaitForSnapshotAsync(
            session,
            current => current.TrackVersion > 0 && current.TimelineVersion > 0);
        session.Stop();

        Assert.Equal("Background", snapshot.Track?.Name);
        Assert.Equal(TimeSpan.FromSeconds(8), snapshot.Position);
        Assert.True(source.TrackReadCount > 0);
        Assert.True(source.TimelineReadCount > 0);
    }

    private static async Task<PlaybackSnapshot> WaitForSnapshotAsync(
        PlaybackSessionService session,
        Func<PlaybackSnapshot, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            PlaybackSnapshot snapshot = session.LatestSnapshot;
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Playback session did not publish the expected snapshot.");
    }

    private sealed class FakePlaybackSource : IPlaybackSource
    {
        public FakePlaybackSource(string id)
        {
            Id = id;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public PlaybackSourceCapabilities Capabilities =>
            PlaybackSourceCapabilities.TrackMetadata |
            PlaybackSourceCapabilities.PlaybackState;
        public bool IsAvailable { get; set; } = true;
        public TrackInfo? Track { get; set; }
        public (TimeSpan Position, bool IsPlaying)? Timeline { get; set; }
        public bool ThrowOnTimeline { get; set; }
        public int TrackReadCount { get; private set; }
        public int TimelineReadCount { get; private set; }

        public Task<TrackInfo?> GetCurrentTrackAsync()
        {
            TrackReadCount++;
            return Task.FromResult(Track);
        }

        public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
        {
            TimelineReadCount++;
            if (ThrowOnTimeline)
            {
                throw new InvalidOperationException("timeline failed");
            }

            return Task.FromResult(Timeline);
        }

        public Task<bool> ExecuteAsync(
            PlaybackCommand command,
            PlaybackControlBindings bindings)
        {
            return Task.FromResult(false);
        }
    }
}
