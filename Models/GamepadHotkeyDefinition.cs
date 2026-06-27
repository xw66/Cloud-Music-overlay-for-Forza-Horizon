using System.Collections.Generic;

namespace HorizonRadioOverlay.Models;

[Flags]
public enum GamepadButton
{
    None = 0,
    DPadUp = 1 << 0,
    DPadDown = 1 << 1,
    DPadLeft = 1 << 2,
    DPadRight = 1 << 3,
    Start = 1 << 4,
    Back = 1 << 5,
    LeftThumb = 1 << 6,
    RightThumb = 1 << 7,
    LeftShoulder = 1 << 8,
    RightShoulder = 1 << 9,
    A = 1 << 10,
    B = 1 << 11,
    X = 1 << 12,
    Y = 1 << 13,
    LeftTrigger = 1 << 14,
    RightTrigger = 1 << 15,
    Button1 = 1 << 16,
    Button2 = 1 << 17,
    Button3 = 1 << 18,
    Button4 = 1 << 19,
    Button5 = 1 << 20,
    Button6 = 1 << 21,
    Button7 = 1 << 22,
    Button8 = 1 << 23,
    Button9 = 1 << 24,
    Button10 = 1 << 25,
    Button11 = 1 << 26,
    Button12 = 1 << 27,
    Button13 = 1 << 28,
    Button14 = 1 << 29,
    Button15 = 1 << 30,
    Button16 = 1 << 31
}

public readonly struct GamepadHotkeyDefinition
{
    public GamepadButton Buttons { get; }

    public GamepadHotkeyDefinition(GamepadButton buttons)
    {
        Buttons = buttons;
    }
}

public static class GamepadHotkeyParser
{
    private static readonly Dictionary<string, GamepadButton> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = GamepadButton.A,
        ["CROSS"] = GamepadButton.A,
        ["B"] = GamepadButton.B,
        ["CIRCLE"] = GamepadButton.B,
        ["X"] = GamepadButton.X,
        ["SQUARE"] = GamepadButton.X,
        ["Y"] = GamepadButton.Y,
        ["TRIANGLE"] = GamepadButton.Y,
        ["LB"] = GamepadButton.LeftShoulder,
        ["L1"] = GamepadButton.LeftShoulder,
        ["RB"] = GamepadButton.RightShoulder,
        ["R1"] = GamepadButton.RightShoulder,
        ["LT"] = GamepadButton.LeftTrigger,
        ["L2"] = GamepadButton.LeftTrigger,
        ["RT"] = GamepadButton.RightTrigger,
        ["R2"] = GamepadButton.RightTrigger,
        ["LS"] = GamepadButton.LeftThumb,
        ["L3"] = GamepadButton.LeftThumb,
        ["RS"] = GamepadButton.RightThumb,
        ["R3"] = GamepadButton.RightThumb,
        ["BACK"] = GamepadButton.Back,
        ["VIEW"] = GamepadButton.Back,
        ["CREATE"] = GamepadButton.Back,
        ["START"] = GamepadButton.Start,
        ["MENU"] = GamepadButton.Start,
        ["OPTIONS"] = GamepadButton.Start,
        ["UP"] = GamepadButton.DPadUp,
        ["DOWN"] = GamepadButton.DPadDown,
        ["LEFT"] = GamepadButton.DPadLeft,
        ["RIGHT"] = GamepadButton.DPadRight,
        ["DPAD_UP"] = GamepadButton.DPadUp,
        ["DPAD_DOWN"] = GamepadButton.DPadDown,
        ["DPAD_LEFT"] = GamepadButton.DPadLeft,
        ["DPAD_RIGHT"] = GamepadButton.DPadRight,
        ["BUTTON1"] = GamepadButton.Button1,
        ["BUTTON2"] = GamepadButton.Button2,
        ["BUTTON3"] = GamepadButton.Button3,
        ["BUTTON4"] = GamepadButton.Button4,
        ["BUTTON5"] = GamepadButton.Button5,
        ["BUTTON6"] = GamepadButton.Button6,
        ["BUTTON7"] = GamepadButton.Button7,
        ["BUTTON8"] = GamepadButton.Button8,
        ["BUTTON9"] = GamepadButton.Button9,
        ["BUTTON10"] = GamepadButton.Button10,
        ["BUTTON11"] = GamepadButton.Button11,
        ["BUTTON12"] = GamepadButton.Button12,
        ["BUTTON13"] = GamepadButton.Button13,
        ["BUTTON14"] = GamepadButton.Button14,
        ["BUTTON15"] = GamepadButton.Button15,
        ["BUTTON16"] = GamepadButton.Button16
    };

    public static bool TryParse(string text, out GamepadHotkeyDefinition definition)
    {
        definition = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        GamepadButton buttons = GamepadButton.None;
        foreach (string token in tokens)
        {
            string normalized = token.Replace('＋', '+').Trim().ToUpperInvariant();
            if (!TokenMap.TryGetValue(normalized, out GamepadButton button))
            {
                return false;
            }

            buttons |= button;
        }

        if (buttons == GamepadButton.None)
        {
            return false;
        }

        definition = new GamepadHotkeyDefinition(buttons);
        return true;
    }
}
