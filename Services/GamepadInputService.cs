using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HorizonRadioOverlay.Models;
using Windows.Gaming.Input;

#pragma warning disable CA1416

namespace HorizonRadioOverlay.Services;

public sealed class GamepadInputService : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceInfo = 0x2000000B;
    private const uint RimTypeHid = 2;
    private const uint RidevInputSink = 0x00000100;
    private const int RawInputHoldMilliseconds = 500;

    private readonly DispatcherTimer _timer;
    private HwndSource? _rawInputSource;
    private DiagnosticService? _diagnostic;
    private bool _rawInputRegistered;
    private long _lastRawInputDiagnosticTick;
    private bool _isRunning;
    private bool _enabled;
    private bool _prevPressed;
    private bool _nextPressed;
    private bool _togglePressed;
    private bool _toggleOverlayPressed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            SyncTimerState();
        }
    }
    public GamepadHotkeyDefinition PrevHotkey { get; set; }
    public GamepadHotkeyDefinition NextHotkey { get; set; }
    public GamepadHotkeyDefinition ToggleHotkey { get; set; }
    public GamepadHotkeyDefinition ToggleOverlayHotkey { get; set; }

    public event EventHandler? PrevTriggered;
    public event EventHandler? NextTriggered;
    public event EventHandler? ToggleTriggered;
    public event EventHandler? ToggleOverlayTriggered;

    public GamepadInputService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += Timer_Tick;
    }

    public void Start()
    {
        _isRunning = true;
        SyncTimerState();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();
    }

    public static GamepadButton GetCurrentButtonsSnapshot()
    {
        return ReadCurrentButtons();
    }

    public void AttachRawInput(Window owner, DiagnosticService? diagnostic = null)
    {
        _diagnostic = diagnostic;
        IntPtr hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _rawInputSource = HwndSource.FromHwnd(hwnd);
        _rawInputSource?.AddHook(RawInputWndProc);

        RawInputDevice[] devices =
        {
            new()
            {
                UsagePage = 0x01,
                Usage = 0x04,
                Flags = RidevInputSink,
                Target = hwnd
            },
            new()
            {
                UsagePage = 0x01,
                Usage = 0x05,
                Flags = RidevInputSink,
                Target = hwnd
            }
        };

        _rawInputRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
        if (!_rawInputRegistered)
        {
            _diagnostic?.Warn("Raw Input 手柄注册失败。");
        }
        else
        {
            _diagnostic?.Event("Raw Input 手柄监听已启用。");
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!Enabled)
        {
            _prevPressed = false;
            _nextPressed = false;
            _togglePressed = false;
            _toggleOverlayPressed = false;
            return;
        }

        GamepadButton current = ReadCurrentButtons();

        bool prevNow = IsPressed(current, PrevHotkey.Buttons);
        bool nextNow = IsPressed(current, NextHotkey.Buttons);
        bool toggleNow = IsPressed(current, ToggleHotkey.Buttons);
        bool toggleOverlayNow = IsPressed(current, ToggleOverlayHotkey.Buttons);

        if (prevNow && !_prevPressed)
        {
            PrevTriggered?.Invoke(this, EventArgs.Empty);
        }

        if (nextNow && !_nextPressed)
        {
            NextTriggered?.Invoke(this, EventArgs.Empty);
        }

        if (toggleNow && !_togglePressed)
        {
            ToggleTriggered?.Invoke(this, EventArgs.Empty);
        }

        if (toggleOverlayNow && !_toggleOverlayPressed)
        {
            ToggleOverlayTriggered?.Invoke(this, EventArgs.Empty);
        }

        _prevPressed = prevNow;
        _nextPressed = nextNow;
        _togglePressed = toggleNow;
        _toggleOverlayPressed = toggleOverlayNow;
    }

    private static bool IsPressed(GamepadButton current, GamepadButton required)
    {
        if (required == GamepadButton.None)
        {
            return false;
        }

        return (current & required) == required;
    }

    private static GamepadButton ReadCurrentButtons()
    {
        GamepadButton rawInputButtons = ReadRawInputSnapshot();
        if (_hasDualSenseRawInput)
        {
            return rawInputButtons;
        }

        GamepadButton buttons = ReadXInputButtons() | rawInputButtons;
        GamepadButton rawButtons = GamepadButton.None;

        try
        {
            foreach (RawGameController controller in RawGameController.RawGameControllers)
            {
                if (controller.HardwareVendorId == SonyVendorId)
                {
                    continue;
                }

                rawButtons |= ReadGenericRawControllerButtons(controller);
            }
        }
        catch
        {
            // RawGameController is unavailable on some stripped-down Windows editions.
        }

        return buttons == GamepadButton.None ? rawButtons : buttons;
    }

    private static GamepadButton ReadRawInputSnapshot()
    {
        long elapsedMs = Environment.TickCount64 - _lastRawInputTick;
        return elapsedMs <= RawInputHoldMilliseconds ? _lastRawInputButtons : GamepadButton.None;
    }

    private static GamepadButton ReadWindowsGamepadButtons()
    {
        GamepadButton buttons = GamepadButton.None;

        try
        {
            foreach (Windows.Gaming.Input.Gamepad gamepad in Windows.Gaming.Input.Gamepad.Gamepads)
            {
                GamepadReading reading = gamepad.GetCurrentReading();
                buttons |= MapWindowsGamepadButtons(reading.Buttons);

                if (reading.LeftTrigger >= 0.2)
                {
                    buttons |= GamepadButton.LeftTrigger;
                }

                if (reading.RightTrigger >= 0.2)
                {
                    buttons |= GamepadButton.RightTrigger;
                }
            }
        }
        catch
        {
            // Some HID devices are exposed only through RawGameController.
        }

        return buttons;
    }

    private static GamepadButton MapWindowsGamepadButtons(GamepadButtons rawButtons)
    {
        GamepadButton buttons = GamepadButton.None;

        if (rawButtons.HasFlag(GamepadButtons.DPadUp)) buttons |= GamepadButton.DPadUp;
        if (rawButtons.HasFlag(GamepadButtons.DPadDown)) buttons |= GamepadButton.DPadDown;
        if (rawButtons.HasFlag(GamepadButtons.DPadLeft)) buttons |= GamepadButton.DPadLeft;
        if (rawButtons.HasFlag(GamepadButtons.DPadRight)) buttons |= GamepadButton.DPadRight;
        if (rawButtons.HasFlag(GamepadButtons.Menu)) buttons |= GamepadButton.Start;
        if (rawButtons.HasFlag(GamepadButtons.View)) buttons |= GamepadButton.Back;
        if (rawButtons.HasFlag(GamepadButtons.LeftThumbstick)) buttons |= GamepadButton.LeftThumb;
        if (rawButtons.HasFlag(GamepadButtons.RightThumbstick)) buttons |= GamepadButton.RightThumb;
        if (rawButtons.HasFlag(GamepadButtons.LeftShoulder)) buttons |= GamepadButton.LeftShoulder;
        if (rawButtons.HasFlag(GamepadButtons.RightShoulder)) buttons |= GamepadButton.RightShoulder;
        if (rawButtons.HasFlag(GamepadButtons.A)) buttons |= GamepadButton.A;
        if (rawButtons.HasFlag(GamepadButtons.B)) buttons |= GamepadButton.B;
        if (rawButtons.HasFlag(GamepadButtons.X)) buttons |= GamepadButton.X;
        if (rawButtons.HasFlag(GamepadButtons.Y)) buttons |= GamepadButton.Y;

        return buttons;
    }

    private static GamepadButton ReadXInputButtons()
    {
        GamepadButton buttons = GamepadButton.None;
        for (uint userIndex = 0; userIndex < 4; userIndex++)
        {
            if (XInputGetState(userIndex, out XInputState state) == 0)
            {
                buttons |= MapXInputButtons(state.Gamepad);
            }
        }

        return buttons;
    }

    private static GamepadButton MapXInputButtons(XInputGamepad gamepad)
    {
        GamepadButton buttons = GamepadButton.None;
        ushort wButtons = gamepad.wButtons;

        if ((wButtons & 0x0001) != 0) buttons |= GamepadButton.DPadUp;
        if ((wButtons & 0x0002) != 0) buttons |= GamepadButton.DPadDown;
        if ((wButtons & 0x0004) != 0) buttons |= GamepadButton.DPadLeft;
        if ((wButtons & 0x0008) != 0) buttons |= GamepadButton.DPadRight;
        if ((wButtons & 0x0010) != 0) buttons |= GamepadButton.Start;
        if ((wButtons & 0x0020) != 0) buttons |= GamepadButton.Back;
        if ((wButtons & 0x0040) != 0) buttons |= GamepadButton.LeftThumb;
        if ((wButtons & 0x0080) != 0) buttons |= GamepadButton.RightThumb;
        if ((wButtons & 0x0100) != 0) buttons |= GamepadButton.LeftShoulder;
        if ((wButtons & 0x0200) != 0) buttons |= GamepadButton.RightShoulder;
        if ((wButtons & 0x1000) != 0) buttons |= GamepadButton.A;
        if ((wButtons & 0x2000) != 0) buttons |= GamepadButton.B;
        if ((wButtons & 0x4000) != 0) buttons |= GamepadButton.X;
        if ((wButtons & 0x8000) != 0) buttons |= GamepadButton.Y;

        if (gamepad.bLeftTrigger >= 30) buttons |= GamepadButton.LeftTrigger;
        if (gamepad.bRightTrigger >= 30) buttons |= GamepadButton.RightTrigger;

        return buttons;
    }

    private static GamepadButton ReadDualSenseButtons(RawGameController controller)
    {
        bool[] rawButtons = new bool[controller.ButtonCount];
        GameControllerSwitchPosition[] switches = new GameControllerSwitchPosition[controller.SwitchCount];
        double[] axes = new double[controller.AxisCount];
        controller.GetCurrentReading(rawButtons, switches, axes);

        GamepadButton buttons = GamepadButton.None;
        buttons |= RawButton(rawButtons, 0, GamepadButton.X); // Square
        buttons |= RawButton(rawButtons, 1, GamepadButton.A); // Cross
        buttons |= RawButton(rawButtons, 2, GamepadButton.B); // Circle
        buttons |= RawButton(rawButtons, 3, GamepadButton.Y); // Triangle
        buttons |= RawButton(rawButtons, 4, GamepadButton.LeftShoulder);
        buttons |= RawButton(rawButtons, 5, GamepadButton.RightShoulder);
        buttons |= RawButton(rawButtons, 6, GamepadButton.LeftTrigger);
        buttons |= RawButton(rawButtons, 7, GamepadButton.RightTrigger);
        buttons |= RawButton(rawButtons, 8, GamepadButton.Back); // Create
        buttons |= RawButton(rawButtons, 9, GamepadButton.Start); // Options
        buttons |= RawButton(rawButtons, 10, GamepadButton.LeftThumb);
        buttons |= RawButton(rawButtons, 11, GamepadButton.RightThumb);

        return buttons;
    }

    private static GamepadButton ReadGenericRawControllerButtons(RawGameController controller)
    {
        bool[] rawButtons = new bool[controller.ButtonCount];
        GameControllerSwitchPosition[] switches = new GameControllerSwitchPosition[controller.SwitchCount];
        double[] axes = new double[controller.AxisCount];
        controller.GetCurrentReading(rawButtons, switches, axes);

        GamepadButton buttons = GamepadButton.None;
        int buttonCount = Math.Min(rawButtons.Length, GenericRawButtonMap.Length);
        for (int index = 0; index < buttonCount; index++)
        {
            if (rawButtons[index])
            {
                buttons |= GenericRawButtonMap[index];
            }
        }

        if (switches.Length > 0)
        {
            buttons |= MapDPad(switches[0]);
        }

        return buttons;
    }

    private static bool IsDualSense(RawGameController controller)
    {
        if (controller.HardwareVendorId != SonyVendorId)
        {
            return false;
        }

        return controller.HardwareProductId is DualSenseProductId or DualSenseEdgeProductId;
    }

    private static GamepadButton RawButton(bool[] buttons, int index, GamepadButton mappedButton)
    {
        return index < buttons.Length && buttons[index] ? mappedButton : GamepadButton.None;
    }

    private static GamepadButton MapDPad(GameControllerSwitchPosition position)
    {
        return position switch
        {
            GameControllerSwitchPosition.Up => GamepadButton.DPadUp,
            GameControllerSwitchPosition.UpRight => GamepadButton.DPadUp | GamepadButton.DPadRight,
            GameControllerSwitchPosition.Right => GamepadButton.DPadRight,
            GameControllerSwitchPosition.DownRight => GamepadButton.DPadDown | GamepadButton.DPadRight,
            GameControllerSwitchPosition.Down => GamepadButton.DPadDown,
            GameControllerSwitchPosition.DownLeft => GamepadButton.DPadDown | GamepadButton.DPadLeft,
            GameControllerSwitchPosition.Left => GamepadButton.DPadLeft,
            GameControllerSwitchPosition.UpLeft => GamepadButton.DPadUp | GamepadButton.DPadLeft,
            _ => GamepadButton.None
        };
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _rawInputSource?.RemoveHook(RawInputWndProc);
        _rawInputSource = null;
    }

    private IntPtr RawInputWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmInput)
        {
            HandleRawInput(lParam);
        }

        return IntPtr.Zero;
    }

    private void HandleRawInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, (uint)RawInputHeaderSize) != 0 || size == 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, (uint)RawInputHeaderSize) != size)
            {
                return;
            }

            uint type = (uint)Marshal.ReadInt32(buffer);
            if (type != RimTypeHid)
            {
                return;
            }

            IntPtr deviceHandle = Marshal.ReadIntPtr(buffer, 8);
            bool hasDeviceInfo = TryGetRawInputDeviceInfo(deviceHandle, out RawInputHidDeviceInfo deviceInfo);
            if (hasDeviceInfo && !IsLikelyGameController(deviceInfo))
            {
                return;
            }

            int hidOffset = RawInputHeaderSize;
            int reportSize = Marshal.ReadInt32(buffer, hidOffset);
            int reportCount = Marshal.ReadInt32(buffer, hidOffset + 4);
            int dataOffset = hidOffset + 8;
            GamepadButton buttons = GamepadButton.None;
            bool sawDualSenseReport = false;

            for (int reportIndex = 0; reportIndex < reportCount; reportIndex++)
            {
                byte[] report = new byte[reportSize];
                Marshal.Copy(IntPtr.Add(buffer, dataOffset + reportIndex * reportSize), report, 0, reportSize);
                if (IsDualSenseHidReport(report))
                {
                    sawDualSenseReport = true;
                    buttons |= MapDualSenseHidReport(report);
                }
                LogRawInputSample(deviceInfo, report, buttons);
            }

            if (sawDualSenseReport)
            {
                _lastRawInputButtons = buttons;
                _lastRawInputTick = Environment.TickCount64;
                _hasDualSenseRawInput = true;
            }
        }
        catch (Exception ex)
        {
            _diagnostic?.Warn($"Raw Input 手柄读取失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void LogRawInputSample(RawInputHidDeviceInfo deviceInfo, byte[] report, GamepadButton buttons)
    {
        if (_diagnostic == null)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastRawInputDiagnosticTick < 1000)
        {
            return;
        }

        _lastRawInputDiagnosticTick = now;
        string bytes = string.Join(" ", report.Take(Math.Min(report.Length, 24)).Select(x => x.ToString("X2")));
        _diagnostic.Event(
            $"Raw Input 手柄报告：vid=0x{deviceInfo.VendorId:X4}, pid=0x{deviceInfo.ProductId:X4}, usage=0x{deviceInfo.UsagePage:X2}/0x{deviceInfo.Usage:X2}, len={report.Length}, buttons={buttons}, data={bytes}");
    }

    private static bool IsLikelyGameController(RawInputHidDeviceInfo deviceInfo)
    {
        return deviceInfo.UsagePage == 0x01 &&
            (deviceInfo.Usage == 0x04 || deviceInfo.Usage == 0x05);
    }

    private static bool TryGetRawInputDeviceInfo(IntPtr deviceHandle, out RawInputHidDeviceInfo deviceInfo)
    {
        deviceInfo = default;
        uint size = (uint)Marshal.SizeOf<RawInputDeviceInfo>();
        RawInputDeviceInfo info = new()
        {
            Size = size
        };

        int result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceInfo, ref info, ref size);
        if (result <= 0 || info.Type != RimTypeHid)
        {
            return false;
        }

        deviceInfo = info.Hid;
        return true;
    }

    private static GamepadButton MapDualSenseHidReport(byte[] report)
    {
        if (report.Length < 11)
        {
            return GamepadButton.None;
        }

        int buttonOffset = report[0] switch
        {
            0x01 => 8,
            0x31 => 9,
            _ => 8
        };

        GamepadButton buttons = MapDualSenseButtonBytes(report, buttonOffset);
        if (buttons == GamepadButton.None && report[0] == 0x31)
        {
            buttons = MapDualSenseButtonBytes(report, 10);
        }

        return buttons;
    }

    private static bool IsDualSenseHidReport(byte[] report)
    {
        return report.Length >= 11 && report[0] is 0x01 or 0x31;
    }

    private static GamepadButton MapDualSenseButtonBytes(byte[] report, int offset)
    {
        if (offset < 0 || offset + 1 >= report.Length)
        {
            return GamepadButton.None;
        }

        byte primary = report[offset];
        byte secondary = report[offset + 1];
        GamepadButton buttons = MapDualSenseDPad(primary);

        if ((primary & 0x10) != 0) buttons |= GamepadButton.X; // Square
        if ((primary & 0x20) != 0) buttons |= GamepadButton.A; // Cross
        if ((primary & 0x40) != 0) buttons |= GamepadButton.B; // Circle
        if ((primary & 0x80) != 0) buttons |= GamepadButton.Y; // Triangle
        if ((secondary & 0x01) != 0) buttons |= GamepadButton.LeftShoulder;
        if ((secondary & 0x02) != 0) buttons |= GamepadButton.RightShoulder;
        if ((secondary & 0x04) != 0) buttons |= GamepadButton.LeftTrigger;
        if ((secondary & 0x08) != 0) buttons |= GamepadButton.RightTrigger;
        if ((secondary & 0x10) != 0) buttons |= GamepadButton.Back;
        if ((secondary & 0x20) != 0) buttons |= GamepadButton.Start;
        if ((secondary & 0x40) != 0) buttons |= GamepadButton.LeftThumb;
        if ((secondary & 0x80) != 0) buttons |= GamepadButton.RightThumb;

        return buttons;
    }

    private static GamepadButton MapDualSenseDPad(byte value)
    {
        return (value & 0x0F) switch
        {
            0 => GamepadButton.DPadUp,
            1 => GamepadButton.DPadUp | GamepadButton.DPadRight,
            2 => GamepadButton.DPadRight,
            3 => GamepadButton.DPadDown | GamepadButton.DPadRight,
            4 => GamepadButton.DPadDown,
            5 => GamepadButton.DPadDown | GamepadButton.DPadLeft,
            6 => GamepadButton.DPadLeft,
            7 => GamepadButton.DPadUp | GamepadButton.DPadLeft,
            _ => GamepadButton.None
        };
    }

    private void SyncTimerState()
    {
        if (_isRunning && _enabled)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private const ushort SonyVendorId = 0x054C;
    private const ushort DualSenseProductId = 0x0CE6;
    private const ushort DualSenseEdgeProductId = 0x0DF2;
    private static readonly GamepadButton[] GenericRawButtonMap =
    {
        GamepadButton.Button1,
        GamepadButton.Button2,
        GamepadButton.Button3,
        GamepadButton.Button4,
        GamepadButton.Button5,
        GamepadButton.Button6,
        GamepadButton.Button7,
        GamepadButton.Button8,
        GamepadButton.Button9,
        GamepadButton.Button10,
        GamepadButton.Button11,
        GamepadButton.Button12,
        GamepadButton.Button13,
        GamepadButton.Button14,
        GamepadButton.Button15,
        GamepadButton.Button16
    };
    private static readonly int RawInputHeaderSize = 8 + IntPtr.Size * 2;
    private static GamepadButton _lastRawInputButtons;
    private static long _lastRawInputTick = long.MinValue;
    private static bool _hasDualSenseRawInput;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint dwUserIndex, out XInputState pState);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] rawInputDevices,
        uint numberDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint sizeHeader);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern int GetRawInputDeviceInfo(
        IntPtr device,
        uint command,
        ref RawInputDeviceInfo data,
        ref uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHidDeviceInfo
    {
        public uint VendorId;
        public uint ProductId;
        public uint VersionNumber;
        public ushort UsagePage;
        public ushort Usage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceInfo
    {
        public uint Size;
        public uint Type;
        public RawInputHidDeviceInfo Hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }
}

#pragma warning restore CA1416
