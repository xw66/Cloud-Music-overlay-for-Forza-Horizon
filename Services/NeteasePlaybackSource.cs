using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

internal static class NeteasePlaybackCapabilityPolicy
{
    public static bool SupportsMemoryTimeline(
        bool providerConfigured,
        bool is64BitProcess)
    {
        return providerConfigured && is64BitProcess;
    }
}

public sealed class NeteasePlaybackSource : IPlaybackSource
{
    private readonly ITrackMetadataProvider _metadataProvider;
    private readonly Func<PlaybackCommand, Task<PlaybackCommandStatus>> _executeCommand;
    private readonly IPlaybackStateProvider? _playbackStateProvider;

    public NeteasePlaybackSource(
        ITrackMetadataProvider metadataProvider,
        Func<PlaybackCommand, Task<PlaybackCommandStatus>> executeCommand,
        IPlaybackStateProvider? playbackStateProvider = null)
    {
        _metadataProvider = metadataProvider;
        _executeCommand = executeCommand;
        _playbackStateProvider = playbackStateProvider;
    }

    public string Id => PlaybackSourceIds.Netease;

    public string DisplayName => "网易云专用渠道（默认）";

    public PlaybackSourceCapabilities Capabilities =>
        PlaybackSourceCapabilities.TrackMetadata |
        (SupportsPlaybackState
            ? PlaybackSourceCapabilities.PlaybackState
            : PlaybackSourceCapabilities.None) |
        PlaybackSourceCapabilities.TransportControls |
        PlaybackSourceCapabilities.Lyrics;

    private bool SupportsPlaybackState =>
        NeteasePlaybackCapabilityPolicy.SupportsMemoryTimeline(
            _playbackStateProvider != null,
            Environment.Is64BitProcess);

    public bool IsAvailable => true;

    public async Task<TrackInfo?> GetCurrentTrackAsync()
    {
        TrackInfo? track = await _metadataProvider.GetCurrentTrackAsync();
        if (_playbackStateProvider is ITrackAwarePlaybackStateProvider trackAwareProvider)
        {
            trackAwareProvider.SetTrackContext(track);
        }

        return track;
    }

    public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
    {
        return SupportsPlaybackState
            ? _playbackStateProvider!.GetPlaybackStateAsync()
            : Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(null);
    }

    public Task<PlaybackCommandStatus> ExecuteAsync(PlaybackCommand command)
    {
        return _executeCommand(command);
    }
}
