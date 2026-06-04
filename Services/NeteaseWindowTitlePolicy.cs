namespace HorizonRadioOverlay.Services;

public readonly record struct NeteaseWindowCandidate(string Title, bool IsVisible);

public static class NeteaseWindowTitlePolicy
{
    private static readonly string[] InvalidExactTitles =
    [
        "网易云音乐",
        "NetEase Cloud Music",
        "CloudMusic",
        "cloudmusic"
    ];

    private static readonly string[] InvalidTitleFragments =
    [
        "SMTC window",
        "MediaPlayer SMTC window",
        "CEF",
        "Widget"
    ];

    public static string? SelectBestTitle(IEnumerable<NeteaseWindowCandidate> windows)
    {
        string? bestTitle = null;
        int bestScore = int.MinValue;

        foreach (NeteaseWindowCandidate window in windows)
        {
            string title = window.Title.Trim();
            if (!TryScoreTitle(title, window.IsVisible, out int score))
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestTitle = title;
            }
        }

        return bestTitle;
    }

    private static bool TryScoreTitle(string title, bool isVisible, out int score)
    {
        score = 0;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (InvalidExactTitles.Any(x => string.Equals(x, title, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (InvalidTitleFragments.Any(x => title.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!TryParseSongArtist(title, out _, out _))
        {
            return false;
        }

        score = 100;
        if (isVisible)
        {
            score += 10;
        }

        return true;
    }

    private static bool TryParseSongArtist(string title, out string songName, out string artistName)
    {
        songName = string.Empty;
        artistName = string.Empty;

        int splitIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (splitIndex <= 0 || splitIndex >= title.Length - 3)
        {
            return false;
        }

        songName = title[..splitIndex].Trim();
        artistName = title[(splitIndex + 3)..].Trim();
        return !string.IsNullOrWhiteSpace(songName) && !string.IsNullOrWhiteSpace(artistName);
    }
}
