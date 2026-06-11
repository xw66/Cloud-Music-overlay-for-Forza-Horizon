using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;

    private const int IdPrev = 0x7011;
    private const int IdNext = 0x7012;
    private const int IdPlayPause = 0x7013;
    private const int IdToggleOverlay = 0x7014;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;

    private bool _registered;

    public bool IsRegistered => _registered;

    public event EventHandler? NextRequested;

    public event EventHandler? PrevRequested;

    public event EventHandler? TogglePlayPauseRequested;

    public event EventHandler? ToggleOverlayRequested;

    public GlobalHotkeyService(Window owner)
    {
        _hwnd = new WindowInteropHelper(owner).Handle;
        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("Failed to get HwndSource.");
        _source.AddHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public bool Register()
    {
        return Register("Ctrl+Shift+Left", "Ctrl+Shift+Right", "Ctrl+Shift+Down", "Ctrl+Shift+H");
    }

    public bool Register(string prevHotkey, string nextHotkey, string toggleHotkey, string toggleOverlayHotkey)
    {
        if (_registered)
        {
            return true;
        }

        if (!HotkeyParser.TryParse(prevHotkey, out HotkeyDefinition prev) ||
            !HotkeyParser.TryParse(nextHotkey, out HotkeyDefinition next) ||
            !HotkeyParser.TryParse(toggleHotkey, out HotkeyDefinition toggle) ||
            !HotkeyParser.TryParse(toggleOverlayHotkey, out HotkeyDefinition toggleOverlay))
        {
            return false;
        }

        bool okPrev = RegisterHotKey(_hwnd, IdPrev, prev.Modifiers, prev.VirtualKey);
        bool okNext = RegisterHotKey(_hwnd, IdNext, next.Modifiers, next.VirtualKey);
        bool okPause = RegisterHotKey(_hwnd, IdPlayPause, toggle.Modifiers, toggle.VirtualKey);
        bool okToggleOverlay = RegisterHotKey(_hwnd, IdToggleOverlay, toggleOverlay.Modifiers, toggleOverlay.VirtualKey);

        _registered = okPrev && okNext && okPause && okToggleOverlay;

        if (!_registered)
        {
            if (okPrev)
            {
                UnregisterHotKey(_hwnd, IdPrev);
            }

            if (okNext)
            {
                UnregisterHotKey(_hwnd, IdNext);
            }

            if (okPause)
            {
                UnregisterHotKey(_hwnd, IdPlayPause);
            }

            if (okToggleOverlay)
            {
                UnregisterHotKey(_hwnd, IdToggleOverlay);
            }
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
            UnregisterHotKey(_hwnd, IdToggleOverlay);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();

            switch (id)
            {
                case IdPrev:
                    PrevRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case IdNext:
                    NextRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case IdPlayPause:
                    TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
                case IdToggleOverlay:
                    ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }
}
