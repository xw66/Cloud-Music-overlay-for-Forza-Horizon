using System;
using System.Runtime.InteropServices;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class NeteaseShortcutSender
{
    private const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    public bool Send(string hotkeyText)
    {
        if (!HotkeyParser.TryParse(hotkeyText, out HotkeyDefinition hotkey))
        {
            return false;
        }

        byte[] modifierKeys = GetModifierVks(hotkey.Modifiers);
        byte key = (byte)hotkey.VirtualKey;

        foreach (byte mod in modifierKeys)
        {
            keybd_event(mod, 0, 0, IntPtr.Zero);
        }

        keybd_event(key, 0, 0, IntPtr.Zero);
        keybd_event(key, 0, KeyeventfKeyup, IntPtr.Zero);

        for (int i = modifierKeys.Length - 1; i >= 0; i--)
        {
            keybd_event(modifierKeys[i], 0, KeyeventfKeyup, IntPtr.Zero);
        }

        return true;
    }

    private static byte[] GetModifierVks(uint modifiers)
    {
        System.Collections.Generic.List<byte> keys = new();
        if ((modifiers & 0x0002) != 0)
        {
            keys.Add(0x11);
        }

        if ((modifiers & 0x0001) != 0)
        {
            keys.Add(0x12);
        }

        if ((modifiers & 0x0004) != 0)
        {
            keys.Add(0x10);
        }

        if ((modifiers & 0x0008) != 0)
        {
            keys.Add(0x5B);
        }

        return keys.ToArray();
    }
}
