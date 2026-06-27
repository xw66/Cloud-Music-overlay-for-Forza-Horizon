using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public static class HotkeyMatchPolicy
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static bool Matches(
        HotkeyDefinition hotkey,
        uint virtualKey,
        bool alt,
        bool control,
        bool shift,
        bool win)
    {
        if (hotkey.VirtualKey != virtualKey)
        {
            return false;
        }

        return HasModifier(hotkey.Modifiers, ModAlt) == alt &&
               HasModifier(hotkey.Modifiers, ModControl) == control &&
               HasModifier(hotkey.Modifiers, ModShift) == shift &&
               HasModifier(hotkey.Modifiers, ModWin) == win;
    }

    private static bool HasModifier(uint modifiers, uint modifier) => (modifiers & modifier) != 0;
}
