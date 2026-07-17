using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class PlaybackCoordinatorTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void NeteasePlaybackCapabilityPolicy_requires_provider_and_64_bit_process(
        bool providerConfigured,
        bool is64BitProcess,
        bool expected)
    {
        Assert.Equal(
            expected,
            NeteasePlaybackCapabilityPolicy.SupportsMemoryTimeline(
                providerConfigured,
                is64BitProcess));
    }

    [Fact]
    public async Task GetCurrentTrackAsync_routes_to_selected_unified_source()
    {
        var netease = new FakePlaybackSource(PlaybackSourceIds.Netease, "Netease");
        var smtc = new FakePlaybackSource(PlaybackSourceIds.Smtc, "SMTC");
        var coordinator = new PlaybackCoordinator([netease, smtc]);

        TrackInfo? neteaseTrack = await coordinator.GetCurrentTrackAsync(PlaybackSourceIds.Netease);
        TrackInfo? smtcTrack = await coordinator.GetCurrentTrackAsync(PlaybackSourceIds.Smtc);

        Assert.Equal("Netease", neteaseTrack?.Name);
        Assert.Equal("SMTC", smtcTrack?.Name);
        Assert.Equal(1, netease.ReadCount);
        Assert.Equal(1, smtc.ReadCount);
    }

    [Fact]
    public async Task GetCurrentTrackAsync_falls_back_to_netease_for_unknown_source()
    {
        var netease = new FakePlaybackSource(PlaybackSourceIds.Netease, "Netease");
        var coordinator = new PlaybackCoordinator([
            netease,
            new FakePlaybackSource(PlaybackSourceIds.Smtc, "SMTC")
        ]);

        TrackInfo? track = await coordinator.GetCurrentTrackAsync("unknown-source");

        Assert.Equal("Netease", track?.Name);
        Assert.Equal(1, netease.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_routes_command_without_source_specific_branching()
    {
        var netease = new FakePlaybackSource(PlaybackSourceIds.Netease, "Netease");
        var smtc = new FakePlaybackSource(PlaybackSourceIds.Smtc, "SMTC");
        var coordinator = new PlaybackCoordinator([netease, smtc]);
        var settings = new OverlaySettings { TrackSource = PlaybackSourceIds.Smtc };

        PlaybackCommandResult result = await coordinator.ExecuteAsync(PlaybackCommand.Next, settings);

        Assert.True(result.Succeeded);
        Assert.Equal(PlaybackSourceIds.Smtc, result.SourceId);
        Assert.Equal(1, smtc.ExecuteCount);
        Assert.Equal(0, netease.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_reports_unavailable_source_as_unsupported()
    {
        var smtc = new FakePlaybackSource(PlaybackSourceIds.Smtc, "SMTC")
        {
            IsAvailable = false
        };
        var coordinator = new PlaybackCoordinator([
            new FakePlaybackSource(PlaybackSourceIds.Netease, "Netease"),
            smtc
        ]);
        var settings = new OverlaySettings { TrackSource = PlaybackSourceIds.Smtc };

        PlaybackCommandResult result = await coordinator.ExecuteAsync(PlaybackCommand.TogglePlayPause, settings);

        Assert.Equal(PlaybackCommandStatus.Unsupported, result.Status);
        Assert.Equal(PlaybackSourceIds.Smtc, result.SourceId);
        Assert.Equal(0, smtc.ExecuteCount);
    }

    [Fact]
    public async Task NeteasePlaybackSource_uses_configured_hotkey_binding()
    {
        var sender = new FakeHotkeySender();
        var source = new NeteasePlaybackSource(new FakeMetadataProvider("Netease"), sender);
        PlaybackControlBindings bindings = new("Prev", "Next", "Toggle");

        bool succeeded = await source.ExecuteAsync(PlaybackCommand.Previous, bindings);

        Assert.True(succeeded);
        Assert.Equal("Prev", sender.LastHotkey);
        Assert.True(source.Capabilities.HasFlag(PlaybackSourceCapabilities.TrackMetadata));
        Assert.False(source.Capabilities.HasFlag(PlaybackSourceCapabilities.PlaybackState));
    }

    [Fact]
    public async Task NeteasePlaybackSource_exposes_memory_timeline_through_unified_interface()
    {
        var stateProvider = new FakeTrackAwarePlaybackStateProvider();
        var source = new NeteasePlaybackSource(
            new FakeMetadataProvider("Netease"),
            new FakeHotkeySender(),
            stateProvider);

        TrackInfo? track = await source.GetCurrentTrackAsync();
        (TimeSpan Position, bool IsPlaying)? state = await source.GetPlaybackStateAsync();

        Assert.True(source.Capabilities.HasFlag(PlaybackSourceCapabilities.PlaybackState));
        Assert.Same(track, stateProvider.Track);
        Assert.Equal(TimeSpan.FromSeconds(42), state?.Position);
        Assert.True(state?.IsPlaying);
    }

    [Fact]
    public async Task SmtcPlaybackSource_exposes_timeline_and_transport_capabilities()
    {
        var transport = new FakePlaybackTransport("SMTC");
        var source = new SmtcPlaybackSource(transport, () => true);

        bool succeeded = await source.ExecuteAsync(
            PlaybackCommand.TogglePlayPause,
            new PlaybackControlBindings("", "", ""));
        (TimeSpan Position, bool IsPlaying)? state = await source.GetPlaybackStateAsync();

        Assert.True(succeeded);
        Assert.Equal(1, transport.ToggleCount);
        Assert.Equal(TimeSpan.FromSeconds(10), state?.Position);
        Assert.True(source.Capabilities.HasFlag(PlaybackSourceCapabilities.PlaybackState));
        Assert.True(source.Capabilities.HasFlag(PlaybackSourceCapabilities.TransportControls));
    }

    private sealed class FakePlaybackSource(string id, string title) : IPlaybackSource
    {
        public string Id { get; } = id;
        public string DisplayName => title;
        public PlaybackSourceCapabilities Capabilities { get; init; } =
            PlaybackSourceCapabilities.TrackMetadata |
            PlaybackSourceCapabilities.PlaybackState |
            PlaybackSourceCapabilities.TransportControls;
        public bool IsAvailable { get; set; } = true;
        public int ReadCount { get; private set; }
        public int ExecuteCount { get; private set; }

        public Task<TrackInfo?> GetCurrentTrackAsync()
        {
            ReadCount++;
            return Task.FromResult<TrackInfo?>(new TrackInfo
            {
                Name = title,
                Artist = "Artist"
            });
        }

        public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
        {
            return Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(
                (TimeSpan.FromSeconds(10), true));
        }

        public Task<bool> ExecuteAsync(PlaybackCommand command, PlaybackControlBindings bindings)
        {
            ExecuteCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeMetadataProvider(string title) : ITrackMetadataProvider
    {
        public Task<TrackInfo?> GetCurrentTrackAsync()
        {
            return Task.FromResult<TrackInfo?>(new TrackInfo
            {
                Name = title,
                Artist = "Artist"
            });
        }
    }

    private sealed class FakePlaybackTransport(string title) : IPlaybackTransport
    {
        public int ToggleCount { get; private set; }

        public Task<TrackInfo?> GetCurrentTrackAsync()
        {
            return Task.FromResult<TrackInfo?>(new TrackInfo
            {
                Name = title,
                Artist = "Artist"
            });
        }

        public Task<bool> NextAsync() => Task.FromResult(true);

        public Task<bool> PreviousAsync() => Task.FromResult(true);

        public Task<bool> TogglePlayPauseAsync()
        {
            ToggleCount++;
            return Task.FromResult(true);
        }

        public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
        {
            return Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(
                (TimeSpan.FromSeconds(10), true));
        }
    }

    private sealed class FakeHotkeySender : IHotkeySender
    {
        public string? LastHotkey { get; private set; }

        public bool Send(string hotkeyText)
        {
            LastHotkey = hotkeyText;
            return true;
        }
    }

    private sealed class FakeTrackAwarePlaybackStateProvider : ITrackAwarePlaybackStateProvider
    {
        public TrackInfo? Track { get; private set; }

        public void SetTrackContext(TrackInfo? track)
        {
            Track = track;
        }

        public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
        {
            return Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(
                (TimeSpan.FromSeconds(42), true));
        }
    }
}
