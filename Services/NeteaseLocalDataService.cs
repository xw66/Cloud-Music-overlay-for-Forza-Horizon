using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseLocalDataService
{
    private readonly string _playingListPath;
    private readonly string _fmPlayPath;
    private readonly CoverCacheService _coverCache;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
    };

    public NeteaseLocalDataService(CoverCacheService coverCache)
    {
        _coverCache = coverCache;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _playingListPath = Path.Combine(localAppData, "Netease", "CloudMusic", "webdata", "file", "playingList");
        _fmPlayPath = Path.Combine(localAppData, "Netease", "CloudMusic", "webdata", "file", "fmPlay");
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

        return new TrackInfo
        {
            Name = liveTrack.Name,
            Artist = liveTrack.Artist,
            SourceAppId = coverBytes == null ? "CloudMusic(ProcessTitle)" : "CloudMusic(ProcessTitle+Cover)",
            CoverBytes = coverBytes
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
        byte[]? result = await TryMatchFromFileAsync(_playingListPath, name, artist, hasTrackWrapper: true);
        if (result != null) return result;

        result = await TryMatchFromFileAsync(_fmPlayPath, name, artist, hasTrackWrapper: false);
        if (result != null) return result;

        return await TrySearchCoverFromApiAsync(name, artist);
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
        try
        {
            string query = Uri.EscapeDataString($"{name} {artist}");
            string apiUrl = $"https://music.163.com/api/search/get?s={query}&type=1&limit=5";

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Referrer = new Uri("https://music.163.com/");

            using var response = await HttpClient.SendAsync(request);
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
                string? songName = ReadString(song, "name");
                if (!string.IsNullOrWhiteSpace(songName) &&
                    string.Equals(songName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    if (song.TryGetProperty("album", out JsonElement album))
                    {
                        string? picUrl = ReadString(album, "picUrl");
                        if (!string.IsNullOrWhiteSpace(picUrl))
                        {
                            if (!picUrl.StartsWith("http"))
                            {
                                picUrl = "https:" + picUrl;
                            }
                            return await DownloadCoverBytesAsync(picUrl);
                        }
                    }
                }
            }
        }
        catch
        {
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
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        const int maxRetries = 2;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await HttpClient.GetByteArrayAsync(url);
            }
            catch
            {
                if (attempt < maxRetries)
                {
                    await Task.Delay(500 * (attempt + 1));
                }
            }
        }

        return null;
    }
}
