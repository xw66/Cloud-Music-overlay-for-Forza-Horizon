using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class ForegroundGameDetector : IDisposable
{
    private const int TitleCapacity = 512;
    private readonly GameProfileService _profileService;
    private readonly DispatcherTimer _timer;
    private string _lastProfileId = string.Empty;

    public ForegroundGameDetector(GameProfileService profileService)
    {
        _profileService = profileService;
        CurrentProfile = _profileService.GetByIdOrDefault("generic-game");
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _timer.Tick += (_, _) => Detect();
    }

    public event EventHandler<GameProfile>? ActiveGameChanged;

    public GameProfile CurrentProfile { get; private set; }

    public void Start()
    {
        Detect();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        Stop();
    }

    private void Detect()
    {
        (string? processName, string? windowTitle) = GetForegroundWindowInfo();
        GameProfile profile = _profileService.MatchOrDefault(processName, windowTitle);
        if (string.Equals(profile.Id, _lastProfileId, StringComparison.OrdinalIgnoreCase))
        {
            CurrentProfile = profile;
            return;
        }

        _lastProfileId = profile.Id;
        CurrentProfile = profile;
        ActiveGameChanged?.Invoke(this, profile);
    }

    private static (string? ProcessName, string? WindowTitle) GetForegroundWindowInfo()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return (null, null);
        }

        string? title = GetWindowTitle(hwnd);
        _ = GetWindowThreadProcessId(hwnd, out uint processId);
        string? processName = GetProcessFileName(processId);
        return (processName, title);
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        StringBuilder builder = new(TitleCapacity);
        int length = GetWindowText(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : null;
    }

    private static string? GetProcessFileName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string processName = process.ProcessName;
            return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : $"{processName}.exe";
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
