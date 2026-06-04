using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseOfficialResolver
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly Regex NoiseRegex = new(
        @"(\(.*?(live|\u4f34\u594f|dj|cover).*?\))|(\[.*?(live|\u4f34\u594f|dj|cover).*?\])|(\b(live|\u4f34\u594f|dj\u7248|cover)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, ResolvedSong> _songCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ResolvedSong> _trackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DiagnosticService _diagnostic;

    public NeteaseOfficialResolver(DiagnosticService diagnostic)
    {
        _diagnostic = diagnostic;
    }

    public async Task<ResolvedSong?> ResolveAsync(string title, string artist, string? preferredSongId = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredSongId))
        {
            if (_songCache.TryGetValue(preferredSongId, out ResolvedSong? cachedById))
            {
                _trackCache[BuildTrackCacheKey(title, artist)] = cachedById;
                _diagnostic.Info($"Netease resolver song cache hit: {cachedById.SongId}");
                return cachedById;
            }

            ResolvedSong? byId = await TryResolveByIdAsync(preferredSongId);
            if (byId != null)
            {
                _songCache[byId.SongId] = byId;
                _trackCache[BuildTrackCacheKey(title, artist)] = byId;
                return byId;
            }
        }

        string trackKey = BuildTrackCacheKey(title, artist);
        if (_trackCache.TryGetValue(trackKey, out ResolvedSong? cached))
        {
            return cached;
        }

        ResolvedSong? resolved = await TryResolveBySearchAsync(title, artist);
        if (resolved != null)
        {
            _songCache[resolved.SongId] = resolved;
            _trackCache[trackKey] = resolved;
        }

        return resolved;
    }

    public static string BuildTrackCacheKey(string title, string artist)
    {
        return $"{NormalizeTitle(title)}|{NormalizeArtistToken(artist)}";
    }

    public static string NormalizeTitle(string title)
    {
        string normalized = NoiseRegex.Replace(title ?? string.Empty, " ");
        normalized = normalized.Replace("\uFF08", "(").Replace("\uFF09", ")")
            .Replace("\u3010", "[").Replace("\u3011", "]")
            .Trim()
            .ToLowerInvariant();
        normalized = new string(normalized.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return normalized;
    }

    public static string NormalizeArtistToken(string artist)
    {
        return string.Join("|", SplitArtists(artist).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    public static string[] SplitArtists(string artist)
    {
        return (artist ?? string.Empty)
            .Split(["/", "&", ",", "\u3001", "\uFF0C"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static double ScoreCandidate(string inputTitle, string inputArtist, string candidateTitle, string candidateArtist)
    {
        string normalizedInputTitle = NormalizeTitle(inputTitle);
        string normalizedCandidateTitle = NormalizeTitle(candidateTitle);

        double score = 0;
        if (string.Equals(inputTitle.Trim(), candidateTitle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 70;
        }
        else if (normalizedInputTitle == normalizedCandidateTitle)
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

        return score;
    }

    private async Task<ResolvedSong?> TryResolveByIdAsync(string songId)
    {
        try
        {
            string url = $"https://music.163.com/api/song/detail?ids=%5B{Uri.EscapeDataString(songId)}%5D";
            using HttpRequestMessage request = CreateRequest(url);
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("songs", out JsonElement songs) ||
                songs.ValueKind != JsonValueKind.Array ||
                songs.GetArrayLength() == 0)
            {
                return null;
            }

            ResolvedSong? resolved = ParseResolvedSong(songs[0], 100, "netease-id");
            if (resolved != null)
            {
                _diagnostic.Info($"Netease resolver matched by id: {resolved.SongId}");
            }

            return resolved;
        }
        catch (Exception ex)
        {
            _diagnostic.Warn($"Netease resolver id lookup failed: {songId}, {ex.Message}");
            return null;
        }
    }

    private async Task<ResolvedSong?> TryResolveBySearchAsync(string title, string artist)
    {
        try
        {
            string query = $"{title} {artist}".Trim();
            string url = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(query)}&type=1&limit=5";
            using HttpRequestMessage request = CreateRequest(url);
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out JsonElement result) ||
                !result.TryGetProperty("songs", out JsonElement songs) ||
                songs.ValueKind != JsonValueKind.Array ||
                songs.GetArrayLength() == 0)
            {
                return null;
            }

            ResolvedSong? best = null;
            double bestScore = double.MinValue;
            double secondScore = double.MinValue;

            foreach (JsonElement song in songs.EnumerateArray())
            {
                ResolvedSong? candidate = ParseResolvedSong(song, 0, "netease-search");
                if (candidate == null)
                {
                    continue;
                }

                double score = ScoreCandidate(title, artist, candidate.Title, candidate.Artist);
                candidate = new ResolvedSong
                {
                    SongId = candidate.SongId,
                    AlbumId = candidate.AlbumId,
                    Title = candidate.Title,
                    Artist = candidate.Artist,
                    CoverUrl = candidate.CoverUrl,
                    DurationSeconds = candidate.DurationSeconds,
                    Confidence = score,
                    ResolveSource = "netease-search"
                };

                if (score > bestScore)
                {
                    secondScore = bestScore;
                    bestScore = score;
                    best = candidate;
                }
                else if (score > secondScore)
                {
                    secondScore = score;
                }
            }

            if (best == null)
            {
                return null;
            }

            if (bestScore >= 80 || (bestScore >= 55 && bestScore - secondScore >= 10))
            {
                _diagnostic.Info($"Netease resolver matched by search: {best.SongId}, score={bestScore:0.##}");
                return best;
            }

            _diagnostic.Warn($"Netease resolver rejected low confidence search match: title={title}, artist={artist}, score={bestScore:0.##}");
            return null;
        }
        catch (Exception ex)
        {
            _diagnostic.Warn($"Netease resolver search failed: {title} / {artist}, {ex.Message}");
            return null;
        }
    }

    private static ResolvedSong? ParseResolvedSong(JsonElement song, double confidence, string source)
    {
        string? id = ReadNumberAsString(song, "id");
        string? title = ReadString(song, "name");
        string? artist = ReadArtists(song);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        string? albumId = null;
        string? coverUrl = null;
        if (song.TryGetProperty("album", out JsonElement album) || song.TryGetProperty("al", out album))
        {
            albumId = ReadNumberAsString(album, "id");
            coverUrl = ReadString(album, "picUrl") ?? ReadString(album, "cover");
        }

        double durationSeconds = 0;
        if (song.TryGetProperty("duration", out JsonElement durationEl) && durationEl.ValueKind == JsonValueKind.Number)
        {
            durationSeconds = durationEl.GetDouble() / 1000.0;
        }
        else if (song.TryGetProperty("dt", out JsonElement dtEl) && dtEl.ValueKind == JsonValueKind.Number)
        {
            durationSeconds = dtEl.GetDouble() / 1000.0;
        }

        return new ResolvedSong
        {
            SongId = id,
            AlbumId = albumId,
            Title = title,
            Artist = artist,
            CoverUrl = coverUrl,
            DurationSeconds = durationSeconds,
            Confidence = confidence,
            ResolveSource = source
        };
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://music.163.com/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        return request;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement target) || target.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return target.GetString();
    }

    private static string? ReadNumberAsString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement target))
        {
            return null;
        }

        return target.ValueKind switch
        {
            JsonValueKind.Number => target.GetInt64().ToString(),
            JsonValueKind.String => target.GetString(),
            _ => null
        };
    }

    private static string? ReadArtists(JsonElement trackElement)
    {
        if (trackElement.TryGetProperty("artists", out JsonElement artists) || trackElement.TryGetProperty("ar", out artists))
        {
            if (artists.ValueKind == JsonValueKind.Array)
            {
                string[] names = artists
                    .EnumerateArray()
                    .Select(x => ReadString(x, "name"))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .ToArray();

                if (names.Length > 0)
                {
                    return string.Join(" / ", names);
                }
            }
        }

        return null;
    }
}
