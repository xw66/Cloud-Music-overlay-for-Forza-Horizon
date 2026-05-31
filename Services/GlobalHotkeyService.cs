using System;
using System.Runtime.InteropServices;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;

    private const int IdPrev = 0x7011;
    private const int IdNext = 0x7012;
    private const int IdPlayPause = 0x7013;

    private readonly IntPtr _hwnd;
    private bool _registered;

    public bool IsRegistered => _registered;

    public event EventHandler? NextRequested;
    public event EventHandler? PrevRequested;
    public event EventHandler? TogglePlayPauseRequested;

    public GlobalHotkeyService(Microsoft.UI.Xaml.Window owner)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public bool Register() => Register("Ctrl+Shift+Left", "Ctrl+Shift+Right", "Ctrl+Shift+Down");

    public bool Register(string prevHotkey, string nextHotkey, string toggleHotkey)
    {
        if (_registered) return true;

        if (!HotkeyParser.TryParse(prevHotkey, out HotkeyDefinition prev) ||
            !HotkeyParser.TryParse(nextHotkey, out HotkeyDefinition next) ||
            !HotkeyParser.TryParse(toggleHotkey, out HotkeyDefinition toggle))
        {
            return false;
        }

        bool okPrev = RegisterHotKey(_hwnd, IdPrev, prev.Modifiers, prev.VirtualKey);
        bool okNext = RegisterHotKey(_hwnd, IdNext, next.Modifiers, next.VirtualKey);
        bool okPause = RegisterHotKey(_hwnd, IdPlayPause, toggle.Modifiers, toggle.VirtualKey);

        _registered = okPrev && okNext && okPause;

        if (!_registered)
        {
            if (okPrev) UnregisterHotKey(_hwnd, IdPrev);
            if (okNext) UnregisterHotKey(_hwnd, IdNext);
            if (okPause) UnregisterHotKey(_hwnd, IdPlayPause);
        }

        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, IdPrev);
            UnregisterHotKey(_hwnd, IdNext);
            UnregisterHotKey(_hwnd, IdPlayPause);
            _registered = false;
        }
    }
}
