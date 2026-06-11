using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HorizonRadioOverlay.Services;

public sealed class LyricsService : IDisposable
{
    private const long RetryAfterMissMs = 1500;
    private const double MinimumAcceptedCandidateScore = 45;
    private const double MinimumHighConfidenceCandidateScore = 68;
    private const double MinimumCandidateScoreGap = 8;
    private const double LineBoundaryStabilitySeconds = 0.08;
    private const double BackwardLineSwitchToleranceSeconds = 0.65;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static readonly Regex QueryNoiseRegex = new(
        @"(\(.*?(live|\u4f34\u594f|dj|cover|vip|explicit|remaster|version|ver\.?|feat\.?|ft\.?|\u6bcd\u5e26|\u8d85\u54c1\u8d28|\u81fb\u54c1|\u675c\u6bd4|\u5168\u666f\u58f0).*?\))|(\[.*?(live|\u4f34\u594f|dj|cover|vip|explicit|remaster|version|ver\.?|feat\.?|ft\.?|\u6bcd\u5e26|\u8d85\u54c1\u8d28|\u81fb\u54c1|\u675c\u6bd4|\u5168\u666f\u58f0).*?\])|(\b(?:live|dj|cover|vip|explicit|remaster|version|ver|feat|ft)\.?)|(\u6bcd\u5e26)|(\u8d85\u54c1\u8d28)|(\u81fb\u54c1)|(\u675c\u6bd4\u5168\u666f\u58f0?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Func<string, string, string?, double, Task<IReadOnlyList<SongSearchCandidate>>> _searchSongCandidatesAsync;
    private readonly Func<string, Task<string>> _fetchLyricPayloadAsync;
    private readonly Func<long> _nowProvider;
    private readonly DiagnosticService? _diagnostic;
    private string _lastLyricsKey = string.Empty;
    private string? _inFlightKey;
    private long _lastFetchAttemptAtMs;
    private readonly Dictionary<string, List<(double Time, string Text)>> _lyricsCache = new(StringComparer.Ordinal);
    private List<(double Time, string Text)>? _cachedLyrics;
    private double _currentPlaybackPositionSeconds;
    private string _currentLine = string.Empty;
    private int _lastLineIndex = -1;

    public bool HasLyrics => _cachedLyrics is { Count: > 0 };

    public LyricsService(DiagnosticService diagnostic)
    {
        _searchSongCandidatesAsync = SearchSongCandidatesAsync;
        _fetchLyricPayloadAsync = FetchLyricPayloadAsync;
        _nowProvider = () => Environment.TickCount64;
        _diagnostic = diagnostic;
    }

    internal LyricsService(
        Func<string, string, string?, double, Task<IReadOnlyList<SongSearchCandidate>>> searchSongCandidatesAsync,
        Func<string, Task<string>> fetchLyricPayloadAsync,
        Func<long>? nowProvider = null)
    {
        _searchSongCandidatesAsync = searchSongCandidatesAsync;
        _fetchLyricPayloadAsync = fetchLyricPayloadAsync;
        _nowProvider = nowProvider ?? (() => Environment.TickCount64);
    }

    internal LyricsService(
        Func<string, string, string?, double, Task<IReadOnlyList<string>>> searchSongIdsAsync,
        Func<string, Task<string>> fetchLyricPayloadAsync,
        Func<long>? nowProvider = null)
        : this(
            async (songName, artist, albumTitle, durationSeconds) =>
            {
                IReadOnlyList<string> ids = await searchSongIdsAsync(songName, artist, albumTitle, durationSeconds);
                return ids.Select(id => new SongSearchCandidate(id, 100, string.Empty, string.Empty, string.Empty, 0)).ToArray();
            },
            fetchLyricPayloadAsync,
            nowProvider)
    {
    }

    public async Task FetchLyricsAsync(
        string songName,
        string artist,
        string? albumTitle,
        double startTimeSeconds,
        double durationSeconds = 0,
        string? preferredSongId = null)
    {
        if (ShouldSkipLookup(songName, artist))
        {
            _diagnostic?.Info($"Lyrics fetch skipped for placeholder metadata: {songName} / {artist}");
            _cachedLyrics = null;
            _currentLine = string.Empty;
            _lastLyricsKey = string.Empty;
            _inFlightKey = null;
            _lastLineIndex = -1;
            return;
        }

        string key = BuildLyricsKey(songName, artist, albumTitle, durationSeconds);
        if (string.Equals(_lastLyricsKey, key, StringComparison.Ordinal))
        {
            if (_cachedLyrics is { Count: > 0 })
            {
                return;
            }

            if (string.Equals(_inFlightKey, key, StringComparison.Ordinal))
            {
                _diagnostic?.Info($"Lyrics fetch skipped because request is already in flight: {songName} / {artist}");
                return;
            }

            if (_nowProvider() - _lastFetchAttemptAtMs < RetryAfterMissMs)
            {
                _diagnostic?.Info($"Lyrics fetch throttled after previous miss: {songName} / {artist}");
                return;
            }
        }

        if (_lyricsCache.TryGetValue(key, out List<(double Time, string Text)>? cachedLyrics))
        {
            _lastLyricsKey = key;
            _cachedLyrics = cachedLyrics;
            _currentPlaybackPositionSeconds = startTimeSeconds;
            _currentLine = string.Empty;
            _lastLineIndex = -1;
            return;
        }

        _lastLyricsKey = key;
        _currentPlaybackPositionSeconds = startTimeSeconds;
        _currentLine = string.Empty;
        _lastLineIndex = -1;
        _lastFetchAttemptAtMs = _nowProvider();
        _inFlightKey = key;
        try
        {
            List<(double Time, string Text)>? fetchedLyrics = await FetchLyricsFromApiAsync(
                songName,
                artist,
                albumTitle,
                durationSeconds,
                preferredSongId);
            if (!string.Equals(_lastLyricsKey, key, StringComparison.Ordinal))
            {
                _diagnostic?.Info($"Lyrics fetch ignored stale result: {songName} / {artist}");
                return;
            }

            _cachedLyrics = fetchedLyrics;
            _lastLineIndex = -1;
            if (_cachedLyrics is { Count: > 0 })
            {
                _lyricsCache[key] = _cachedLyrics;
                _diagnostic?.Info($"Lyrics fetch succeeded: {songName} / {artist}, lines={_cachedLyrics.Count}");
            }
            else
            {
                _diagnostic?.Warn($"Lyrics fetch returned empty: {songName} / {artist}");
            }
        }
        finally
        {
            if (string.Equals(_inFlightKey, key, StringComparison.Ordinal))
            {
                _inFlightKey = null;
            }
        }
    }

    public void SetPlaybackPosition(double positionSeconds)
    {
        _currentPlaybackPositionSeconds = positionSeconds;
    }

    public string? UpdateCurrentLine()
    {
        if (_cachedLyrics is not { Count: > 0 })
        {
            return null;
        }

        string? line = GetLineAtTime(_cachedLyrics, _currentPlaybackPositionSeconds, ref _lastLineIndex);
        if (line != null && !string.Equals(line, _currentLine, StringComparison.Ordinal))
        {
            _currentLine = line;
            return line;
        }

        return null;
    }

    public string? GetCurrentLine()
    {
        if (_cachedLyrics is not { Count: > 0 })
        {
            return null;
        }

        return GetLineAtTime(_cachedLyrics, _currentPlaybackPositionSeconds, ref _lastLineIndex);
    }

    public void Reset()
    {
        _cachedLyrics = null;
        _lastLyricsKey = string.Empty;
        _inFlightKey = null;
        _lastFetchAttemptAtMs = 0;
        _currentLine = string.Empty;
        _currentPlaybackPositionSeconds = 0;
        _lastLineIndex = -1;
    }

    private static string BuildLyricsKey(string songName, string artist, string? albumTitle, double durationSeconds)
    {
        string normalizedSong = (songName ?? string.Empty).Trim();
        string normalizedArtist = (artist ?? string.Empty).Trim();
        string normalizedAlbum = (albumTitle ?? string.Empty).Trim();
        int roundedDuration = durationSeconds > 0 ? (int)Math.Round(durationSeconds, MidpointRounding.AwayFromZero) : 0;
        return $"{normalizedSong}|{normalizedArtist}|{normalizedAlbum}|{roundedDuration}";
    }

    internal static IReadOnlyList<string> BuildSearchQueries(string songName, string artist, string? albumTitle = null)
    {
        HashSet<string> queries = new(StringComparer.OrdinalIgnoreCase);
        string cleanedTitle = CleanupSearchText(songName);
        string cleanedArtist = CleanupSearchText(artist);
        string cleanedAlbum = CleanupSearchText(albumTitle);

        AddQuery(queries, songName, artist);
        AddQuery(queries, cleanedTitle, artist);
        AddQuery(queries, cleanedTitle, cleanedArtist);
        AddQuery(queries, songName, null);
        AddQuery(queries, cleanedTitle, null);
        if (!string.IsNullOrWhiteSpace(cleanedAlbum))
        {
            AddQuery(queries, $"{cleanedTitle} {cleanedAlbum}", artist);
            AddQuery(queries, cleanedTitle, cleanedAlbum);
        }

        return queries.ToArray();
    }

    internal static bool ShouldSkipLookup(string? songName, string? artist)
    {
        string title = (songName ?? string.Empty).Trim();
        string singer = (artist ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        if (title.Contains("姝ｅ湪杩炴帴", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("正在连接", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("鏈娴嬪埌", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("未检测到", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(singer, "Unknown Artist", StringComparison.OrdinalIgnoreCase) &&
            (title.Contains("姝ｅ湪杩炴帴", StringComparison.OrdinalIgnoreCase) ||
             title.Contains("正在连接", StringComparison.OrdinalIgnoreCase) ||
             title.Contains("鏈娴嬪埌", StringComparison.OrdinalIgnoreCase) ||
             title.Contains("未检测到", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    internal static string ExtractBestLyricFromPayload(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (TryReadNestedLyric(doc.RootElement, "lrc", out string? lrc))
        {
            return lrc;
        }

        if (TryReadNestedLyric(doc.RootElement, "yrc", out string? yrc))
        {
            return NormalizeDynamicLyric(yrc);
        }

        if (TryReadNestedLyric(doc.RootElement, "klyric", out string? klyric))
        {
            return NormalizeDynamicLyric(klyric);
        }

        if (TryReadNestedLyric(doc.RootElement, "tlyric", out string? tlyric))
        {
            return tlyric;
        }

        return string.Empty;
    }

    internal static IReadOnlyList<string> RankSongIdsFromSearchPayload(
        string json,
        string songName,
        string artist,
        string? albumTitle,
        double durationSeconds)
    {
        return RankSongCandidatesFromSearchPayload(json, songName, artist, albumTitle, durationSeconds)
            .Select(x => x.SongId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<SongSearchCandidate> RankSongCandidatesFromSearchPayload(
        string json,
        string songName,
        string artist,
        string? albumTitle,
        double durationSeconds)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out JsonElement result) ||
            !result.TryGetProperty("songs", out JsonElement songs) ||
            songs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SongSearchCandidate>();
        }

        List<SongSearchCandidate> ranked = new();
        foreach (JsonElement song in songs.EnumerateArray())
        {
            if (!song.TryGetProperty("id", out JsonElement id))
            {
                continue;
            }

            string candidateTitle = song.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            string candidateArtist = ReadCandidateArtist(song);
            string candidateAlbum = ReadCandidateAlbum(song);
            double candidateDuration = ReadCandidateDurationSeconds(song);
            double score = ScoreSearchCandidate(songName, artist, albumTitle, durationSeconds, candidateTitle, candidateArtist, candidateAlbum, candidateDuration);
            if (score >= MinimumAcceptedCandidateScore)
            {
                ranked.Add(new SongSearchCandidate(
                    id.GetInt64().ToString(),
                    score,
                    candidateTitle,
                    candidateArtist,
                    candidateAlbum,
                    candidateDuration));
            }
        }

        SongSearchCandidate[] ordered = ranked
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SongId, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length == 0)
        {
            return ordered;
        }

        if (!ShouldAcceptTopCandidate(ordered))
        {
            return Array.Empty<SongSearchCandidate>();
        }

        return ordered;
    }

    private async Task<List<(double Time, string Text)>?> FetchLyricsFromApiAsync(
        string songName,
        string artist,
        string? albumTitle,
        double durationSeconds,
        string? preferredSongId)
    {
        try
        {
            List<string> candidateSongIds = new();
            if (!string.IsNullOrWhiteSpace(preferredSongId))
            {
                candidateSongIds.Add(preferredSongId);
                _diagnostic?.Info($"Lyrics fetch preferred song id: {preferredSongId}");
            }
            _diagnostic?.Info($"Lyrics fetch start: {songName} / {artist}");

            IReadOnlyList<SongSearchCandidate> rankedCandidates = Array.Empty<SongSearchCandidate>();
            try
            {
                rankedCandidates = await _searchSongCandidatesAsync(songName, artist, albumTitle, durationSeconds);
            }
            catch (Exception ex)
            {
                _diagnostic?.Warn($"Lyrics fetch search failed: {songName} / {artist}, {ex.Message}");
            }

            foreach (SongSearchCandidate candidate in rankedCandidates)
            {
                string songId = candidate.SongId;
                if (!string.IsNullOrWhiteSpace(songId) &&
                    !candidateSongIds.Contains(songId, StringComparer.Ordinal))
                {
                    candidateSongIds.Add(songId);
                }
            }

            if (rankedCandidates.Count > 0)
            {
                _diagnostic?.Info(
                    "Lyrics fetch candidates: " +
                    string.Join(
                        " | ",
                        rankedCandidates.Select(x => $"{x.SongId}:{x.Title}/{x.Artist}/{x.Album}/{x.DurationSeconds:F1}s score={x.Score:F1}")));
            }
            else
            {
                _diagnostic?.Info("Lyrics fetch candidates: <none>");
            }

            foreach (string songId in candidateSongIds)
            {
                try
                {
                    string payload = await _fetchLyricPayloadAsync(songId);
                    string lyric = ExtractBestLyricFromPayload(payload);
                    if (!string.IsNullOrWhiteSpace(lyric))
                    {
                        _diagnostic?.Info($"Lyrics fetch hit lyric by songId: {songId}");
                        return ParseLrc(lyric);
                    }

                    _diagnostic?.Info($"Lyrics fetch candidate had no lyric: {songId}");
                }
                catch (Exception ex)
                {
                    _diagnostic?.Warn($"Lyrics fetch candidate failed: {songId}, {ex.Message}");
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _diagnostic?.Warn($"Lyrics fetch failed: {songName} / {artist}, {ex.Message}");
            return null;
        }
    }

    private static async Task<IReadOnlyList<SongSearchCandidate>> SearchSongCandidatesAsync(string songName, string artist, string? albumTitle, double durationSeconds)
    {
        Dictionary<string, SongSearchCandidate> candidates = new(StringComparer.Ordinal);

        foreach (string query in BuildSearchQueries(songName, artist, albumTitle))
        {
            try
            {
                string url = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(query)}&type=1&limit=3";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://music.163.com/");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                using HttpResponseMessage response = await HttpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                foreach (SongSearchCandidate candidate in RankSongCandidatesFromSearchPayload(json, songName, artist, albumTitle, durationSeconds))
                {
                    if (!candidates.TryGetValue(candidate.SongId, out SongSearchCandidate? existing) ||
                        candidate.Score > existing.Score)
                    {
                        candidates[candidate.SongId] = candidate;
                    }
                }
            }
            catch
            {
            }
        }

        return candidates.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SongId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CleanupSearchText(string? value)
    {
        string cleaned = QueryNoiseRegex.Replace(value ?? string.Empty, " ");
        cleaned = cleaned
            .Replace("\uFF08", "(")
            .Replace("\uFF09", ")")
            .Replace("\u3010", "[")
            .Replace("\u3011", "]")
            .Replace("\u3000", " ")
            .Replace("\u2013", "-")
            .Replace("\u2014", "-")
            .Replace("\u2018", "'")
            .Replace("\u2019", "'")
            .Replace("\u201C", "\"")
            .Replace("\u201D", "\"");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static void AddQuery(HashSet<string> queries, string? title, string? artist)
    {
        string normalizedTitle = string.IsNullOrWhiteSpace(artist) ? (title ?? string.Empty).Trim() : CleanupSearchText(title);
        string normalizedArtist = CleanupSearchText(artist);
        string query = string.IsNullOrWhiteSpace(normalizedArtist)
            ? normalizedTitle
            : $"{normalizedTitle} {normalizedArtist}".Trim();

        if (!string.IsNullOrWhiteSpace(query))
        {
            queries.Add(query);
        }
    }

    private static string ReadCandidateArtist(JsonElement song)
    {
        if (!song.TryGetProperty("artists", out JsonElement artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        List<string> names = new();
        foreach (JsonElement artist in artists.EnumerateArray())
        {
            if (artist.TryGetProperty("name", out JsonElement name))
            {
                string value = name.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value.Trim());
                }
            }
        }

        return string.Join(" / ", names);
    }

    private static string ReadCandidateAlbum(JsonElement song)
    {
        if (song.TryGetProperty("album", out JsonElement album) &&
            album.TryGetProperty("name", out JsonElement albumName))
        {
            return albumName.GetString() ?? string.Empty;
        }

        if (song.TryGetProperty("al", out JsonElement al) &&
            al.TryGetProperty("name", out JsonElement alName))
        {
            return alName.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static double ReadCandidateDurationSeconds(JsonElement song)
    {
        if (song.TryGetProperty("duration", out JsonElement duration) && duration.TryGetInt64(out long durationMs))
        {
            return durationMs / 1000.0;
        }

        if (song.TryGetProperty("dt", out JsonElement dt) && dt.TryGetInt64(out long dtMs))
        {
            return dtMs / 1000.0;
        }

        return 0;
    }

    private static double ScoreSearchCandidate(
        string inputTitle,
        string inputArtist,
        string? inputAlbumTitle,
        double inputDurationSeconds,
        string candidateTitle,
        string candidateArtist,
        string candidateAlbum,
        double candidateDurationSeconds)
    {
        string normalizedInputTitle = CleanupSearchText(inputTitle).Replace(" ", string.Empty);
        string normalizedCandidateTitle = CleanupSearchText(candidateTitle).Replace(" ", string.Empty);
        bool inputHasVersionNoise = ContainsVersionNoise(inputTitle) || ContainsVersionNoise(inputAlbumTitle);
        bool candidateHasVersionNoise = ContainsVersionNoise(candidateTitle) || ContainsVersionNoise(candidateAlbum);

        double score = 0;
        if (string.Equals(inputTitle.Trim(), candidateTitle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 70;
        }
        else if (string.Equals(normalizedInputTitle, normalizedCandidateTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 55;
        }
        else if (normalizedCandidateTitle.Contains(normalizedInputTitle, StringComparison.OrdinalIgnoreCase) ||
                 normalizedInputTitle.Contains(normalizedCandidateTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        string[] inputArtists = SplitArtists(inputArtist);
        string[] candidateArtists = SplitArtists(candidateArtist);
        int overlap = inputArtists.Intersect(candidateArtists, StringComparer.OrdinalIgnoreCase).Count();
        if (overlap > 0)
        {
            score += 20 + Math.Min(10, overlap * 5);
        }

        int extraArtists = candidateArtists.Except(inputArtists, StringComparer.OrdinalIgnoreCase).Count();
        int missingArtists = inputArtists.Except(candidateArtists, StringComparer.OrdinalIgnoreCase).Count();
        score -= extraArtists * 6;
        score -= missingArtists * 3;

        if (string.Equals(NormalizeArtistToken(inputArtist), NormalizeArtistToken(candidateArtist), StringComparison.OrdinalIgnoreCase))
        {
            score += 12;
        }

        if (!string.IsNullOrWhiteSpace(inputAlbumTitle))
        {
            string normalizedInputAlbum = CleanupSearchText(inputAlbumTitle).Replace(" ", string.Empty);
            string normalizedCandidateAlbum = CleanupSearchText(candidateAlbum).Replace(" ", string.Empty);
            if (!string.IsNullOrWhiteSpace(normalizedCandidateAlbum))
            {
                if (string.Equals(normalizedInputAlbum, normalizedCandidateAlbum, StringComparison.OrdinalIgnoreCase))
                {
                    score += 24;
                }
                else if (normalizedCandidateAlbum.Contains(normalizedInputAlbum, StringComparison.OrdinalIgnoreCase) ||
                         normalizedInputAlbum.Contains(normalizedCandidateAlbum, StringComparison.OrdinalIgnoreCase))
                {
                    score += 8;
                }
                else
                {
                    score -= 10;
                }
            }
        }

        if (inputDurationSeconds > 0 && candidateDurationSeconds > 0)
        {
            double delta = Math.Abs(candidateDurationSeconds - inputDurationSeconds);
            if (delta <= 2)
            {
                score += 20;
            }
            else if (delta <= 5)
            {
                score += 12;
            }
            else if (delta <= 10)
            {
                score += 4;
            }
            else if (delta >= 12)
            {
                score -= 16;
            }
        }

        if (!inputHasVersionNoise && candidateHasVersionNoise)
        {
            score -= 12;
        }

        return score;
    }

    private static bool ShouldAcceptTopCandidate(IReadOnlyList<SongSearchCandidate> ordered)
    {
        SongSearchCandidate top = ordered[0];
        if (top.Score < MinimumHighConfidenceCandidateScore)
        {
            return false;
        }

        if (ordered.Count == 1)
        {
            return true;
        }

        SongSearchCandidate second = ordered[1];
        if ((top.Score - second.Score) >= MinimumCandidateScoreGap)
        {
            return true;
        }

        return false;
    }

    private static bool ContainsVersionNoise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = CleanupSearchText(value);
        return normalized.Contains("live", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("remaster", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("deluxe", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("explicit", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("demo", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("version", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArtistToken(string artist)
    {
        return string.Join("|", SplitArtists(artist).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    private static string[] SplitArtists(string artist)
    {
        return Regex
            .Split(CleanupSearchText(artist ?? string.Empty), @"\s*(?:/|&|,|銆亅锛寍;|锛泑\bx\b|脳|feat\.?|ft\.?)\s*", RegexOptions.IgnoreCase)
            .Select(NormalizeArtistPart)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeArtistPart(string value)
    {
        string trimmed = value.Trim().Trim('.');
        return new string(trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    private static async Task<string> FetchLyricPayloadAsync(string songId)
    {
        string url = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://music.163.com/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using HttpResponseMessage response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static List<(double Time, string Text)> ParseLrc(string lrc)
    {
        List<(double Time, string Text)> lines = new();
        double offsetSeconds = 0;

        foreach (string line in lrc.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase))
            {
                int close = trimmed.IndexOf(']');
                if (close > 8)
                {
                    string offsetPart = trimmed.Substring(8, close - 8);
                    if (double.TryParse(
                            offsetPart,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double offsetMs))
                    {
                        offsetSeconds = offsetMs / 1000.0;
                    }
                }
                continue;
            }

            int closeBracket = trimmed.IndexOf(']');
            if (closeBracket < 0 || !trimmed.StartsWith('['))
            {
                continue;
            }

            string timePart = trimmed.Substring(1, closeBracket - 1);
            string textPart = trimmed[(closeBracket + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(textPart))
            {
                continue;
            }

            if (TryParseTime(timePart, out double time))
            {
                lines.Add((Math.Max(0, time + offsetSeconds), textPart));
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static string? GetLineAtTime(List<(double Time, string Text)> lyrics, double time, ref int lastIndex)
    {
        double effectiveTime = Math.Max(0, time - LineBoundaryStabilitySeconds);
        if (lyrics.Count == 0)
        {
            lastIndex = -1;
            return null;
        }

        if (lastIndex >= 0 && lastIndex < lyrics.Count)
        {
            double currentTime = lyrics[lastIndex].Time;
            double nextTime = lastIndex == lyrics.Count - 1 ? double.MaxValue : lyrics[lastIndex + 1].Time;
            if (effectiveTime >= currentTime && effectiveTime < nextTime)
            {
                return lyrics[lastIndex].Text;
            }

            if (effectiveTime < currentTime &&
                (currentTime - effectiveTime) <= BackwardLineSwitchToleranceSeconds)
            {
                return lyrics[lastIndex].Text;
            }

            if (effectiveTime >= nextTime)
            {
                for (int i = lastIndex + 1; i < lyrics.Count; i++)
                {
                    double candidateTime = lyrics[i].Time;
                    double candidateNextTime = i == lyrics.Count - 1 ? double.MaxValue : lyrics[i + 1].Time;
                    if (effectiveTime >= candidateTime && effectiveTime < candidateNextTime)
                    {
                        lastIndex = i;
                        return lyrics[i].Text;
                    }
                }
            }
        }

        int low = 0;
        int high = lyrics.Count - 1;
        int match = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (lyrics[mid].Time <= effectiveTime)
            {
                match = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        lastIndex = match;
        return match >= 0 ? lyrics[match].Text : null;
    }

    private static bool TryParseTime(string timeStr, out double seconds)
    {
        seconds = 0;
        string[] parts = timeStr.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int minutes))
        {
            return false;
        }

        if (!double.TryParse(
                parts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double secs))
        {
            return false;
        }

        seconds = minutes * 60 + secs;
        return true;
    }

    private static bool TryReadNestedLyric(JsonElement root, string sectionName, out string lyric)
    {
        lyric = string.Empty;
        if (!root.TryGetProperty(sectionName, out JsonElement section) ||
            !section.TryGetProperty("lyric", out JsonElement lyricElement))
        {
            return false;
        }

        lyric = lyricElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(lyric);
    }

    private static string NormalizeDynamicLyric(string lyric)
    {
        if (string.IsNullOrWhiteSpace(lyric))
        {
            return string.Empty;
        }

        List<string> normalizedLines = new();
        foreach (string rawLine in lyric.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Match lineMatch = Regex.Match(line, @"^\[(\d+),\d+\](.*)$");
            if (!lineMatch.Success)
            {
                normalizedLines.Add(Regex.Replace(line, @"\(\d+,\d+,\d+\)", string.Empty).Trim());
                continue;
            }

            if (!int.TryParse(lineMatch.Groups[1].Value, out int startMs))
            {
                continue;
            }

            string text = Regex.Replace(lineMatch.Groups[2].Value, @"\(\d+,\d+,\d+\)", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            normalizedLines.Add($"[{FormatMsAsLrcTime(startMs)}]{text}");
        }

        return string.Join("\n", normalizedLines);
    }

    private static string FormatMsAsLrcTime(int totalMs)
    {
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int centiseconds = (totalMs % 1000) / 10;
        return $"{minutes:00}:{seconds:00}.{centiseconds:00}";
    }

    internal sealed record SongSearchCandidate(
        string SongId,
        double Score,
        string Title,
        string Artist,
        string Album,
        double DurationSeconds);

    public void Dispose()
    {
        Reset();
    }
}
