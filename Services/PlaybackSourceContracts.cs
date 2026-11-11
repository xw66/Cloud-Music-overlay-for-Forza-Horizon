using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public static class PlaybackSourceIds
{
    public const string Netease = "NeteaseProcess";
    public const string Smtc = "SMTC";
}

[Flags]
public enum PlaybackSourceCapabilities
{
    None = 0,
    TrackMetadata = 1 << 0,
    PlaybackState = 1 << 1,
    TransportControls = 1 << 2,
    Lyrics = 1 << 3
}

public interface IPlaybackSource
{
    string Id { get; }

    string DisplayName { get; }

    PlaybackSourceCapabilities Capabilities { get; }

    bool IsAvailable { get; }

    Task<TrackInfo?> GetCurrentTrackAsync();

    Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync();

    Task<PlaybackCommandStatus> ExecuteAsync(PlaybackCommand command);
}

public interface ITrackMetadataProvider
{
    Task<TrackInfo?> GetCurrentTrackAsync();
}

public interface IPlaybackStateProvider
{
    Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync();
}

public interface ITrackAwarePlaybackStateProvider : IPlaybackStateProvider
{
    void SetTrackContext(TrackInfo? track);
}

public interface IPlaybackTransport : ITrackMetadataProvider
{
    Task<bool> NextAsync();

    Task<bool> PreviousAsync();

    Task<bool> TogglePlayPauseAsync();

    Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync();
}

public enum PlaybackCommand
{
    Previous,
    Next,
    TogglePlayPause
}

public enum PlaybackCommandStatus
{
    Succeeded,
    Unsupported,
    Failed
}

public readonly record struct PlaybackCommandResult(
    PlaybackCommandStatus Status,
    string SourceId)
{
    public bool Succeeded => Status == PlaybackCommandStatus.Succeeded;
}
