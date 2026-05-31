using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseLocalDataService
{
    private const int WmAppcommand = 0x0319;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int AppcommandMediaNexttrack = 11;
    private const int AppcommandMediaPrevioustrack = 12;
    private const int AppcommandMediaPlayPause = 14;
    private const int VkControl = 0x11;
    private const int VkLeft = 0x25;
    private const int VkRight = 0x27;
    private const int VkSpace = 0x20;

    private readonly string _playingListPath;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1200)
    };

    private string _lastCoverTrackKey = string.Empty;
    private byte[]? _lastCoverBytes;

    public NeteaseLocalDataService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _playingListPath = Path.Combine(localAppData, "Netease", "CloudMusic", "webdata", "file", "playingList");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
        byte[]? coverBytes;

        if (string.Equals(key, _lastCoverTrackKey, StringComparison.Ordinal))
        {
            coverBytes = _lastCoverBytes;
        }
        else
        {
            coverBytes = await TryGetCoverBytesFromPlayingListAsync(liveTrack.Name, liveTrack.Artist);
            _lastCoverTrackKey = key;
            _lastCoverBytes = coverBytes;
        }

        return new TrackInfo
        {
            Name = liveTrack.Name,
            Artist = liveTrack.Artist,
            SourceAppId = coverBytes == null ? "CloudMusic(ProcessTitle)" : "CloudMusic(ProcessTitle+Cover)",
            CoverBytes = coverBytes
        };
    }

    public Task<bool> NextAsync()
    {
        return Task.FromResult(SendMediaKeyFallback(AppcommandMediaNexttrack));
    }

    public Task<bool> PreviousAsync()
    {
        return Task.FromResult(SendMediaKeyFallback(AppcommandMediaPrevioustrack));
    }

    public Task<bool> TogglePlayPauseAsync()
    {
        return Task.FromResult(SendMediaKeyFallback(AppcommandMediaPlayPause));
    }


    private static bool SendAppCommand(int command)
    {
        IntPtr hwnd = NativeWindowFinder.FindNeteaseMainWindow();
        bool success = false;

        if (hwnd != IntPtr.Zero)
        {
            IntPtr lParam = (IntPtr)(command << 16);

            // Try both sync and async command delivery.
            _ = SendMessage(hwnd, WmAppcommand, hwnd, lParam);
            _ = PostMessage(hwnd, WmAppcommand, hwnd, lParam);
            success = true;

            // Some NetEase desktop versions respond better to simulated shortcut keys.
            bool keySent = command switch
            {
                AppcommandMediaNexttrack => SendShortcutToWindow(hwnd, VkRight),
                AppcommandMediaPrevioustrack => SendShortcutToWindow(hwnd, VkLeft),
                AppcommandMediaPlayPause => SendShortcutSingleKeyToWindow(hwnd, VkSpace),
                _ => false
            };

            success = success || keySent;
        }

        bool mediaFallback = SendMediaKeyFallback(command);
        return success || mediaFallback;
    }

    private static bool SendShortcutToWindow(IntPtr hwnd, int vk)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        bool ok = true;
        ok &= PostMessage(hwnd, WmKeydown, (IntPtr)VkControl, IntPtr.Zero) != IntPtr.Zero;
        ok &= PostMessage(hwnd, WmKeydown, (IntPtr)vk, IntPtr.Zero) != IntPtr.Zero;
        ok &= PostMessage(hwnd, WmKeyup, (IntPtr)vk, IntPtr.Zero) != IntPtr.Zero;
        ok &= PostMessage(hwnd, WmKeyup, (IntPtr)VkControl, IntPtr.Zero) != IntPtr.Zero;
        return ok;
    }

    private static bool SendShortcutSingleKeyToWindow(IntPtr hwnd, int vk)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        bool ok = true;
        ok &= PostMessage(hwnd, WmKeydown, (IntPtr)vk, IntPtr.Zero) != IntPtr.Zero;
        ok &= PostMessage(hwnd, WmKeyup, (IntPtr)vk, IntPtr.Zero) != IntPtr.Zero;
        return ok;
    }

    private static bool SendMediaKeyFallback(int command)
    {
        ushort vk = command switch
        {
            AppcommandMediaNexttrack => 0xB0,
            AppcommandMediaPrevioustrack => 0xB1,
            AppcommandMediaPlayPause => 0xB3,
            _ => (ushort)0
        };

        if (vk == 0)
        {
            return false;
        }

        INPUT down = new()
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 0
                }
            }
        };

        INPUT up = new()
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = 0x0002
                }
            }
        };

        INPUT[] inputs = { down, up };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static class NativeWindowFinder
    {
        public static IntPtr FindNeteaseMainWindow()
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("cloudmusic")
                .Where(p => p.MainWindowHandle != IntPtr.Zero)
                .ToList();

            var titled = processes
                .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                .OrderByDescending(p => p.StartTime)
                .FirstOrDefault();

            if (titled != null)
            {
                return titled.MainWindowHandle;
            }

            var fallback = processes.OrderByDescending(p => p.StartTime).FirstOrDefault();
            if (fallback != null)
            {
                return fallback.MainWindowHandle;
            }

            return IntPtr.Zero;
        }
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
        if (!File.Exists(_playingListPath))
        {
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(_playingListPath);
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
            if (!doc.RootElement.TryGetProperty("list", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? exactTrack = null;
            JsonElement? firstTrack = null;

            foreach (JsonElement item in list.EnumerateArray())
            {
                JsonElement trackElement = item;
                if (item.TryGetProperty("track", out JsonElement trackObj))
                {
                    trackElement = trackObj;
                }

                if (firstTrack == null)
                {
                    firstTrack = trackElement;
                }

                string? itemName = ReadString(trackElement, "name");
                string? itemArtist = ReadArtists(trackElement);

                if (!string.IsNullOrWhiteSpace(itemName) &&
                    string.Equals(itemName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(itemArtist) &&
                    IsArtistMatch(itemArtist, artist))
                {
                    exactTrack = trackElement;
                    break;
                }
            }

            JsonElement? targetTrack = exactTrack ?? firstTrack;
            if (targetTrack == null)
            {
                return null;
            }

            string? url = ReadCoverUrl(targetTrack.Value);
            return await DownloadCoverBytesAsync(url);
        }
        catch
        {
            return null;
        }
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

        try
        {
            return await HttpClient.GetByteArrayAsync(url);
        }
        catch
        {
            return null;
        }
    }
}
