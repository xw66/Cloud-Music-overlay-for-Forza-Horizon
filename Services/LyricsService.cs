using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HorizonRadioOverlay.Services;

public sealed class LyricsService : IDisposable
{
    private const long RetryAfterMissMs = 1500;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static readonly Regex QueryNoiseRegex = new(
        @"(\(.*?(live|\u4f34\u594f|dj|cover|vip|explicit|remaster|version|ver\.?|feat\.?|ft\.?|\u6bcd\u5e26|\u8d85\u54c1\u8d28|\u81fb\u54c1|\u675c\u6bd4|\u5168\u666f\u58f0).*?\))|(\[.*?(live|\u4f34\u594f|dj|cover|vip|explicit|remaster|version|ver\.?|feat\.?|ft\.?|\u6bcd\u5e26|\u8d85\u54c1\u8d28|\u81fb\u54c1|\u675c\u6bd4|\u5168\u666f\u58f0).*?\])|(\b(?:live|dj|cover|vip|explicit|remaster|version|ver|feat|ft)\.?)|(\u6bcd\u5e26)|(\u8d85\u54c1\u8d28)|(\u81fb\u54c1)|(\u675c\u6bd4\u5168\u666f\u58f0?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Func<string, string, Task<IReadOnlyList<string>>> _searchSongIdsAsync;
    private readonly Func<string, Task<string>> _fetchLyricPayloadAsync;
    private readonly Func<long> _nowProvider;
    private readonly DiagnosticService? _diagnostic;
    private string _lastLyricsKey = string.Empty;
    private string? _inFlightKey;
    private long _lastFetchAttemptAtMs;
    private List<(double Time, string Text)>? _cachedLyrics;
    private double _songStartTime;
    private double _songDuration;
    private string _currentLine = string.Empty;

    public bool HasLyrics => _cachedLyrics is { Count: > 0 };

    public LyricsService(DiagnosticService diagnostic)
    {
        _searchSongIdsAsync = SearchSongIdsAsync;
        _fetchLyricPayloadAsync = FetchLyricPayloadAsync;
        _nowProvider = () => Environment.TickCount64;
        _diagnostic = diagnostic;
    }

    internal LyricsService(
        Func<string, string, Task<IReadOnlyList<string>>> searchSongIdsAsync,
        Func<string, Task<string>> fetchLyricPayloadAsync,
        Func<long>? nowProvider = null)
    {
        _searchSongIdsAsync = searchSongIdsAsync;
        _fetchLyricPayloadAsync = fetchLyricPayloadAsync;
        _nowProvider = nowProvider ?? (() => Environment.TickCount64);
    }

    public async Task FetchLyricsAsync(string songName, string artist, double startTimeSeconds, double durationSeconds = 0)
    {
        string key = $"{songName}|{artist}";
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

        _lastLyricsKey = key;
        _songStartTime = startTimeSeconds;
        _songDuration = durationSeconds;
        _currentLine = string.Empty;
        _lastFetchAttemptAtMs = _nowProvider();
        _inFlightKey = key;
        try
        {
            _cachedLyrics = await FetchLyricsFromApiAsync(songName, artist);
            if (_cachedLyrics is { Count: > 0 })
            {
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
        _songStartTime = Environment.TickCount / 1000.0 - positionSeconds;
    }

    public string? UpdateCurrentLine()
    {
        if (_cachedLyrics is not { Count: > 0 })
        {
            return null;
        }

        double elapsed = Environment.TickCount / 1000.0 - _songStartTime;

        if (_songDuration > 0 && elapsed > _songDuration + 2)
        {
            elapsed = 0;
            _songStartTime = Environment.TickCount / 1000.0;
        }

        string? line = GetLineAtTime(_cachedLyrics, elapsed);
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

        double elapsed = Environment.TickCount / 1000.0 - _songStartTime;
        return GetLineAtTime(_cachedLyrics, elapsed);
    }

    public void Reset()
    {
        _cachedLyrics = null;
        _lastLyricsKey = string.Empty;
        _inFlightKey = null;
        _lastFetchAttemptAtMs = 0;
        _currentLine = string.Empty;
        _songStartTime = 0;
        _songDuration = 0;
    }

    internal static IReadOnlyList<string> BuildSearchQueries(string songName, string artist)
    {
        HashSet<string> queries = new(StringComparer.OrdinalIgnoreCase);
        string cleanedTitle = CleanupSearchText(songName);
        string cleanedArtist = CleanupSearchText(artist);

        AddQuery(queries, songName, artist);
        AddQuery(queries, cleanedTitle, artist);
        AddQuery(queries, cleanedTitle, cleanedArtist);
        AddQuery(queries, songName, null);
        AddQuery(queries, cleanedTitle, null);

        return queries.ToArray();
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

    private async Task<List<(double Time, string Text)>?> FetchLyricsFromApiAsync(string songName, string artist)
    {
        try
        {
            List<string> candidateSongIds = new();
            _diagnostic?.Info($"Lyrics fetch start: {songName} / {artist}");

            IReadOnlyList<string> fallbackSongIds = Array.Empty<string>();
            try
            {
                fallbackSongIds = await _searchSongIdsAsync(songName, artist);
            }
            catch (Exception ex)
            {
                _diagnostic?.Warn($"Lyrics fetch search failed: {songName} / {artist}, {ex.Message}");
            }

            foreach (string songId in fallbackSongIds)
            {
                if (!string.IsNullOrWhiteSpace(songId) &&
                    !candidateSongIds.Contains(songId, StringComparer.Ordinal))
                {
                    candidateSongIds.Add(songId);
                }
            }

            _diagnostic?.Info($"Lyrics fetch candidates: {string.Join(", ", candidateSongIds)}");

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

    private static async Task<IReadOnlyList<string>> SearchSongIdsAsync(string songName, string artist)
    {
        List<string> songIds = new();

        foreach (string query in BuildSearchQueries(songName, artist))
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

                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("result", out JsonElement result) ||
                    !result.TryGetProperty("songs", out JsonElement songs) ||
                    songs.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement song in songs.EnumerateArray())
                {
                    if (!song.TryGetProperty("id", out JsonElement id))
                    {
                        continue;
                    }

                    string candidate = id.GetInt64().ToString();
                    if (!songIds.Contains(candidate, StringComparer.Ordinal))
                    {
                        songIds.Add(candidate);
                    }
                }
            }
            catch
            {
            }
        }

        return songIds;
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

        foreach (string line in lrc.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
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
                lines.Add((time, textPart));
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static string? GetLineAtTime(List<(double Time, string Text)> lyrics, double time)
    {
        string? result = null;
        for (int i = 0; i < lyrics.Count; i++)
        {
            if (lyrics[i].Time <= time)
            {
                result = lyrics[i].Text;
            }
            else
            {
                break;
            }
        }

        return result;
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

    public void Dispose()
    {
        Reset();
    }
}
