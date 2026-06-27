using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WhKeyboardLl = 13;
    private const uint LlkhfInjected = 0x10;
    private const uint ModNoRepeat = 0x4000;

    private const int IdPrev = 0x7011;
    private const int IdNext = 0x7012;
    private const int IdPlayPause = 0x7013;
    private const int IdToggleOverlay = 0x7014;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly Dictionary<HotkeyAction, long> _lastHookTriggers = [];
    private readonly List<int> _registeredIds = [];

    private HotkeyDefinition _prev;
    private HotkeyDefinition _next;
    private HotkeyDefinition _toggle;
    private HotkeyDefinition _toggleOverlay;
    private IntPtr _keyboardHook;
    private bool _disposed;

    public bool IsRegistered { get; private set; }

    public event EventHandler? NextRequested;
    public event EventHandler? PrevRequested;
    public event EventHandler? TogglePlayPauseRequested;
    public event EventHandler? ToggleOverlayRequested;

    public GlobalHotkeyService(Window owner)
    {
        _hwnd = new WindowInteropHelper(owner).Handle;
        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("Failed to get HwndSource.");
        _keyboardProc = KeyboardHookProc;
        _source.AddHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    public bool Register()
    {
        return Register("Ctrl+Shift+Left", "Ctrl+Shift+Right", "Ctrl+Shift+Down", "Ctrl+Shift+H");
    }

    public bool Register(string prevHotkey, string nextHotkey, string toggleHotkey, string toggleOverlayHotkey)
    {
        if (IsRegistered)
        {
            return true;
        }

        if (!HotkeyParser.TryParse(prevHotkey, out _prev) ||
            !HotkeyParser.TryParse(nextHotkey, out _next) ||
            !HotkeyParser.TryParse(toggleHotkey, out _toggle) ||
            !HotkeyParser.TryParse(toggleOverlayHotkey, out _toggleOverlay))
        {
            return false;
        }

        RegisterNativeHotkey(IdPrev, _prev);
        RegisterNativeHotkey(IdNext, _next);
        RegisterNativeHotkey(IdPlayPause, _toggle);
        RegisterNativeHotkey(IdToggleOverlay, _toggleOverlay);

        _keyboardHook = SetWindowsHookEx(
            WhKeyboardLl,
            _keyboardProc,
            GetModuleHandle(null),
            0);

        bool allNativeRegistered = _registeredIds.Count == 4;
        IsRegistered = allNativeRegistered || _keyboardHook != IntPtr.Zero;
        if (!IsRegistered)
        {
            UnregisterNativeHotkeys();
        }

        return IsRegistered;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterNativeHotkeys();

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        IsRegistered = false;
        _source.RemoveHook(WndProc);
    }

    private void RegisterNativeHotkey(int id, HotkeyDefinition hotkey)
    {
        if (RegisterHotKey(_hwnd, id, hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey))
        {
            _registeredIds.Add(id);
        }
    }

    private void UnregisterNativeHotkeys()
    {
        foreach (int id in _registeredIds)
        {
            UnregisterHotKey(_hwnd, id);
        }

        _registeredIds.Clear();
    }

    private IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((data.Flags & LlkhfInjected) == 0)
            {
                int message = wParam.ToInt32();
                if (message is WmKeyUp or WmSysKeyUp)
                {
                    _pressedKeys.Remove(data.VirtualKey);
                }
                else if (message is WmKeyDown or WmSysKeyDown && _pressedKeys.Add(data.VirtualKey))
                {
                    TryHandleHookHotkey(data.VirtualKey);
                }
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void TryHandleHookHotkey(uint virtualKey)
    {
        bool alt = IsKeyDown(0x12);
        bool control = IsKeyDown(0x11);
        bool shift = IsKeyDown(0x10);
        bool win = IsKeyDown(0x5B) || IsKeyDown(0x5C);

        if (HotkeyMatchPolicy.Matches(_prev, virtualKey, alt, control, shift, win))
        {
            Raise(HotkeyAction.Prev, fromHook: true);
        }
        else if (HotkeyMatchPolicy.Matches(_next, virtualKey, alt, control, shift, win))
        {
            Raise(HotkeyAction.Next, fromHook: true);
        }
        else if (HotkeyMatchPolicy.Matches(_toggle, virtualKey, alt, control, shift, win))
        {
            Raise(HotkeyAction.PlayPause, fromHook: true);
        }
        else if (HotkeyMatchPolicy.Matches(_toggleOverlay, virtualKey, alt, control, shift, win))
        {
            Raise(HotkeyAction.ToggleOverlay, fromHook: true);
        }
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void Raise(HotkeyAction action, bool fromHook)
    {
        long now = Environment.TickCount64;
        if (!fromHook && _lastHookTriggers.TryGetValue(action, out long hookTick) && now - hookTick < 200)
        {
            return;
        }

        if (fromHook)
        {
            _lastHookTriggers[action] = now;
        }

        _source.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (action)
            {
                case HotkeyAction.Prev:
                    PrevRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.Next:
                    NextRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.PlayPause:
                    TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ToggleOverlay:
                    ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        HotkeyAction? action = wParam.ToInt32() switch
        {
            IdPrev => HotkeyAction.Prev,
            IdNext => HotkeyAction.Next,
            IdPlayPause => HotkeyAction.PlayPause,
            IdToggleOverlay => HotkeyAction.ToggleOverlay,
            _ => null
        };

        if (action.HasValue)
        {
            Raise(action.Value, fromHook: false);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private enum HotkeyAction
    {
        Prev,
        Next,
        PlayPause,
        ToggleOverlay
    }
}
