using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class SmtcPlaybackSource : IPlaybackSource
{
    private readonly IPlaybackTransport _transport;
    private readonly Func<bool> _isSupported;

    public SmtcPlaybackSource(IPlaybackTransport transport)
        : this(transport, RuntimeFeatureSupport.SupportsSmtc)
    {
    }

    internal SmtcPlaybackSource(IPlaybackTransport transport, Func<bool> isSupported)
    {
        _transport = transport;
        _isSupported = isSupported;
    }

    public string Id => PlaybackSourceIds.Smtc;

    public string DisplayName => "SMTC 渠道（QQ音乐 / Apple Music / 酷狗等）";

    public PlaybackSourceCapabilities Capabilities =>
        PlaybackSourceCapabilities.TrackMetadata |
        PlaybackSourceCapabilities.PlaybackState |
        PlaybackSourceCapabilities.TransportControls |
        PlaybackSourceCapabilities.Lyrics;

    public bool IsAvailable => _isSupported();

    public Task<TrackInfo?> GetCurrentTrackAsync()
    {
        return IsAvailable
            ? _transport.GetCurrentTrackAsync()
            : Task.FromResult<TrackInfo?>(null);
    }

    public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
    {
        return IsAvailable
            ? _transport.GetPlaybackStateAsync()
            : Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(null);
    }

    public async Task<bool> ExecuteAsync(PlaybackCommand command, PlaybackControlBindings bindings)
    {
        if (!IsAvailable)
        {
            return false;
        }

        return command switch
        {
            PlaybackCommand.Previous => await _transport.PreviousAsync(),
            PlaybackCommand.Next => await _transport.NextAsync(),
            PlaybackCommand.TogglePlayPause => await _transport.TogglePlayPauseAsync(),
            _ => false
        };
    }
}
