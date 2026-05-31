using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class GamepadInputService : IDisposable
{
    private readonly DispatcherQueueTimer _timer;
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
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (s, e) => Timer_Tick();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public static GamepadButton GetCurrentButtonsSnapshot() => ReadCurrentButtons();

    private void Timer_Tick()
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

        if (prevNow && !_prevPressed) PrevTriggered?.Invoke(this, EventArgs.Empty);
        if (nextNow && !_nextPressed) NextTriggered?.Invoke(this, EventArgs.Empty);
        if (toggleNow && !_togglePressed) ToggleTriggered?.Invoke(this, EventArgs.Empty);

        _prevPressed = prevNow;
        _nextPressed = nextNow;
        _togglePressed = toggleNow;
    }

    private static bool IsPressed(GamepadButton current, GamepadButton required)
    {
        if (required == GamepadButton.None) return false;
        return (current & required) == required;
    }

    private static GamepadButton ReadCurrentButtons()
    {
        uint result = XInputGetState(0, out XInputState state);
        if (result != 0) return GamepadButton.None;

        GamepadButton buttons = GamepadButton.None;
        ushort w = state.Gamepad.wButtons;

        if ((w & 0x0001) != 0) buttons |= GamepadButton.DPadUp;
        if ((w & 0x0002) != 0) buttons |= GamepadButton.DPadDown;
        if ((w & 0x0004) != 0) buttons |= GamepadButton.DPadLeft;
        if ((w & 0x0008) != 0) buttons |= GamepadButton.DPadRight;
        if ((w & 0x0010) != 0) buttons |= GamepadButton.Start;
        if ((w & 0x0020) != 0) buttons |= GamepadButton.Back;
        if ((w & 0x0040) != 0) buttons |= GamepadButton.LeftThumb;
        if ((w & 0x0080) != 0) buttons |= GamepadButton.RightThumb;
        if ((w & 0x0100) != 0) buttons |= GamepadButton.LeftShoulder;
        if ((w & 0x0200) != 0) buttons |= GamepadButton.RightShoulder;
        if ((w & 0x1000) != 0) buttons |= GamepadButton.A;
        if ((w & 0x2000) != 0) buttons |= GamepadButton.B;
        if ((w & 0x4000) != 0) buttons |= GamepadButton.X;
        if ((w & 0x8000) != 0) buttons |= GamepadButton.Y;
        if (state.Gamepad.bLeftTrigger >= 30) buttons |= GamepadButton.LeftTrigger;
        if (state.Gamepad.bRightTrigger >= 30) buttons |= GamepadButton.RightTrigger;

        return buttons;
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint dwPacketNumber; public XInputGamepad Gamepad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }
}
