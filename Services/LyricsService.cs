using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace HorizonRadioOverlay.Services;

public sealed class LyricsService : IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private string _lastLyricsKey = string.Empty;
    private List<(double Time, string Text)>? _cachedLyrics;
    private double _songStartTime;
    private double _songDuration;
    private string _currentLine = string.Empty;

    public bool HasLyrics => _cachedLyrics is { Count: > 0 };

    public async Task FetchLyricsAsync(string songName, string artist, double startTimeSeconds, double durationSeconds = 0)
    {
        string key = $"{songName}|{artist}";
        if (string.Equals(_lastLyricsKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _lastLyricsKey = key;
        _songStartTime = startTimeSeconds;
        _songDuration = durationSeconds;
        _currentLine = string.Empty;
        _cachedLyrics = await FetchLyricsFromApiAsync(songName, artist);
    }

    public void SetPlaybackPosition(double positionSeconds)
    {
        _songStartTime = Environment.TickCount / 1000.0 - positionSeconds;
    }

    public string? UpdateCurrentLine()
    {
        if (_cachedLyrics is not { Count: > 0 }) return null;

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
        if (_cachedLyrics is not { Count: > 0 }) return null;
        double elapsed = Environment.TickCount / 1000.0 - _songStartTime;
        return GetLineAtTime(_cachedLyrics, elapsed);
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

    private async Task<List<(double Time, string Text)>?> FetchLyricsFromApiAsync(string songName, string artist)
    {
        try
        {
            string? songId = await SearchSongIdAsync(songName, artist);
            if (songId == null) return null;

            string lrc = await FetchLrcAsync(songId);
            if (string.IsNullOrWhiteSpace(lrc)) return null;

            return ParseLrc(lrc);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> SearchSongIdAsync(string songName, string artist)
    {
        try
        {
            string query = $"{songName} {artist}";
            string url = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(query)}&type=1&limit=1";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://music.163.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("songs", out var songs) || songs.GetArrayLength() == 0) return null;

            var song = songs[0];
            if (song.TryGetProperty("id", out var id))
            {
                return id.GetInt64().ToString();
            }
        }
        catch { }

        return null;
    }

    private static async Task<string> FetchLrcAsync(string songId)
    {
        string url = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://music.163.com/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("lrc", out var lrc)) return string.Empty;
        if (!lrc.TryGetProperty("lyric", out var lyric)) return string.Empty;
        return lyric.GetString() ?? string.Empty;
    }

    private static List<(double Time, string Text)> ParseLrc(string lrc)
    {
        var lines = new List<(double Time, string Text)>();

        foreach (string line in lrc.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            int closeBracket = trimmed.IndexOf(']');
            if (closeBracket < 0 || !trimmed.StartsWith('[')) continue;

            string timePart = trimmed.Substring(1, closeBracket - 1);
            string textPart = trimmed.Substring(closeBracket + 1).Trim();

            if (string.IsNullOrWhiteSpace(textPart)) continue;

            if (TryParseTime(timePart, out double time))
            {
                lines.Add((time, textPart));
            }
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static bool TryParseTime(string timeStr, out double seconds)
    {
        seconds = 0;
        string[] parts = timeStr.Split(':');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], out int minutes)) return false;

        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double secs))
        {
            return false;
        }

        seconds = minutes * 60 + secs;
        return true;
    }

    public void Dispose()
    {
        _cachedLyrics = null;
        _lastLyricsKey = string.Empty;
        _currentLine = string.Empty;
    }
}
