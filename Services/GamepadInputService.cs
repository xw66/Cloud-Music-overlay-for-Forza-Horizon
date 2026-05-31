using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class GamepadInputService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private bool _prevPressed;
    private bool _nextPressed;
    private bool _togglePressed;

    public bool Enabled { get; set; }
    public GamepadHotkeyDefinition PrevHotkey { get; set; }
    public GamepadHotkeyDefinition NextHotkey { get; set; }
    public GamepadHotkeyDefinition ToggleHotkey { get; set; }

    public event EventHandler? PrevTriggered;
    public event EventHandler? NextTriggered;
    public event EventHandler? ToggleTriggered;

    public GamepadInputService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += Timer_Tick;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public static GamepadButton GetCurrentButtonsSnapshot()
    {
        return ReadCurrentButtons();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!Enabled)
        {
            _prevPressed = false;
            _nextPressed = false;
            _togglePressed = false;
            return;
        }

        GamepadButton current = ReadCurrentButtons();

        bool prevNow = IsPressed(current, PrevHotkey.Buttons);
        bool nextNow = IsPressed(current, NextHotkey.Buttons);
        bool toggleNow = IsPressed(current, ToggleHotkey.Buttons);

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

        _prevPressed = prevNow;
        _nextPressed = nextNow;
        _togglePressed = toggleNow;
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
        uint result = XInputGetState(0, out XInputState state);
        if (result != 0)
        {
            return GamepadButton.None;
        }

        GamepadButton buttons = GamepadButton.None;
        ushort wButtons = state.Gamepad.wButtons;

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

        if (state.Gamepad.bLeftTrigger >= 30) buttons |= GamepadButton.LeftTrigger;
        if (state.Gamepad.bRightTrigger >= 30) buttons |= GamepadButton.RightTrigger;

        return buttons;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint dwUserIndex, out XInputState pState);

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
