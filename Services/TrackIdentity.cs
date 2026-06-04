using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public static class TrackIdentity
{
    public static string BuildTrackKey(TrackInfo track, bool includeSourceAppId)
    {
        if (includeSourceAppId && !string.IsNullOrWhiteSpace(track.SourceAppId))
        {
            return $"{track.SourceAppId}|{track.Name}|{track.Artist}";
        }

        return $"{track.Name}|{track.Artist}";
    }
}
