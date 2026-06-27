using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class HotkeyMatchPolicyTests
{
    [Fact]
    public void Matches_returns_true_for_exact_hotkey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+Left", out HotkeyDefinition hotkey));

        bool result = HotkeyMatchPolicy.Matches(
            hotkey,
            virtualKey: 0x25,
            alt: false,
            control: true,
            shift: true,
            win: false);

        Assert.True(result);
    }

    [Fact]
    public void Matches_returns_false_when_an_extra_modifier_is_pressed()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+Left", out HotkeyDefinition hotkey));

        bool result = HotkeyMatchPolicy.Matches(
            hotkey,
            virtualKey: 0x25,
            alt: true,
            control: true,
            shift: true,
            win: false);

        Assert.False(result);
    }

    [Fact]
    public void Matches_returns_false_for_a_different_key()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+Left", out HotkeyDefinition hotkey));

        bool result = HotkeyMatchPolicy.Matches(
            hotkey,
            virtualKey: 0x27,
            alt: false,
            control: true,
            shift: true,
            win: false);

        Assert.False(result);
    }
}
