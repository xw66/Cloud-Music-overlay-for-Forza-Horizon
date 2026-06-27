using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Tests;

public sealed class GamepadHotkeyParserTests
{
    [Fact]
    public void TryParse_AcceptsDualSenseButtonNames()
    {
        Assert.True(GamepadHotkeyParser.TryParse("L2+R2+Triangle", out GamepadHotkeyDefinition hotkey));

        Assert.Equal(
            GamepadButton.LeftTrigger | GamepadButton.RightTrigger | GamepadButton.Y,
            hotkey.Buttons);
    }

    [Fact]
    public void TryParse_MapsCreateAndOptions()
    {
        Assert.True(GamepadHotkeyParser.TryParse("Create+Options", out GamepadHotkeyDefinition hotkey));

        Assert.Equal(GamepadButton.Back | GamepadButton.Start, hotkey.Buttons);
    }

    [Fact]
    public void TryParse_AcceptsGenericRawControllerButtonNames()
    {
        Assert.True(GamepadHotkeyParser.TryParse("Button1+Button16", out GamepadHotkeyDefinition hotkey));

        Assert.Equal(GamepadButton.Button1 | GamepadButton.Button16, hotkey.Buttons);
    }
}
