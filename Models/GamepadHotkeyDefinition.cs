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
    RightTrigger = 1 << 15
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
        ["B"] = GamepadButton.B,
        ["X"] = GamepadButton.X,
        ["Y"] = GamepadButton.Y,
        ["LB"] = GamepadButton.LeftShoulder,
        ["RB"] = GamepadButton.RightShoulder,
        ["LT"] = GamepadButton.LeftTrigger,
        ["RT"] = GamepadButton.RightTrigger,
        ["LS"] = GamepadButton.LeftThumb,
        ["RS"] = GamepadButton.RightThumb,
        ["BACK"] = GamepadButton.Back,
        ["VIEW"] = GamepadButton.Back,
        ["START"] = GamepadButton.Start,
        ["MENU"] = GamepadButton.Start,
        ["UP"] = GamepadButton.DPadUp,
        ["DOWN"] = GamepadButton.DPadDown,
        ["LEFT"] = GamepadButton.DPadLeft,
        ["RIGHT"] = GamepadButton.DPadRight,
        ["DPAD_UP"] = GamepadButton.DPadUp,
        ["DPAD_DOWN"] = GamepadButton.DPadDown,
        ["DPAD_LEFT"] = GamepadButton.DPadLeft,
        ["DPAD_RIGHT"] = GamepadButton.DPadRight
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
