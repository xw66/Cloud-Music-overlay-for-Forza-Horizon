using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public static class TrackIdentity
{
    public static string BuildTrackKey(TrackInfo track, bool includeSourceAppId)
    {
        string album = (track.AlbumTitle ?? string.Empty).Trim();
        int duration = track.DurationSeconds > 0
            ? (int)Math.Round(track.DurationSeconds, MidpointRounding.AwayFromZero)
            : 0;

        if (includeSourceAppId && !string.IsNullOrWhiteSpace(track.SourceAppId))
        {
            return $"{track.SourceAppId}|{track.Name}|{track.Artist}|{album}|{duration}";
        }

        return $"{track.Name}|{track.Artist}|{album}|{duration}";
    }
}
