using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class PlaybackSessionServiceTests
{
    [Theory]
    [InlineData(PlaybackSourceIds.Netease, 500)]
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
    public async Task RefreshTrackAsync_does_not_publish_unchanged_track_again()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo
            {
                Name = "Track",
                Artist = "Artist",
                SongId = "123",
                CoverBytes = [1, 2, 3]
            }
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(coordinator);

        PlaybackSnapshot first = await session.RefreshTrackAsync();
        source.Track = new TrackInfo
        {
            Name = "Track",
            Artist = "Artist",
            SongId = "123",
            CoverBytes = [1, 2, 3]
        };
        PlaybackSnapshot second = await session.RefreshTrackAsync();

        Assert.Equal(1, first.TrackVersion);
        Assert.Equal(first.TrackVersion, second.TrackVersion);
    }

    [Fact]
    public async Task RefreshTrackAsync_publishes_same_track_when_cover_is_enriched()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo { Name = "Track", Artist = "Artist" }
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(coordinator);

        PlaybackSnapshot first = await session.RefreshTrackAsync();
        source.Track = new TrackInfo
        {
            Name = "Track",
            Artist = "Artist",
            CoverBytes = [1, 2, 3]
        };
        PlaybackSnapshot second = await session.RefreshTrackAsync();

        Assert.Equal(first.TrackVersion + 1, second.TrackVersion);
    }

    [Fact]
    public async Task RefreshTimelineAsync_does_not_publish_identical_paused_sample_again()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Timeline = (TimeSpan.FromSeconds(42), false)
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(coordinator);
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        PlaybackSnapshot first = await session.RefreshTimelineAsync();
        PlaybackSnapshot second = await session.RefreshTimelineAsync();
        source.Timeline = (TimeSpan.FromSeconds(42), true);
        PlaybackSnapshot resumed = await session.RefreshTimelineAsync();

        Assert.Equal(first.TimelineVersion, second.TimelineVersion);
        Assert.Equal(second.TimelineVersion + 1, resumed.TimelineVersion);
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

    [Fact]
    public async Task Timeline_failures_do_not_back_off_track_refreshes()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo { Name = "Responsive", Artist = "Artist" },
            Timeline = null
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(
            coordinator,
            trackInterval: TimeSpan.FromMilliseconds(20),
            timelineInterval: TimeSpan.FromMilliseconds(20),
            failureCooldown: TimeSpan.FromSeconds(10));
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        session.Start();
        await WaitForSnapshotAsync(
            session,
            _ => source.TimelineReadCount >=
                PlaybackSessionService.FailureThreshold);
        int trackReadsBeforeDelay = source.TrackReadCount;
        await Task.Delay(120);
        session.Stop();

        Assert.True(source.TrackReadCount > trackReadsBeforeDelay);
    }

    [Fact]
    public async Task Track_failures_do_not_back_off_smtc_timeline_refreshes()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Smtc)
        {
            Track = null,
            Timeline = (TimeSpan.FromSeconds(12), true)
        };
        PlaybackCoordinator coordinator = new([
            new FakePlaybackSource(PlaybackSourceIds.Netease),
            source
        ]);
        using PlaybackSessionService session = new(
            coordinator,
            trackInterval: TimeSpan.FromMilliseconds(20),
            timelineInterval: TimeSpan.FromMilliseconds(20),
            failureCooldown: TimeSpan.FromSeconds(10));
        session.Configure(PlaybackSourceIds.Smtc, timelineEnabled: true);

        session.Start();
        await WaitForSnapshotAsync(
            session,
            _ => source.TrackReadCount >= PlaybackSessionService.FailureThreshold);
        int timelineReadsBeforeDelay = source.TimelineReadCount;
        await Task.Delay(120);
        session.Stop();

        Assert.True(source.TimelineReadCount > timelineReadsBeforeDelay);
    }

    [Fact]
    public async Task Faster_track_refresh_does_not_postpone_timeline_refresh()
    {
        FakePlaybackSource source = new(PlaybackSourceIds.Netease)
        {
            Track = new TrackInfo { Name = "Track", Artist = "Artist" },
            Timeline = (TimeSpan.FromSeconds(12), true)
        };
        PlaybackCoordinator coordinator = new([source]);
        using PlaybackSessionService session = new(
            coordinator,
            trackInterval: TimeSpan.FromMilliseconds(20),
            timelineInterval: TimeSpan.FromMilliseconds(60));
        session.Configure(PlaybackSourceIds.Netease, timelineEnabled: true);

        session.Start();
        await Task.Delay(260);
        session.Stop();

        Assert.True(source.TrackReadCount >= 5);
        Assert.True(source.TimelineReadCount >= 3);
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
