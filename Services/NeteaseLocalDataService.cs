using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseLocalDataService
{
    internal readonly record struct LocalSongIdHint(string SongId, string Source);
    private readonly record struct CoverDownloadResult(byte[]? Bytes, string RootCause, int? StatusCode, string? ContentType);

    private readonly CoverCacheService _coverCache;
    private readonly DiagnosticService _diagnostic;
    private readonly NeteaseOfficialResolver _officialResolver;
    private long _lastWindowTitleFailureLogAt;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly string[] LocalDataDirs = [
        "Netease\\CloudMusic",
        "Netease\\cloudmusic",
        "NetEase Music"
    ];

    public NeteaseLocalDataService(CoverCacheService coverCache, DiagnosticService diagnostic, NeteaseOfficialResolver? resolver = null)
    {
        _coverCache = coverCache;
        _diagnostic = diagnostic;
        _officialResolver = resolver ?? new NeteaseOfficialResolver(diagnostic);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public async Task<TrackInfo?> GetCurrentTrackAsync()
    {
        string traceId = DiagnosticContext.NewTraceId();
        Stopwatch pipelineStopwatch = Stopwatch.StartNew();
        _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "start", ("status", "started")));

        TrackInfo? liveTrack = GetTrackFromRunningProcess(_diagnostic, traceId);
        if (liveTrack == null)
        {
            long now = Environment.TickCount64;
            if (now - _lastWindowTitleFailureLogAt >= 5000)
            {
                _lastWindowTitleFailureLogAt = now;
                _diagnostic.Warn(
                    DiagnosticContext.Format(traceId, "netease-cover", "result",
                        ("status", NeteaseCoverDiagnosticPolicy.WindowTitleMissing),
                        ("rootCause", NeteaseCoverDiagnosticPolicy.WindowTitleMissing),
                        ("elapsedMs", pipelineStopwatch.ElapsedMilliseconds),
                        ("suggestion", "确认网易云正在运行且主窗口标题包含歌曲名和歌手")));
            }

            return null;
        }

        LocalSongIdHint? localHint = await TryGetSongIdHintAsync(liveTrack.Name, liveTrack.Artist, traceId);
        string? preferredSongId = localHint?.SongId;
        if (!string.IsNullOrWhiteSpace(preferredSongId))
        {
            _diagnostic.Info($"CloudMusic local song id hint matched: {preferredSongId}, source={localHint?.Source} ({liveTrack.Name} / {liveTrack.Artist})");
        }
        else
        {
            _diagnostic.Warn($"CloudMusic local song id hint missing, falling back to official search: {liveTrack.Name} / {liveTrack.Artist}");
        }

        ResolvedSong? resolved = await _officialResolver.ResolveAsync(liveTrack.Name, liveTrack.Artist, preferredSongId, traceId);
        bool usedPreferredSongId = !string.IsNullOrWhiteSpace(preferredSongId) &&
            string.Equals(resolved?.ResolveSource, "netease-id", StringComparison.Ordinal);

        if (usedPreferredSongId && resolved != null && !ShouldTrustPreferredSongId(liveTrack, resolved))
        {
            _diagnostic.Warn(
                $"CloudMusic local song id hint mismatched process title, falling back to search: " +
                $"title={liveTrack.Name} / {liveTrack.Artist}, " +
                $"hint={preferredSongId}({localHint?.Source}), " +
                $"resolved={resolved.Title} / {resolved.Artist}");
            resolved = await _officialResolver.ResolveAsync(liveTrack.Name, liveTrack.Artist, traceId: traceId);
            usedPreferredSongId = false;
        }

        string coverKey = !string.IsNullOrWhiteSpace(resolved?.SongId)
            ? $"netease-song:{resolved.SongId}"
            : $"{liveTrack.Name}|{liveTrack.Artist}";
        byte[]? coverBytes = _coverCache.TryGet(coverKey, traceId);
        string coverSource;
        string rootCause = "none";

        if (resolved != null)
        {
            _diagnostic.Info($"CloudMusic official resolve result: songId={resolved.SongId}, source={resolved.ResolveSource}, confidence={resolved.Confidence:0.##}");
        }
        else
        {
            _diagnostic.Warn($"CloudMusic official resolve failed: {liveTrack.Name} / {liveTrack.Artist}");
        }

        if (coverBytes != null)
        {
            coverSource = NeteaseCoverDiagnosticPolicy.CacheHit;
            _diagnostic.Info($"CloudMusic cover cache hit: key={coverKey}");
        }
        else if (resolved == null)
        {
            coverSource = NeteaseCoverDiagnosticPolicy.ResolveFailed;
            rootCause = NeteaseCoverDiagnosticPolicy.ResolveFailed;
        }
        else if (string.IsNullOrWhiteSpace(resolved.CoverUrl))
        {
            coverSource = NeteaseCoverDiagnosticPolicy.CoverUrlMissing;
            rootCause = NeteaseCoverDiagnosticPolicy.CoverUrlMissing;
            _diagnostic.Warn($"CloudMusic official result has no cover URL: songId={resolved.SongId}, title={resolved.Title} / {resolved.Artist}");
        }
        else
        {
            CoverDownloadResult download = await DownloadCoverBytesAsync(resolved.CoverUrl, traceId);
            coverBytes = download.Bytes;
            if (coverBytes != null)
            {
                coverSource = NeteaseCoverDiagnosticPolicy.Downloaded;
                _coverCache.Set(coverKey, coverBytes, traceId);
                _diagnostic.Info($"CloudMusic cover downloaded and cached: songId={resolved.SongId}");
            }
            else
            {
                coverSource = NeteaseCoverDiagnosticPolicy.DownloadFailed;
                rootCause = download.RootCause;
                _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-cover", "download-result",
                    ("status", "failed"), ("rootCause", download.RootCause), ("statusCode", download.StatusCode),
                    ("contentType", download.ContentType), ("songId", resolved.SongId), ("url", resolved.CoverUrl),
                    ("suggestion", DiagnosticContext.GetSuggestion(download.RootCause))));
            }
        }

        string baseSourceAppId = resolved == null
            ? "CloudMusic(ProcessTitle)"
            : (usedPreferredSongId
                ? $"CloudMusic(OfficialById:{preferredSongId},{localHint?.Source})"
                : "CloudMusic(OfficialSearch)");
        string sourceAppId = NeteaseCoverDiagnosticPolicy.FormatSourceAppId(baseSourceAppId, coverSource);
        string pipelineSummary =
            DiagnosticContext.Format(traceId, "netease-cover", "result",
                ("status", coverSource),
                ("rootCause", rootCause),
                ("elapsedMs", pipelineStopwatch.ElapsedMilliseconds),
                ("title", liveTrack.Name), ("artist", liveTrack.Artist),
                ("songId", resolved?.SongId ?? preferredSongId), ("resolve", resolved?.ResolveSource),
                ("localHint", localHint?.Source), ("coverUrl", resolved?.CoverUrl),
                ("bytes", coverBytes?.Length ?? 0),
                ("suggestion", rootCause != "none"
                    ? DiagnosticContext.GetSuggestion(rootCause)
                    : "none"));
        if (NeteaseCoverDiagnosticPolicy.GetFailureReason(coverSource) != null)
        {
            _diagnostic.Warn(pipelineSummary);
        }
        else
        {
            _diagnostic.Info(pipelineSummary);
        }

        return new TrackInfo
        {
            Name = resolved?.Title ?? liveTrack.Name,
            Artist = resolved?.Artist ?? liveTrack.Artist,
            SourceAppId = sourceAppId,
            SongId = resolved?.SongId ?? (usedPreferredSongId ? preferredSongId : null),
            CoverBytes = coverBytes,
            DurationSeconds = resolved?.DurationSeconds ?? 0,
            CoverSource = coverSource
        };
    }

    internal static bool ShouldTrustPreferredSongId(TrackInfo liveTrack, ResolvedSong resolved)
    {
        double score = NeteaseOfficialResolver.ScoreCandidate(
            liveTrack.Name,
            liveTrack.Artist,
            resolved.Title,
            resolved.Artist);

        return score >= 55;
    }

    private static TrackInfo? GetTrackFromRunningProcess(DiagnosticService? diagnostic, string traceId)
    {
        var processes = Process.GetProcessesByName("cloudmusic");
        diagnostic?.Info(DiagnosticContext.Format(traceId, "netease-cover", "process-detect",
            ("processCount", processes.Length), ("pids", string.Join(",", processes.Select(x => x.Id)))));
        if (processes.Length == 0)
        {
            return null;
        }

        HashSet<uint> cloudmusicPids = new(processes.Select(p => (uint)p.Id));
        List<(string Title, uint Pid, bool IsVisible)> windows = new();

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!cloudmusicPids.Contains(pid))
            {
                return true;
            }

            int len = GetWindowTextLength(hWnd);
            if (len <= 0)
            {
                return true;
            }

            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString().Trim();

            if (!string.IsNullOrEmpty(title))
            {
                windows.Add((title, pid, IsWindowVisible(hWnd)));
            }

            return true;
        }, IntPtr.Zero);

        string? bestTitle = NeteaseWindowTitlePolicy.SelectBestTitle(
            windows.Select(x => new NeteaseWindowCandidate(x.Title, x.IsVisible)));
        diagnostic?.Info(DiagnosticContext.Format(traceId, "netease-cover", "window-title",
            ("windowCount", windows.Count), ("selected", bestTitle),
            ("candidates", string.Join(" | ", windows.Select(x => $"[{(x.IsVisible ? "visible" : "hidden")}]{x.Title}")))));

        if (string.IsNullOrWhiteSpace(bestTitle))
        {
            if (windows.Count > 0)
            {
                diagnostic?.Warn($"CloudMusic title detection skipped invalid titles: {string.Join(" | ", windows.Select(x => $"[{(x.IsVisible ? "visible" : "hidden")}]{x.Title}"))}");
            }

            return null;
        }

        ParseTitleArtist(bestTitle, out string songName, out string artistName);
        diagnostic?.Info(DiagnosticContext.Format(traceId, "netease-cover", "title-parse",
            ("raw", bestTitle), ("title", songName), ("artist", artistName),
            ("status", artistName == "Unknown Artist" ? "artist-missing" : "ok")));

        return new TrackInfo
        {
            Name = songName,
            Artist = artistName,
            SourceAppId = "CloudMusic(ProcessTitle)",
            CoverBytes = null
        };
    }

    private static void ParseTitleArtist(string titleRaw, out string songName, out string artistName)
    {
        int splitIndex = titleRaw.LastIndexOf(" - ", StringComparison.Ordinal);
        if (splitIndex > 0 && splitIndex < titleRaw.Length - 3)
        {
            songName = titleRaw[..splitIndex].Trim();
            artistName = titleRaw[(splitIndex + 3)..].Trim();
            if (!string.IsNullOrWhiteSpace(songName) && !string.IsNullOrWhiteSpace(artistName))
            {
                return;
            }
        }

        songName = titleRaw;
        artistName = "Unknown Artist";
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement target))
        {
            return null;
        }

        if (target.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return target.GetString();
    }

    private static string? ReadArtists(JsonElement trackElement)
    {
        if (!trackElement.TryGetProperty("artists", out JsonElement artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = artists
            .EnumerateArray()
            .Select(x => ReadString(x, "name"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();

        if (names.Length == 0)
        {
            return null;
        }

        return string.Join(" / ", names);
    }

    private static string? ReadCoverUrl(JsonElement trackElement)
    {
        if (trackElement.TryGetProperty("album", out JsonElement album))
        {
            string? url = ReadString(album, "picUrl") ?? ReadString(album, "cover");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        if (trackElement.TryGetProperty("al", out JsonElement shortAlbum))
        {
            string? url = ReadString(shortAlbum, "picUrl") ?? ReadString(shortAlbum, "cover");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private async Task<LocalSongIdHint?> TryGetSongIdHintAsync(string name, string artist, string traceId)
    {
        List<string> dataDirs = FindAllNeteaseDataDirs();
        _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "local-data-dirs",
            ("count", dataDirs.Count), ("paths", string.Join(" | ", dataDirs))));
        foreach (string dataDir in dataDirs)
        {
            string playingList = Path.Combine(dataDir, "webdata", "file", "playingList");
            LocalSongIdHint? result = await TryGetSongIdFromFileAsync(playingList, name, artist, hasTrackWrapper: true, traceId);
            if (result.HasValue) return result;

            string fmPlay = Path.Combine(dataDir, "webdata", "file", "fmPlay");
            result = await TryGetSongIdFromFileAsync(fmPlay, name, artist, hasTrackWrapper: false, traceId);
            if (result.HasValue) return result;
        }

        return null;
    }

    private static List<string> FindAllNeteaseDataDirs()
    {
        var dirs = new List<string>();

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string sub in LocalDataDirs)
        {
            string path = Path.Combine(localAppData, sub);
            if (Directory.Exists(path)) dirs.Add(path);
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (string sub in LocalDataDirs)
        {
            string path = Path.Combine(programData, sub);
            if (Directory.Exists(path)) dirs.Add(path);
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appDataRoaming = Path.Combine(userProfile, "AppData", "Roaming");
        foreach (string sub in LocalDataDirs)
        {
            string path = Path.Combine(appDataRoaming, sub);
            if (Directory.Exists(path)) dirs.Add(path);
        }

        return dirs;
    }

    private async Task<LocalSongIdHint?> TryGetSongIdFromFileAsync(string filePath, string name, string artist, bool hasTrackWrapper, string traceId)
    {
        if (!File.Exists(filePath))
        {
            _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "local-file",
                ("status", "missing"), ("path", filePath)));
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            string rootCause = DiagnosticContext.ClassifyException(ex);
            _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-cover", "local-file",
                ("status", "read-failed"), ("rootCause", rootCause), ("path", filePath),
                ("error", $"{ex.GetType().Name}: {ex.Message}"), ("suggestion", DiagnosticContext.GetSuggestion(rootCause))));
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "local-file",
                ("status", "empty"), ("path", filePath)));
            return null;
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            string rootCause = DiagnosticContext.ClassifyException(ex);
            _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-cover", "local-file",
                ("status", "parse-failed"), ("rootCause", rootCause), ("path", filePath),
                ("bytes", json.Length), ("error", $"{ex.GetType().Name}: {ex.Message}"),
                ("suggestion", DiagnosticContext.GetSuggestion(rootCause))));
            return null;
        }

        LocalSongIdHint? hint = ParseSongIdHintFromJson(json, name, artist, hasTrackWrapper, Path.GetFileName(filePath));
        _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "local-file",
            ("status", hint.HasValue ? "matched" : "no-match"), ("path", filePath),
            ("bytes", json.Length), ("songId", hint?.SongId), ("source", hint?.Source)));
        return hint;
    }

    internal static LocalSongIdHint? ParseSongIdHintFromJson(string json, string name, string artist, bool hasTrackWrapper, string fileLabel = "unknown")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement list;
            if (!doc.RootElement.TryGetProperty("list", out list) || list.ValueKind != JsonValueKind.Array)
            {
                if (!doc.RootElement.TryGetProperty("queue", out list) || list.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }
            }

            string? rootSongIdHint = TryGetRootSongIdHint(doc.RootElement);
            int? rootIndexHint = TryGetRootIndexHint(doc.RootElement);

            if (!string.IsNullOrWhiteSpace(rootSongIdHint))
            {
                return new LocalSongIdHint(rootSongIdHint, $"{fileLabel}:root-id");
            }

            if (rootIndexHint.HasValue)
            {
                string? indexedSongId = TryGetIndexedSongId(list, rootIndexHint.Value, hasTrackWrapper);
                if (!string.IsNullOrWhiteSpace(indexedSongId))
                {
                    return new LocalSongIdHint(indexedSongId, $"{fileLabel}:current-index");
                }
            }

            List<NeteaseLocalTrackCandidate> candidates = new();
            int index = 0;

            foreach (JsonElement item in list.EnumerateArray())
            {
                JsonElement trackElement = item;
                if (hasTrackWrapper && item.TryGetProperty("track", out JsonElement trackObj))
                {
                    trackElement = trackObj;
                }

                string? itemName = ReadString(trackElement, "name");
                string? itemArtist = ReadArtists(trackElement);
                string? songId = ReadNumberAsString(trackElement, "id");
                if (!string.IsNullOrWhiteSpace(songId) &&
                    !string.IsNullOrWhiteSpace(itemName) &&
                    !string.IsNullOrWhiteSpace(itemArtist))
                {
                    candidates.Add(new NeteaseLocalTrackCandidate(
                        songId,
                        itemName,
                        itemArtist,
                        (rootIndexHint.HasValue && rootIndexHint.Value == index) ||
                        HasCurrentTrackHint(item) ||
                        HasCurrentTrackHint(trackElement)));
                }

                index++;
            }

            string? matchedSongId = NeteaseLocalTrackMatchPolicy.SelectSongId(candidates, name, artist);
            return !string.IsNullOrWhiteSpace(matchedSongId)
                ? new LocalSongIdHint(matchedSongId, $"{fileLabel}:scored-match")
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetIndexedSongId(JsonElement list, int index, bool hasTrackWrapper)
    {
        if (index < 0 || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int current = 0;
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (current == index)
            {
                JsonElement trackElement = item;
                if (hasTrackWrapper && item.TryGetProperty("track", out JsonElement trackObj))
                {
                    trackElement = trackObj;
                }

                return ReadNumberAsString(trackElement, "id")
                    ?? ReadNumberAsString(item, "id")
                    ?? ReadNumberAsString(item, "trackId")
                    ?? ReadNumberAsString(item, "resourceId");
            }

            current++;
        }

        return null;
    }

    private static bool HasCurrentTrackHint(JsonElement element)
    {
        foreach (string propertyName in new[] { "current", "isCurrent", "playing", "isPlaying", "selected" })
        {
            if (element.TryGetProperty(propertyName, out JsonElement boolEl) &&
                (boolEl.ValueKind == JsonValueKind.True || boolEl.ValueKind == JsonValueKind.False))
            {
                return boolEl.GetBoolean();
            }
        }

        foreach (string propertyName in new[] { "playState", "state", "status" })
        {
            if (element.TryGetProperty(propertyName, out JsonElement stateEl) &&
                stateEl.ValueKind == JsonValueKind.String)
            {
                string? state = stateEl.GetString();
                if (!string.IsNullOrWhiteSpace(state) &&
                    (state.Contains("play", StringComparison.OrdinalIgnoreCase) ||
                     state.Contains("current", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? TryGetRootSongIdHint(JsonElement root)
    {
        foreach (string propertyName in new[] { "currentSongId", "songId", "trackId", "currentTrackId", "playingTrackId", "resourceId" })
        {
            string? value = ReadNumberAsString(root, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        foreach (string propertyName in new[] { "currentTrack", "playingTrack", "track" })
        {
            if (root.TryGetProperty(propertyName, out JsonElement nested))
            {
                string? value = ReadNumberAsString(nested, "id")
                    ?? ReadNumberAsString(nested, "songId")
                    ?? ReadNumberAsString(nested, "trackId");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static int? TryGetRootIndexHint(JsonElement root)
    {
        foreach (string propertyName in new[] { "currentIndex", "playingIndex", "index", "playIndex" })
        {
            if (root.TryGetProperty(propertyName, out JsonElement target) &&
                target.ValueKind == JsonValueKind.Number &&
                target.TryGetInt32(out int index))
            {
                return index;
            }
        }

        return null;
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

    private async Task<CoverDownloadResult> DownloadCoverBytesAsync(string? url, string traceId)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new CoverDownloadResult(null, NeteaseCoverDiagnosticPolicy.CoverUrlMissing, null, null);
        }

        url = EnsureHttps(url);

        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://music.163.com/");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                using var response = await HttpClient.SendAsync(request);
                string? contentType = response.Content.Headers.ContentType?.MediaType;
                _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "cover-http",
                    ("attempt", $"{attempt + 1}/{maxRetries + 1}"), ("statusCode", (int)response.StatusCode),
                    ("contentType", contentType), ("elapsedMs", stopwatch.ElapsedMilliseconds), ("url", url)));

                if (!response.IsSuccessStatusCode)
                {
                    string rootCause = DiagnosticContext.ClassifyHttpStatus(response.StatusCode);
                    _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-cover", "cover-http",
                        ("status", "failed"), ("rootCause", rootCause), ("attempt", $"{attempt + 1}/{maxRetries + 1}"),
                        ("statusCode", (int)response.StatusCode), ("url", url),
                        ("suggestion", DiagnosticContext.GetSuggestion(rootCause))));
                    if (attempt >= maxRetries)
                    {
                        return new CoverDownloadResult(null, rootCause, (int)response.StatusCode, contentType);
                    }

                    await Task.Delay(300 * (1 << attempt));
                    continue;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                bool image = DiagnosticContext.IsLikelyImage(bytes);
                _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", "image-validate",
                    ("status", image ? "ok" : "invalid"), ("bytes", bytes.Length),
                    ("contentType", contentType), ("url", url)));

                if (!image)
                {
                    return new CoverDownloadResult(null, "invalid-image", (int)response.StatusCode, contentType);
                }

                return new CoverDownloadResult(bytes, "none", (int)response.StatusCode, contentType);
            }
            catch (Exception ex)
            {
                string rootCause = DiagnosticContext.ClassifyException(ex);
                _diagnostic.Warn(DiagnosticContext.Format(traceId, "netease-cover", "cover-http",
                    ("status", "failed"), ("rootCause", rootCause), ("attempt", $"{attempt + 1}/{maxRetries + 1}"),
                    ("elapsedMs", stopwatch.ElapsedMilliseconds), ("url", url),
                    ("error", $"{ex.GetType().Name}: {ex.Message}"), ("suggestion", DiagnosticContext.GetSuggestion(rootCause))));
                if (attempt < maxRetries)
                {
                    await Task.Delay(300 * (1 << attempt));
                    continue;
                }

                return new CoverDownloadResult(null, rootCause, null, null);
            }
        }

        return new CoverDownloadResult(null, "unknown-error", null, null);
    }

    private static string EnsureHttps(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url.Substring(7);
        }
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && url.Contains("://"))
        {
            return "https://" + url;
        }
        return url;
    }
}
