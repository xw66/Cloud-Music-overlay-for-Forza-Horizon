using HorizonRadioOverlay.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace HorizonRadioOverlay.Services;

public sealed class SmtcTrackService
{
    private readonly DiagnosticService? _diagnostic;

    public SmtcTrackService(DiagnosticService? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    public async Task<TrackInfo?> GetCurrentTrackAsync()
    {
        if (!RuntimeFeatureSupport.SupportsSmtc())
        {
            _diagnostic?.Info("SMTC track read skipped: unsupported OS version.");
            return null;
        }

        for (int attempt = 1; attempt <= SmtcReadPolicy.MaxTrackReadAttempts; attempt++)
        {
            try
            {
                GlobalSystemMediaTransportControlsSessionManager manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
                if (session == null)
                {
                    if (attempt == 1)
                    {
                        _diagnostic?.Info("SMTC track read skipped: no active session.");
                    }

                    return null;
                }

                GlobalSystemMediaTransportControlsSessionMediaProperties media = await session.TryGetMediaPropertiesAsync();
                if (media == null)
                {
                    if (attempt < SmtcReadPolicy.MaxTrackReadAttempts)
                    {
                        await Task.Delay(SmtcReadPolicy.GetRetryDelayMilliseconds(attempt));
                        continue;
                    }

                    _diagnostic?.Warn("SMTC track read failed: media properties were null.");
                    return null;
                }

                string title = SmtcReadPolicy.NormalizeTitle(media.Title);
                if (SmtcReadPolicy.ShouldRetryMissingTrackMetadata(attempt, title))
                {
                    await Task.Delay(SmtcReadPolicy.GetRetryDelayMilliseconds(attempt));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    _diagnostic?.Warn("SMTC track read failed: title was empty after retries.");
                    return null;
                }

                string artist = string.IsNullOrWhiteSpace(media.Artist) ? "Unknown Artist" : media.Artist.Trim();
                byte[]? cover = await ReadThumbnailBytesAsync(media.Thumbnail);
                double duration = TryGetDurationSeconds(session);

                return new TrackInfo
                {
                    Name = title,
                    Artist = artist,
                    SourceAppId = SmtcReadPolicy.FormatSourceAppId(session.SourceAppUserModelId),
                    SongId = null,
                    CoverBytes = cover,
                    DurationSeconds = duration,
                    CoverSource = SmtcReadPolicy.GetCoverSource(cover != null)
                };
            }
            catch (Exception ex) when (attempt < SmtcReadPolicy.MaxTrackReadAttempts)
            {
                _diagnostic?.Warn($"SMTC track read attempt {attempt} failed, retrying: {ex.Message}");
                await Task.Delay(SmtcReadPolicy.GetRetryDelayMilliseconds(attempt));
            }
            catch (Exception ex)
            {
                _diagnostic?.Error("SMTC track read failed.", ex);
                return null;
            }
        }

        return null;
    }

    public async Task<bool> NextAsync()
    {
        if (!RuntimeFeatureSupport.SupportsSmtc())
        {
            return false;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session == null)
            {
                return false;
            }

            return await session.TrySkipNextAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> PreviousAsync()
    {
        if (!RuntimeFeatureSupport.SupportsSmtc())
        {
            return false;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session == null)
            {
                return false;
            }

            return await session.TrySkipPreviousAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TogglePlayPauseAsync()
    {
        if (!RuntimeFeatureSupport.SupportsSmtc())
        {
            return false;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session == null)
            {
                return false;
            }

            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = session.GetPlaybackInfo();
            if (playback == null)
            {
                return false;
            }

            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return await session.TryPauseAsync();
            }
            else
            {
                return await session.TryPlayAsync();
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
    {
        if (!RuntimeFeatureSupport.SupportsSmtc())
        {
            return null;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session == null) return null;

            GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = session.GetTimelineProperties();
            GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback = session.GetPlaybackInfo();
            if (timeline == null || playback == null) return null;

            bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return (timeline.Position, isPlaying);
        }
        catch
        {
            return null;
        }
    }

    private double TryGetDurationSeconds(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = session.GetTimelineProperties();
            if (timeline != null)
            {
                return timeline.EndTime.TotalSeconds;
            }
        }
        catch (Exception ex)
        {
            _diagnostic?.Warn($"SMTC timeline read failed: {ex.Message}");
        }

        return 0;
    }

    private static async Task<byte[]?> ReadThumbnailBytesAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null)
        {
            return null;
        }

        try
        {
            using IRandomAccessStreamWithContentType stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > int.MaxValue)
            {
                return null;
            }

            uint size = (uint)stream.Size;
            IBuffer buffer = new Windows.Storage.Streams.Buffer(size);
            IBuffer loaded = await stream.ReadAsync(buffer, size, InputStreamOptions.None);

            byte[] bytes = new byte[loaded.Length];
            using DataReader reader = DataReader.FromBuffer(loaded);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
