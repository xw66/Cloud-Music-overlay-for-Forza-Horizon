using System;
using System.Threading.Tasks;
using HorizonRadioOverlay.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace HorizonRadioOverlay.Services;

public sealed class SmtcTrackService
{
    public async Task<TrackInfo?> GetCurrentTrackAsync()
    {
        GlobalSystemMediaTransportControlsSessionManager manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
        if (session == null)
        {
            return null;
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties media = await session.TryGetMediaPropertiesAsync();
        if (media == null)
        {
            return null;
        }

        string title = media.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string artist = string.IsNullOrWhiteSpace(media.Artist) ? "Unknown Artist" : media.Artist.Trim();
        byte[]? cover = await ReadThumbnailBytesAsync(media.Thumbnail);

        return new TrackInfo
        {
            Name = title,
            Artist = artist,
            SourceAppId = $"SMTC({session.SourceAppUserModelId})",
            CoverBytes = cover
        };
    }

    public async Task<bool> NextAsync()
    {
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
