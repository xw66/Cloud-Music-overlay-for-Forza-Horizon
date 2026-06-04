namespace HorizonRadioOverlay.Services;

public readonly record struct NeteaseLocalTrackCandidate(string SongId, string Title, string Artist, bool IsCurrentHint);

public static class NeteaseLocalTrackMatchPolicy
{
    public static string? SelectSongId(IEnumerable<NeteaseLocalTrackCandidate> candidates, string title, string artist)
    {
        string? bestSongId = null;
        double bestScore = double.MinValue;
        double secondScore = double.MinValue;

        foreach (NeteaseLocalTrackCandidate candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.SongId))
            {
                continue;
            }

            double score = NeteaseOfficialResolver.ScoreCandidate(title, artist, candidate.Title, candidate.Artist);
            if (candidate.IsCurrentHint)
            {
                score += 30;
            }

            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestSongId = candidate.SongId;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }

        if (string.IsNullOrWhiteSpace(bestSongId))
        {
            return null;
        }

        if (bestScore >= 80 || (bestScore >= 55 && bestScore - secondScore >= 8))
        {
            return bestSongId;
        }

        return null;
    }
}
