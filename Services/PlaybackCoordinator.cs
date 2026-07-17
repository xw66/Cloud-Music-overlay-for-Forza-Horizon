using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class PlaybackCoordinator
{
    private readonly IReadOnlyDictionary<string, IPlaybackSource> _sources;
    private readonly IReadOnlyCollection<IPlaybackSource> _sourceList;
    private readonly IPlaybackSource _defaultSource;

    public PlaybackCoordinator(IEnumerable<IPlaybackSource> sources)
    {
        Dictionary<string, IPlaybackSource> sourceMap = sources.ToDictionary(
            source => source.Id,
            StringComparer.OrdinalIgnoreCase);
        if (!sourceMap.TryGetValue(PlaybackSourceIds.Netease, out IPlaybackSource? defaultSource))
        {
            throw new ArgumentException(
                $"Default playback source '{PlaybackSourceIds.Netease}' is required.",
                nameof(sources));
        }

        _sources = sourceMap;
        _sourceList = sourceMap.Values.ToArray();
        _defaultSource = defaultSource;
    }

    public IReadOnlyCollection<IPlaybackSource> Sources => _sourceList;

    public static bool IsSmtcSource(string? sourceId)
    {
        return string.Equals(sourceId, PlaybackSourceIds.Smtc, StringComparison.OrdinalIgnoreCase);
    }

    public IPlaybackSource GetSource(string? sourceId)
    {
        return !string.IsNullOrWhiteSpace(sourceId) && _sources.TryGetValue(sourceId, out IPlaybackSource? source)
            ? source
            : _defaultSource;
    }

    public bool SupportsSource(string? sourceId)
    {
        return GetSource(sourceId).IsAvailable;
    }

    public PlaybackSourceCapabilities GetCapabilities(string? sourceId)
    {
        return GetSource(sourceId).Capabilities;
    }

    public Task<TrackInfo?> GetCurrentTrackAsync(string? sourceId)
    {
        IPlaybackSource source = GetSource(sourceId);
        return source.IsAvailable
            ? source.GetCurrentTrackAsync()
            : Task.FromResult<TrackInfo?>(null);
    }

    public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync(string? sourceId)
    {
        IPlaybackSource source = GetSource(sourceId);
        bool supportsPlaybackState = source.Capabilities.HasFlag(PlaybackSourceCapabilities.PlaybackState);
        return source.IsAvailable && supportsPlaybackState
            ? source.GetPlaybackStateAsync()
            : Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(null);
    }

    public async Task<PlaybackCommandResult> ExecuteAsync(
        PlaybackCommand command,
        OverlaySettings settings)
    {
        IPlaybackSource source = GetSource(settings.TrackSource);
        if (!source.IsAvailable ||
            !source.Capabilities.HasFlag(PlaybackSourceCapabilities.TransportControls))
        {
            return new PlaybackCommandResult(PlaybackCommandStatus.Unsupported, source.Id);
        }

        bool succeeded = await source.ExecuteAsync(
            command,
            PlaybackControlBindings.FromSettings(settings));
        return new PlaybackCommandResult(
            succeeded ? PlaybackCommandStatus.Succeeded : PlaybackCommandStatus.Failed,
            source.Id);
    }
}
