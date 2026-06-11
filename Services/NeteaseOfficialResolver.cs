using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseOfficialResolver
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly Regex NoiseRegex = new(
        @"(\(.*?(live|\u4f34\u594f|dj|cover|vip|explicit|remaster|version|ver\.?|feat\.?|ft\.?|\u6bcd\u5e26|\u8d85\u54c1\u8d28|\u81fb\u54c1|\u675c\u6bd4|\u5168\u666f\u58f0).*?\))|(\[.*?(live|\u4f34\u594f|dj|cover|vip|explicit|remaster|version|ver\.?|feat\.?|ft\.?|\u6bcd\u5e26|\u8d85\u54c1\u8d28|\u81fb\u54c1|\u675c\u6bd4|\u5168\u666f\u58f0).*?\])|(\b(?:live|\u4f34\u594f|dj\u7248|cover|vip|explicit|remaster|version|ver|feat|ft)\.?)|(\u6bcd\u5e26)|(\u8d85\u54c1\u8d28)|(\u81fb\u54c1)|(\u675c\u6bd4\u5168\u666f\u58f0?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ArtistSplitRegex = new(
        @"\s*(?:/|&|,|\u3001|\uFF0C|;|\uFF1B|\bx\b|\u00D7|\bfeat\.?|\bft\.?)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, ResolvedSong> _songCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ResolvedSong> _trackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DiagnosticService _diagnostic;
    private readonly HttpClient _httpClient;

    public NeteaseOfficialResolver(DiagnosticService diagnostic)
        : this(diagnostic, SharedHttpClient)
    {
    }

    internal NeteaseOfficialResolver(DiagnosticService diagnostic, HttpClient httpClient)
    {
        _diagnostic = diagnostic;
        _httpClient = httpClient;
    }

    public async Task<ResolvedSong?> ResolveAsync(string title, string artist, string? preferredSongId = null, string? traceId = null)
    {
        traceId ??= DiagnosticContext.NewTraceId();
        string trackKey = BuildTrackCacheKey(title, artist);

        if (!string.IsNullOrWhiteSpace(preferredSongId))
        {
            if (_songCache.TryGetValue(preferredSongId, out ResolvedSong? cachedById))
            {
                if (ShouldAcceptPreferredIdResult(title, artist, cachedById))
                {
                    _trackCache[trackKey] = cachedById;
                    _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-resolver", "song-cache",
                        ("status", "hit"), ("songId", cachedById.SongId)));
                    return cachedById;
                }

                _diagnostic.Warn($"Netease resolver rejected cached preferred song id due to mismatch: {cachedById.SongId}");
            }

            ResolvedSong? byId = await TryResolveByIdAsync(preferredSongId, traceId, "id-http", "netease-id");
            if (byId != null)
            {
                CacheIfCoverAvailable(trackKey, byId);
                if (ShouldAcceptPreferredIdResult(title, artist, byId))
                {
                    return byId;
                }

                _diagnostic.Warn($"Netease resolver rejected preferred song id due to mismatch: {byId.SongId}");
            }
        }

        if (_trackCache.TryGetValue(trackKey, out ResolvedSong? cached))
        {
            _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-resolver", "track-cache",
                ("status", "hit"), ("songId", cached.SongId), ("trackKey", trackKey)));
            if (!string.IsNullOrWhiteSpace(cached.CoverUrl))
            {
                return cached;
            }

            _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-resolver", "track-cache",
                ("status", "ignored-no-cover"), ("rootCause", "cover-url-missing"),
                ("songId", cached.SongId), ("trackKey", trackKey),
                ("suggestion", DiagnosticContext.GetSuggestion("cover-url-missing"))));
        }

        ResolvedSong? resolved = await TryResolveBySearchAsync(title, artist, traceId);
        if (resolved != null)
        {
            CacheIfCoverAvailable(trackKey, resolved);
        }

        return resolved;
    }

    private void CacheIfCoverAvailable(string trackKey, ResolvedSong resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved.CoverUrl))
        {
            return;
        }

        _songCache[resolved.SongId] = resolved;
        _trackCache[trackKey] = resolved;
    }

    internal static bool ShouldAcceptPreferredIdResult(string inputTitle, string inputArtist, ResolvedSong candidate)
    {
        return ScoreCandidate(inputTitle, inputArtist, candidate.Title, candidate.Artist) >= 55;
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
        return ArtistSplitRegex
            .Split(artist ?? string.Empty)
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

    private async Task<ResolvedSong?> TryResolveByIdAsync(
        string songId,
        string traceId,
        string stage,
        string source)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string url = $"https://music.163.com/api/song/detail?ids=%5B{Uri.EscapeDataString(songId)}%5D";
            using HttpRequestMessage request = CreateRequest(url);
            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-resolver", stage,
                ("statusCode", (int)response.StatusCode),
                ("contentType", response.Content.Headers.ContentType?.MediaType),
                ("elapsedMs", stopwatch.ElapsedMilliseconds),
                ("songId", songId)));
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("songs", out JsonElement songs) ||
                songs.ValueKind != JsonValueKind.Array ||
                songs.GetArrayLength() == 0)
            {
                return null;
            }

            ResolvedSong? resolved = ParseResolvedSong(songs[0], 100, source);
            if (resolved != null)
            {
                _diagnostic.Info($"Netease resolver matched by id: {resolved.SongId}");
            }

            return resolved;
        }
        catch (Exception ex)
        {
            string rootCause = ex is HttpRequestException http && http.StatusCode.HasValue
                ? DiagnosticContext.ClassifyHttpStatus(http.StatusCode.Value)
                : DiagnosticContext.ClassifyException(ex);
            _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-resolver", stage,
                ("status", "failed"), ("rootCause", rootCause), ("elapsedMs", stopwatch.ElapsedMilliseconds),
                ("songId", songId), ("error", $"{ex.GetType().Name}: {ex.Message}"),
                ("suggestion", DiagnosticContext.GetSuggestion(rootCause))));
            return null;
        }
    }

    private async Task<ResolvedSong?> TryResolveBySearchAsync(string title, string artist, string traceId)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string query = $"{title} {artist}".Trim();
            string url = $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(query)}&type=1&limit=5";
            using HttpRequestMessage request = CreateRequest(url);
            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-resolver", "search-http",
                ("statusCode", (int)response.StatusCode),
                ("contentType", response.Content.Headers.ContentType?.MediaType),
                ("elapsedMs", stopwatch.ElapsedMilliseconds),
                ("query", query)));
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

            _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-resolver", "candidate-score",
                ("candidateCount", songs.GetArrayLength()), ("bestSongId", best?.SongId),
                ("bestScore", bestScore), ("secondScore", secondScore)));

            if (best == null)
            {
                return null;
            }

            if (bestScore >= 80 || (bestScore >= 55 && bestScore - secondScore >= 10))
            {
                if (string.IsNullOrWhiteSpace(best.CoverUrl))
                {
                    ResolvedSong? detail = await TryResolveByIdAsync(
                        best.SongId,
                        traceId,
                        "detail-http",
                        "netease-search-detail");
                    if (!string.IsNullOrWhiteSpace(detail?.CoverUrl))
                    {
                        _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-resolver", "detail-cover",
                            ("status", "filled"), ("songId", detail.SongId), ("coverUrl", detail.CoverUrl)));
                        best = MergeSearchMatchWithDetail(best, detail);
                    }
                    else
                    {
                        _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-resolver", "detail-cover",
                            ("status", "missing"), ("rootCause", "cover-url-missing"), ("songId", best.SongId),
                            ("suggestion", DiagnosticContext.GetSuggestion("cover-url-missing"))));
                    }
                }

                _diagnostic.Info($"Netease resolver matched by search: {best.SongId}, score={bestScore:0.##}");
                return best;
            }

            _diagnostic.Warn($"Netease resolver rejected low confidence search match: title={title}, artist={artist}, score={bestScore:0.##}");
            _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-resolver", "candidate-decision",
                ("status", "rejected"), ("rootCause", "resolve-low-confidence"),
                ("bestSongId", best.SongId), ("bestScore", bestScore), ("secondScore", secondScore),
                ("suggestion", DiagnosticContext.GetSuggestion("resolve-low-confidence"))));
            return null;
        }
        catch (Exception ex)
        {
            string rootCause = ex is HttpRequestException http && http.StatusCode.HasValue
                ? DiagnosticContext.ClassifyHttpStatus(http.StatusCode.Value)
                : DiagnosticContext.ClassifyException(ex);
            _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-resolver", "search-http",
                ("status", "failed"), ("rootCause", rootCause), ("elapsedMs", stopwatch.ElapsedMilliseconds),
                ("title", title), ("artist", artist), ("error", $"{ex.GetType().Name}: {ex.Message}"),
                ("suggestion", DiagnosticContext.GetSuggestion(rootCause))));
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

    private static ResolvedSong MergeSearchMatchWithDetail(ResolvedSong searchMatch, ResolvedSong detail)
    {
        return new ResolvedSong
        {
            SongId = searchMatch.SongId,
            AlbumId = detail.AlbumId ?? searchMatch.AlbumId,
            Title = string.IsNullOrWhiteSpace(detail.Title) ? searchMatch.Title : detail.Title,
            Artist = string.IsNullOrWhiteSpace(detail.Artist) ? searchMatch.Artist : detail.Artist,
            CoverUrl = detail.CoverUrl ?? searchMatch.CoverUrl,
            DurationSeconds = detail.DurationSeconds > 0 ? detail.DurationSeconds : searchMatch.DurationSeconds,
            Confidence = searchMatch.Confidence,
            ResolveSource = "netease-search-detail"
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
