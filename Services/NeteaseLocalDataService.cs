using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseLocalDataService
{
    private readonly CoverCacheService _coverCache;
    private readonly DiagnosticService _diagnostic;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly string[] SearchApiUrls = [
        "https://music.163.com/api/search/get?s={0}&type=1&limit=5",
        "https://music.163.com/api/cloudsearch/pc?s={0}&type=1&limit=5"
    ];

    private static readonly string[] LocalDataDirs = [
        "Netease\\CloudMusic",
        "Netease\\cloudmusic",
        "NetEase Music"
    ];

    public NeteaseLocalDataService(CoverCacheService coverCache, DiagnosticService diagnostic)
    {
        _coverCache = coverCache;
        _diagnostic = diagnostic;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public async Task<TrackInfo?> GetCurrentTrackAsync()
    {
        TrackInfo? liveTrack = GetTrackFromRunningProcess();
        if (liveTrack == null)
        {
            return null;
        }

        string key = $"{liveTrack.Name}|{liveTrack.Artist}";
        byte[]? coverBytes = _coverCache.TryGet(key);

        if (coverBytes == null)
        {
            coverBytes = await TryGetCoverBytesFromPlayingListAsync(liveTrack.Name, liveTrack.Artist);
            if (coverBytes != null)
            {
                _coverCache.Set(key, coverBytes);
            }
        }

        double duration = await GetDurationAsync(liveTrack.Name, liveTrack.Artist);

        return new TrackInfo
        {
            Name = liveTrack.Name,
            Artist = liveTrack.Artist,
            SourceAppId = coverBytes == null ? "CloudMusic(ProcessTitle)" : "CloudMusic(ProcessTitle+Cover)",
            CoverBytes = coverBytes,
            DurationSeconds = duration
        };
    }

    private static TrackInfo? GetTrackFromRunningProcess()
    {
        var processes = Process.GetProcessesByName("cloudmusic");
        if (processes.Length == 0)
        {
            return null;
        }

        HashSet<uint> cloudmusicPids = new(processes.Select(p => (uint)p.Id));
        List<(string Title, uint Pid)> windows = new();

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
                windows.Add((title, pid));
            }

            return true;
        }, IntPtr.Zero);

        string? bestTitle = null;

        foreach (var (title, _) in windows)
        {
            if (title.Contains(" - ", StringComparison.Ordinal))
            {
                int idx = title.LastIndexOf(" - ", StringComparison.Ordinal);
                string songPart = title[..idx].Trim();
                string artistPart = title[(idx + 3)..].Trim();

                if (!string.IsNullOrWhiteSpace(songPart) && !string.IsNullOrWhiteSpace(artistPart))
                {
                    bestTitle = title;
                    break;
                }
            }
        }

        if (bestTitle == null && windows.Count > 0)
        {
            bestTitle = windows[0].Title;
        }

        if (string.IsNullOrWhiteSpace(bestTitle))
        {
            return null;
        }

        ParseTitleArtist(bestTitle, out string songName, out string artistName);

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

    private async Task<byte[]?> TryGetCoverBytesFromPlayingListAsync(string name, string artist)
    {
        foreach (string dataDir in FindAllNeteaseDataDirs())
        {
            string playingList = Path.Combine(dataDir, "webdata", "file", "playingList");
            byte[]? result = await TryMatchFromFileAsync(playingList, name, artist, hasTrackWrapper: true);
            if (result != null) return result;

            string fmPlay = Path.Combine(dataDir, "webdata", "file", "fmPlay");
            result = await TryMatchFromFileAsync(fmPlay, name, artist, hasTrackWrapper: false);
            if (result != null) return result;
        }

        return await TrySearchCoverFromApiAsync(name, artist);
    }

    public async Task<double> GetDurationAsync(string name, string artist)
    {
        foreach (string dataDir in FindAllNeteaseDataDirs())
        {
            string playingList = Path.Combine(dataDir, "webdata", "file", "playingList");
            double duration = await TryGetDurationFromFileAsync(playingList, name, artist, hasTrackWrapper: true);
            if (duration > 0) return duration;

            string fmPlay = Path.Combine(dataDir, "webdata", "file", "fmPlay");
            duration = await TryGetDurationFromFileAsync(fmPlay, name, artist, hasTrackWrapper: false);
            if (duration > 0) return duration;
        }

        return 0;
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

    private static async Task<double> TryGetDurationFromFileAsync(string filePath, string name, string artist, bool hasTrackWrapper)
    {
        if (!File.Exists(filePath)) return 0;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath);
        }
        catch
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(json)) return 0;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement list;
            if (!doc.RootElement.TryGetProperty("list", out list) || list.ValueKind != JsonValueKind.Array)
            {
                if (!doc.RootElement.TryGetProperty("queue", out list) || list.ValueKind != JsonValueKind.Array)
                {
                    return 0;
                }
            }

            foreach (JsonElement item in list.EnumerateArray())
            {
                JsonElement trackElement = item;
                if (hasTrackWrapper && item.TryGetProperty("track", out JsonElement trackObj))
                {
                    trackElement = trackObj;
                }

                string? itemName = ReadString(trackElement, "name");
                string? itemArtist = ReadArtists(trackElement);

                if (!string.IsNullOrWhiteSpace(itemName) &&
                    string.Equals(itemName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(itemArtist) &&
                    IsArtistMatch(itemArtist, artist))
                {
                    if (trackElement.TryGetProperty("duration", out JsonElement durationEl) &&
                        durationEl.ValueKind == JsonValueKind.Number)
                    {
                        double ms = durationEl.GetDouble();
                        return ms / 1000.0;
                    }
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private static string? FindNeteaseCloudMusicDir()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string neteaseDir = Path.Combine(localAppData, "Netease", "CloudMusic");
            if (Directory.Exists(neteaseDir))
            {
                return neteaseDir;
            }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string altDir = Path.Combine(programData, "Netease", "CloudMusic");
            if (Directory.Exists(altDir))
            {
                return altDir;
            }
        }
        catch
        {
        }

        return null;
    }

    private async Task<byte[]?> TryMatchFromFileAsync(string filePath, string name, string artist, bool hasTrackWrapper)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath);
        }
        catch
        {
            return null;
        }

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

            foreach (JsonElement item in list.EnumerateArray())
            {
                JsonElement trackElement = item;
                if (hasTrackWrapper && item.TryGetProperty("track", out JsonElement trackObj))
                {
                    trackElement = trackObj;
                }

                string? itemName = ReadString(trackElement, "name");
                string? itemArtist = ReadArtists(trackElement);

                if (!string.IsNullOrWhiteSpace(itemName) &&
                    string.Equals(itemName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(itemArtist) &&
                    IsArtistMatch(itemArtist, artist))
                {
                    string? url = ReadCoverUrl(trackElement);
                    return await DownloadCoverBytesAsync(url);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private async Task<byte[]?> TrySearchCoverFromApiAsync(string name, string artist)
    {
        string query = $"{name} {artist}";

        foreach (string urlTemplate in SearchApiUrls)
        {
            try
            {
                string url = string.Format(urlTemplate, Uri.EscapeDataString(query));
                string? picUrl = await SearchCoverUrlAsync(url);
                if (!string.IsNullOrWhiteSpace(picUrl))
                {
                    byte[]? cover = await DownloadCoverBytesAsync(picUrl);
                    if (cover != null) return cover;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private async Task<string?> SearchCoverUrlAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://music.163.com/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using var response = await HttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out JsonElement result))
        {
            return null;
        }

        if (!result.TryGetProperty("songs", out JsonElement songs) || songs.GetArrayLength() == 0)
        {
            return null;
        }

        foreach (JsonElement song in songs.EnumerateArray())
        {
            if (song.TryGetProperty("album", out JsonElement album))
            {
                string? picUrl = ReadString(album, "picUrl");
                if (!string.IsNullOrWhiteSpace(picUrl))
                {
                    if (!picUrl.StartsWith("http")) picUrl = "https:" + picUrl;
                    return picUrl;
                }
            }
        }

        return null;
    }

    private static bool IsArtistMatch(string left, string right)
    {
        string a = left.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        string b = right.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> DownloadCoverBytesAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        url = EnsureHttps(url);

        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://music.163.com/");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                using var response = await HttpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch
            {
                if (attempt < maxRetries)
                {
                    await Task.Delay(300 * (1 << attempt));
                }
            }
        }

        return null;
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
