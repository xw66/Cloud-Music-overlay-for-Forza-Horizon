using System;
using System.Collections.Generic;

namespace HorizonRadioOverlay.Models;

public readonly struct HotkeyDefinition
{
    public uint Modifiers { get; }
    public uint VirtualKey { get; }

    public HotkeyDefinition(uint modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }
}

public static class HotkeyParser
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private static readonly Dictionary<string, uint> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["SPACE"] = 0x20,
        ["P"] = 0x50,
        ["MEDIA_NEXT"] = 0xB0,
        ["MEDIA_PREV"] = 0xB1,
        ["MEDIA_PLAY_PAUSE"] = 0xB3,
        ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        ["SEMICOLON"] = 0xBA,
        ["APOSTROPHE"] = 0xDE,
        ["COMMA"] = 0xBC,
        ["PERIOD"] = 0xBE,
        ["SLASH"] = 0xBF,
        ["BACKSLASH"] = 0xDC,
        ["LBRACKET"] = 0xDB,
        ["RBRACKET"] = 0xDD,
        ["MINUS"] = 0xBD,
        ["EQUALS"] = 0xBB,
        ["GRAVE"] = 0xC0,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        [","] = 0xBC,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        ["\\"] = 0xDC,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        ["-"] = 0xBD,
        ["="] = 0xBB,
        ["`"] = 0xC0
    };

    public static bool TryParse(string text, out HotkeyDefinition hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = Normalize(text);

        string[] tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        uint modifiers = 0;
        uint vk = 0;

        foreach (string tokenRaw in tokens)
        {
            string token = tokenRaw.Trim();
            if (token.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || token.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModControl;
                continue;
            }

            if (token.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModAlt;
                continue;
            }

            if (token.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModShift;
                continue;
            }

            if (token.Equals("WIN", StringComparison.OrdinalIgnoreCase) || token.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModWin;
                continue;
            }

            if (vk != 0)
            {
                return false;
            }

            if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
            {
                char ch = char.ToUpperInvariant(token[0]);
                vk = ch;
                continue;
            }

            if (VkMap.TryGetValue(token.ToUpperInvariant(), out uint mapped))
            {
                vk = mapped;
                continue;
            }

            return false;
        }

        if (vk == 0)
        {
            return false;
        }

        hotkey = new HotkeyDefinition(modifiers, vk);
        return true;
    }

    private static string Normalize(string input)
    {
        return input
            .Trim()
            .Replace('＋', '+')
            .Replace('，', ',')
            .Replace('。', '.')
            .Replace('；', ';')
            .Replace('：', ';')
            .Replace('、', '\\')
            .Replace('‘', '\'')
            .Replace('’', '\'')
            .Replace('“', '"')
            .Replace('”', '"');
    }
}
