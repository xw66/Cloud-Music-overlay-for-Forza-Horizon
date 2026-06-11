namespace HorizonRadioOverlay.Services;

public static class SmtcReadPolicy
{
    public const int MaxTrackReadAttempts = 3;

    public static string NormalizeTitle(string? title)
    {
        return title?.Trim() ?? string.Empty;
    }

    public static bool ShouldRetryMissingTrackMetadata(int attempt, string normalizedTitle)
    {
        return attempt < MaxTrackReadAttempts && string.IsNullOrWhiteSpace(normalizedTitle);
    }

    public static int GetRetryDelayMilliseconds(int attempt)
    {
        return 80 * attempt;
    }

    public static string FormatSourceAppId(string? sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return "SMTC";
        }

        return $"SMTC({sourceAppId.Trim()})";
    }

    public static string GetCoverSource(bool hasThumbnail)
    {
        return hasThumbnail ? "smtc-thumbnail" : "smtc-no-thumbnail";
    }

    public static string NormalizeArtist(string? artist, string? albumTitle)
    {
        string normalizedArtist = artist?.Trim() ?? string.Empty;
        string normalizedAlbum = albumTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedArtist))
        {
            return string.Empty;
        }

        if (TrySplitAppleMusicArtistAlbum(normalizedArtist, out string splitArtist, out string splitAlbum) &&
            (string.IsNullOrWhiteSpace(normalizedAlbum) ||
             string.Equals(splitAlbum, normalizedAlbum, StringComparison.OrdinalIgnoreCase)))
        {
            return splitArtist;
        }

        if (string.IsNullOrWhiteSpace(normalizedAlbum))
        {
            return normalizedArtist;
        }

        foreach (string separator in ArtistAlbumSeparators)
        {
            string suffix = $"{separator}{normalizedAlbum}";
            if (normalizedArtist.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedArtist[..^suffix.Length].Trim();
            }
        }

        return normalizedArtist;
    }

    public static string NormalizeAlbumTitle(string? albumTitle, string? artist)
    {
        string normalizedAlbum = albumTitle?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedAlbum))
        {
            return normalizedAlbum;
        }

        string normalizedArtist = artist?.Trim() ?? string.Empty;
        return TrySplitAppleMusicArtistAlbum(normalizedArtist, out _, out string splitAlbum)
            ? splitAlbum
            : string.Empty;
    }

    private static readonly string[] ArtistAlbumSeparators = [" \u2014 ", " \u2013 ", " - "];

    private static bool TrySplitAppleMusicArtistAlbum(string value, out string artist, out string album)
    {
        artist = string.Empty;
        album = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (string separator in ArtistAlbumSeparators)
        {
            int index = value.LastIndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index + separator.Length >= value.Length)
            {
                continue;
            }

            artist = value[..index].Trim();
            album = value[(index + separator.Length)..].Trim();
            return !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album);
        }

        return false;
    }
}
